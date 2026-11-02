"""Disposable Docker verification: migrations, replay, failure atomicity, backup, restore, restart.

Runs only its own randomly named container; never reads production credentials or volumes.
"""
import datetime
import hashlib
import json
from pathlib import Path
import secrets
import shutil
import subprocess
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]


def run(args, **kwargs):
    return subprocess.run(args, check=True, capture_output=True, **kwargs)


def main():
    container = 'tixflow-phase12-check-' + uuid.uuid4().hex[:12]
    started = time.monotonic()
    with tempfile.TemporaryDirectory(prefix='phase12-') as directory:
        fixture = Path(directory)
        shutil.copytree(ROOT / 'database', fixture / 'database')
        shutil.copy(ROOT / 'deploy/scripts/migrate.sh', fixture / 'migrate.sh')
        password = secrets.token_hex(32)
        run(['docker', 'run', '-d', '--name', container, '-e', 'POSTGRES_USER=tixflow',
             '-e', 'POSTGRES_DB=tixflow', '-e', 'POSTGRES_PASSWORD=' + password,
             '--health-cmd', 'pg_isready -U tixflow -d tixflow', '--health-interval', '1s',
             '--health-retries', '60', 'postgres:16-alpine'])
        try:
            for _ in range(90):
                state = run(['docker', 'inspect', '--format', '{{.State.Health.Status}}', container]).stdout.strip()
                if state == b'healthy':
                    break
                time.sleep(1)
            else:
                raise RuntimeError('Test PostgreSQL did not become healthy')
            run(['docker', 'cp', str(fixture / 'database'), container + ':/database'])
            run(['docker', 'cp', str(fixture / 'migrate.sh'), container + ':/migrate.sh'])

            def sql(text, database='tixflow', check=True):
                return subprocess.run(['docker', 'exec', '-i', container, 'psql', '-X', '-U', 'tixflow',
                                       '-d', database, '-v', 'ON_ERROR_STOP=1', '-At'],
                                      input=text.encode(), capture_output=True, check=check)

            def migrate(check=True):
                return subprocess.run(['docker', 'exec', '-e', 'PGUSER=tixflow', '-e', 'PGDATABASE=tixflow',
                                       container, 'sh', '/migrate.sh'], capture_output=True, check=check)

            migrate()
            initial = sql('SELECT count(*) FROM deployment_migrations;').stdout
            assert int(initial) == 11, initial
            migrate()
            assert sql('SELECT count(*) FROM deployment_migrations;').stdout == initial
            # Verify checksums block edited historical migrations.
            migration = fixture / 'database/migrations/002_keycloak_identity.sql'
            original = migration.read_bytes()
            migration.write_bytes(original + b'\n-- changed history\n')
            run(['docker', 'cp', str(migration), container + ':/database/migrations/002_keycloak_identity.sql'])
            assert migrate(check=False).returncode != 0
            migration.write_bytes(original)
            run(['docker', 'cp', str(migration), container + ':/database/migrations/002_keycloak_identity.sql'])
            # A failed new migration must leave neither its table nor a ledger row.
            broken = fixture / '012_broken.sql'
            broken.write_text('BEGIN;\nCREATE TABLE should_rollback(id int);\nSELECT 1/0;\nCOMMIT;\n')
            run(['docker', 'cp', str(broken), container + ':/database/migrations/012_broken.sql'])
            assert migrate(check=False).returncode != 0
            assert sql("SELECT to_regclass('should_rollback') IS NULL;").stdout.strip() == b't'
            assert sql('SELECT count(*) FROM deployment_migrations;').stdout == initial
            # Persist a sentinel record, then backup and restore into a new database.
            sql('CREATE TABLE phase12_sentinel(id int PRIMARY KEY); INSERT INTO phase12_sentinel VALUES (12);')
            backup = run(['docker', 'exec', container, 'pg_dump', '-U', 'tixflow', '-d', 'tixflow', '-Fc']).stdout
            assert len(backup) > 1024
            restore_started = time.monotonic()
            sql('CREATE DATABASE phase12_restored;')
            run(['docker', 'exec', '-i', container, 'pg_restore', '-U', 'tixflow', '-d', 'phase12_restored',
                 '--exit-on-error', '--no-owner'], input=backup)
            invariants = (ROOT / 'deploy/verify-invariants.sql').read_text()
            sql(invariants, 'phase12_restored')
            assert sql('SELECT id FROM phase12_sentinel;', 'phase12_restored').stdout.strip() == b'12'
            restore_seconds = time.monotonic() - restore_started
            run(['docker', 'restart', container])
            for _ in range(60):
                if sql('SELECT 1;', check=False).returncode == 0:
                    break
                time.sleep(1)
            assert sql('SELECT id FROM phase12_sentinel;').stdout.strip() == b'12'
            output = ROOT / 'artifacts/phase12'
            output.mkdir(parents=True, exist_ok=True)
            (output / 'deployment-integration.json').write_text(json.dumps({
                'completedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                'checks': ['clean migration', 'idempotent replay', 'checksum mismatch blocked',
                           'failed migration rolled back', 'backup/isolated restore', 'SQL invariants', 'restart persistence'],
                'migrationCount': int(initial), 'backupBytes': len(backup),
                'backupSha256': hashlib.sha256(backup).hexdigest(),
                'databaseRestoreSeconds': round(restore_seconds, 3),
                'elapsedSeconds': round(time.monotonic() - started, 3),
                'scope': 'Disposable PostgreSQL fixture only; no VPS/TLS/authenticated browser acceptance'
            }, indent=2) + '\n')
            print('Phase 12 disposable deployment integration: PASS')
        except subprocess.CalledProcessError as error:
            # Commands contain no real credentials. Avoid echoing argv/environment nevertheless.
            print((error.stderr or error.stdout or b'').decode(errors='replace'))
            raise RuntimeError('Deployment integration command failed') from None
        finally:
            # This container and its anonymous volume were created by this test exclusively.
            subprocess.run(['docker', 'rm', '-f', '-v', container], check=True, capture_output=True)


if __name__ == '__main__':
    main()

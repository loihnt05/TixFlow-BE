#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
lock_deploy
release="$ROOT/releases/$(date -u +%Y%m%dT%H%M%S)-$$"
mkdir -p "$release"
if [[ -f deploy/rendered/images.env ]]; then cp deploy/rendered/images.env "$release/previous-images.env"; fi
python3 deploy/scripts/prepare.py --env "$ENV_FILE" --lock-images
# Reload Compose so it uses the newly resolved immutable references.
source "$ROOT/deploy/scripts/common.sh"
dc config --quiet
cp deploy/rendered/images.env "$release/images.env"
cp deploy/rendered/nginx.conf "$release/nginx.conf"
sha256sum docker-compose.production.yml database/init/*.sql database/migrations/*.sql > "$release/source-sha256.txt"
trap 'echo "Deploy failed; see release directory. Traffic may be stopped. Follow the rollback runbook." >&2' ERR
# Planned maintenance: no concurrent booking writes while backing up or migrating.
dc stop nginx api worker frontend
dc up -d --wait --wait-timeout 180 postgres keycloak-postgres redis rabbitmq
TIXFLOW_LOCK_HELD=1 bash deploy/scripts/backup.sh > "$release/backup-path.txt"
dc run --rm --no-deps migrate > "$release/migration.log" 2>&1
dc exec -T postgres psql -X -U tixflow -d tixflow -v ON_ERROR_STOP=1 < deploy/verify-invariants.sql
if [[ ! -s deploy/rendered/letsencrypt/live/tixflow/fullchain.pem ]]; then
    dc run --rm --no-deps -p 80:80 certbot certonly --standalone --non-interactive --agree-tos \
        --email "$(value ACME_EMAIL)" --cert-name tixflow \
        -d "$(value APP_DOMAIN)" -d "$(value API_DOMAIN)" -d "$(value AUTH_DOMAIN)" -d "$(value MONITORING_DOMAIN)"
fi
dc up -d --wait --wait-timeout 420
dc exec -T nginx nginx -t
bash deploy/scripts/smoke.sh | tee "$release/smoke.log"
date -u +%FT%TZ > "$release/completed-at.txt"
printf '%s\n' "$release" > releases/current
echo "Deployment passed infrastructure smoke: $release"
echo 'Complete the authenticated acceptance checklist before promoting the demo.'

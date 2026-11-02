import importlib.util
import json
from pathlib import Path
import secrets
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts/prepare.py'
spec = importlib.util.spec_from_file_location('prepare', SCRIPT)
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)


def valid_values():
    result = prepare.read_env(prepare.ROOT / '.env.production.example')
    for key in ['APP_DOMAIN', 'API_DOMAIN', 'AUTH_DOMAIN', 'MONITORING_DOMAIN']:
        result[key] = key.split('_')[0].lower() + '.tixflow.test'
    for key in ['POSTGRES_PASSWORD', 'KEYCLOAK_DB_PASSWORD', 'RABBITMQ_PASSWORD',
                'KEYCLOAK_ADMIN_PASSWORD', 'GRAFANA_ADMIN_PASSWORD']:
        result[key] = secrets.token_hex(32)
    for key in prepare.IMAGE_KEYS[:3]:
        result[key] = 'ghcr.io/test/' + key.lower() + '@sha256:' + 'a' * 64
    return result


class ProductionConfigurationTests(unittest.TestCase):
    def test_template_cannot_be_accidentally_deployed(self):
        with self.assertRaises(ValueError):
            prepare.validate(prepare.read_env(prepare.ROOT / '.env.production.example'))

    def test_rejects_shared_password_mutable_application_image_and_nginx_injection(self):
        for key, value in [('APP_DOMAIN', 'test.com; include /tmp/evil;'),
                           ('API_IMAGE', 'ghcr.io/test/api:main'),
                           ('POSTGRES_PASSWORD', 'short'),
                           ('API_DOMAIN', 'https://api.tixflow.test')]:
            with self.subTest(key=key):
                values = valid_values()
                values[key] = value
                with self.assertRaises(ValueError):
                    prepare.validate(values)
        values = valid_values()
        values['POSTGRES_PASSWORD'] = values['KEYCLOAK_DB_PASSWORD']
        with self.assertRaises(ValueError):
            prepare.validate(values)

    def test_rejects_compose_shell_interpolation_and_duplicate_env_keys(self):
        for content in ['POSTGRES_PASSWORD=${OTHER}\n', 'A=one\nA=two\n', 'A="quoted"\n']:
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / 'test.env'
                path.write_text(content)
                with self.assertRaises(ValueError):
                    prepare.read_env(path)

    def test_render_preserves_customer_pkce_and_audience_without_dev_credentials(self):
        values = valid_values()
        prepare.validate(values)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            prepare.render(values, output)
            realm = json.loads((output / 'keycloak/tixflow-realm.json').read_text())
            self.assertNotIn('users', realm)
            self.assertEqual('all', realm['sslRequired'])
            web = next(c for c in realm['clients'] if c['clientId'] == 'tixflow-web')
            self.assertEqual(['https://app.tixflow.test/auth/callback'], web['redirectUris'])
            self.assertEqual('S256', web['attributes']['pkce.code.challenge.method'])
            self.assertFalse(web['directAccessGrantsEnabled'])
            self.assertTrue(any(m['config'].get('included.client.audience') == 'tixflow-api' for m in web['protocolMappers']))
            role = next(r for r in realm['roles']['realm'] if r['name'] == 'default-roles-tixflow')
            self.assertEqual(['Customer'], role['composites']['realm'])
            rendered = ''.join(p.read_text() for p in output.rglob('*') if p.is_file())
            self.assertNotIn('localhost', rendered)
            for key in ['POSTGRES_PASSWORD', 'KEYCLOAK_DB_PASSWORD', 'RABBITMQ_PASSWORD']:
                self.assertNotIn(values[key], rendered)


if __name__ == '__main__':
    unittest.main()

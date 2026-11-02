"""Validate local configuration and render public-only deployment files (Python 3 stdlib)."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
IMAGE_KEYS = ['API_IMAGE', 'WORKER_IMAGE', 'FRONTEND_IMAGE', 'POSTGRES_IMAGE', 'REDIS_IMAGE',
              'RABBITMQ_IMAGE', 'KEYCLOAK_IMAGE', 'NGINX_IMAGE', 'PROMETHEUS_IMAGE',
              'GRAFANA_IMAGE', 'NODE_EXPORTER_IMAGE', 'CERTBOT_IMAGE']


def read_env(path):
    result = {}
    for line in Path(path).read_text(encoding='utf-8-sig').splitlines():
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        key, separator, value = line.partition('=')
        if not separator or not re.fullmatch(r'[A-Z][A-Z0-9_]*', key) or key in result:
            raise ValueError('Invalid or duplicate environment key')
        # Restricted unquoted syntax keeps Python and Compose interpolation identical.
        if any(c in value for c in '\r\n\t\'"$#` ') or '\\' in value:
            raise ValueError(f'{key}: use an unquoted value without whitespace/interpolation')
        result[key] = value
    return result


def validate(values):
    domains = []
    for key in ['APP_DOMAIN', 'API_DOMAIN', 'AUTH_DOMAIN', 'MONITORING_DOMAIN']:
        domain = values.get(key, '')
        if not re.fullmatch(r'(?=.{1,253}$)[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?\.[a-z]{2,63}', domain):
            raise ValueError(f'{key}: expected a lowercase DNS name without scheme or path')
        if domain.endswith('.example.com') or '..' in domain:
            raise ValueError(f'{key}: replace example domain')
        domains.append(domain)
    if len(set(domains)) != 4:
        raise ValueError('Use four distinct domain names')
    secrets = []
    for key in ['POSTGRES_PASSWORD', 'KEYCLOAK_DB_PASSWORD', 'RABBITMQ_PASSWORD',
                'KEYCLOAK_ADMIN_PASSWORD', 'GRAFANA_ADMIN_PASSWORD']:
        value = values.get(key, '')
        if not re.fullmatch(r'[A-Za-z0-9_-]{32,128}', value):
            raise ValueError(f'{key}: generate an independent 32-128 character secret')
        secrets.append(value)
    if len(set(secrets)) != len(secrets):
        raise ValueError('Service passwords must be different')
    for key in ['KEYCLOAK_ADMIN_USERNAME', 'GRAFANA_ADMIN_USERNAME']:
        if not re.fullmatch(r'[A-Za-z0-9_-]{3,64}', values.get(key, '')):
            raise ValueError(f'{key}: expected a simple username')
    if not re.fullmatch(r'[^@\s]+@[^@\s]+\.[a-zA-Z]{2,}', values.get('ACME_EMAIL', '')):
        raise ValueError('ACME_EMAIL is required')
    for key in IMAGE_KEYS:
        image = values.get(key, '')
        if not re.fullmatch(r'[a-zA-Z0-9./_:@-]+', image) or 'REPLACE' in image or image.endswith(':latest'):
            raise ValueError(f'{key}: configure a versioned image')
        if key in IMAGE_KEYS[:3] and not re.search(r'@sha256:[a-f0-9]{64}$', image):
            raise ValueError(f'{key}: use the tested CI image digest')


def render(values, output):
    output.mkdir(parents=True, exist_ok=True)
    for directory in ['keycloak', 'acme', 'letsencrypt']:
        (output / directory).mkdir(exist_ok=True)
    realm = json.loads((ROOT / 'infrastructure/keycloak/tixflow-realm.json').read_text(encoding='utf-8-sig'))
    realm.pop('users', None)
    realm['sslRequired'] = 'all'
    realm['bruteForceProtected'] = True
    # Custom theme is already bundled; never import development accounts or Swagger clients.
    realm['clients'] = [c for c in realm['clients'] if c['clientId'] in ['tixflow-api', 'tixflow-web']]
    web = next(c for c in realm['clients'] if c['clientId'] == 'tixflow-web')
    origin = 'https://' + values['APP_DOMAIN']
    web['redirectUris'] = [origin + '/auth/callback']
    web['webOrigins'] = [origin]
    web['attributes']['post.logout.redirect.uris'] = origin + '/'
    (output / 'keycloak/tixflow-realm.json').write_text(json.dumps(realm, indent=2) + '\n', encoding='utf-8')
    template = (ROOT / 'deploy/nginx.conf.template').read_text(encoding='utf-8')
    for key in ['APP_DOMAIN', 'API_DOMAIN', 'AUTH_DOMAIN', 'MONITORING_DOMAIN']:
        template = template.replace('@@' + key + '@@', values[key])
    (output / 'nginx.conf').write_text(template, encoding='utf-8')


def lock_images(values, output):
    locked = []
    for key in IMAGE_KEYS:
        ref = values[key]
        subprocess.run(['docker', 'pull', ref], check=True)
        digests = json.loads(subprocess.check_output(['docker', 'image', 'inspect', ref,
                                                      '--format', '{{json .RepoDigests}}'], text=True))
        if not digests:
            raise ValueError(f'{key}: image has no registry digest')
        digest = ref if '@sha256:' in ref else digests[0]
        locked.append(f'{key}={digest}')
    target = output / 'images.env'
    temporary = target.with_suffix('.tmp')
    temporary.write_text('\n'.join(locked) + '\n', encoding='utf-8')
    os.replace(temporary, target)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--env', type=Path, default=ROOT / '.env.production')
    parser.add_argument('--output', type=Path, default=ROOT / 'deploy/rendered')
    parser.add_argument('--lock-images', action='store_true')
    parser.add_argument('--get', choices=['APP_DOMAIN', 'API_DOMAIN', 'AUTH_DOMAIN', 'MONITORING_DOMAIN', 'ACME_EMAIL'])
    args = parser.parse_args()
    values = read_env(args.env)
    validate(values)
    if args.get:
        print(values[args.get])
        return
    if os.name != 'nt' and args.env.stat().st_mode & 0o077:
        raise ValueError('chmod 600 the environment file before deploying')
    render(values, args.output)
    if args.lock_images:
        lock_images(values, args.output)
    print('Production configuration validated; rendered public configuration only.')


if __name__ == '__main__':
    main()

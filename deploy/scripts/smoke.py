"""Public smoke, normal TLS validation; never uses an insecure certificate bypass."""
import argparse
import json
import urllib.error
import urllib.request
from prepare import read_env, validate


def request(url, expected=200, headers=None):
    try:
        response = urllib.request.urlopen(urllib.request.Request(url, headers=headers or {}), timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        if response.status != expected:
            raise RuntimeError(f'{url}: expected {expected}, got {response.status}')
        body = response.read()
        if url.startswith('https://') and not response.headers.get('Strict-Transport-Security'):
            raise RuntimeError('Missing HSTS')
        return body, response.headers


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--env', required=True)
    values = read_env(parser.parse_args().env)
    validate(values)
    api = 'https://' + values['API_DOMAIN']
    app = 'https://' + values['APP_DOMAIN']
    auth = 'https://' + values['AUTH_DOMAIN']
    request(app)
    request(api + '/health')
    request(api + '/api/v1/events')
    request(api + '/api/v1/users/me', 401)
    request(api + '/api/v1/operations/metrics', 401)
    request(api + '/internal/metrics', 404)
    request(api + '/swagger/index.html', 404)
    request(auth + '/admin/', 404)
    discovery, _ = request(auth + '/realms/tixflow/.well-known/openid-configuration')
    if json.loads(discovery)['issuer'] != auth + '/realms/tixflow':
        raise RuntimeError('Public OIDC issuer mismatch')
    _, headers = request(api + '/api/v1/events', headers={'Origin': app})
    if headers.get('Access-Control-Allow-Origin') != app:
        raise RuntimeError('Configured frontend origin is not allowed')
    _, headers = request(api + '/api/v1/events', headers={'Origin': 'https://untrusted.invalid'})
    if headers.get('Access-Control-Allow-Origin'):
        raise RuntimeError('Unexpected CORS origin allowed')
    request('https://' + values['MONITORING_DOMAIN'] + '/login')
    print('HTTPS, catalog, auth boundaries, CORS and monitoring login passed.')


if __name__ == '__main__':
    main()

export function createClient(baseUrl, token, evidence, fetcher = fetch) {
  const url = new URL(baseUrl);
  if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname))) ||
      url.username || url.password || url.search || url.hash || url.pathname !== '/') throw new Error('Invalid API origin');
  if (!token) throw new Error('Missing test access token');
  return async function request(path, { method = 'GET', body, key, version, expected = [200] } = {}) {
    if (!path.startsWith('/api/v1/') || path.includes('://')) throw new Error('Invalid API path');
    const headers = { Authorization: `Bearer ${token}` };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (key) headers['Idempotency-Key'] = key;
    if (version !== undefined) headers['X-Expected-Version'] = String(version);
    let response;
    try {
      response = await fetcher(url.origin + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body),
        redirect: 'error', signal: AbortSignal.timeout(15000) });
    } catch {
      evidence.push({ method, path, error: 'NETWORK_OR_TIMEOUT' });
      throw new Error(`Request failed: ${method} ${path}`);
    }
    evidence.push({ method, path, status: response.status, passed: expected.includes(response.status) });
    if (!expected.includes(response.status)) throw new Error(`Unexpected HTTP ${response.status}: ${method} ${path}`);
    return response.status === 204 ? null : response.json();
  };
}

export function requireValue(condition, message) {
  if (!condition) throw new Error(message);
}

export async function poll(read, accepted, { attempts = 30, intervalMs = 1000 } = {}) {
  for (let attempt = 0; attempt < attempts; attempt++) {
    const result = await read();
    if (accepted(result)) return result;
    if (attempt + 1 < attempts) await new Promise(resolve => setTimeout(resolve, intervalMs));
  }
  throw new Error('Timed out waiting for asynchronous processing');
}

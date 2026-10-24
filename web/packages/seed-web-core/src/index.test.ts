import { afterEach, describe, expect, it, vi } from 'vitest';
import { createHttpClient, HttpError, loadRuntimeConfig } from './index.js';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('loadRuntimeConfig', () => {
  it('liest die Konfiguration und kürzt den Schrägstrich am Ende', async () => {
    const fetchFn = (async () => Response.json({ apiBaseUrl: 'https://api.example.org/', extra: 1 })) as typeof fetch;

    await expect(loadRuntimeConfig({ fetch: fetchFn })).resolves.toEqual({
      apiBaseUrl: 'https://api.example.org',
      extra: 1,
    });
  });

  it('bricht ohne apiBaseUrl ab', async () => {
    const fetchFn = (async () => Response.json({})) as typeof fetch;

    await expect(loadRuntimeConfig({ fetch: fetchFn })).rejects.toThrow('apiBaseUrl');
  });
});

describe('createHttpClient', () => {
  it('sendet JSON mit Bearer-Token', async () => {
    let request: Request | undefined;
    const fetchFn = (async (input: RequestInfo | URL, init?: RequestInit) => {
      request = new Request(input, init);
      return Response.json({ id: 1 });
    }) as typeof fetch;

    const client = createHttpClient({
      baseUrl: 'https://api.example.org/',
      getAccessToken: async () => 'token-123',
      fetch: fetchFn,
    });

    await expect(client.post('/api/items', { name: 'a' })).resolves.toEqual({ id: 1 });
    expect(request?.url).toBe('https://api.example.org/api/items');
    expect(request?.headers.get('Authorization')).toBe('Bearer token-123');
    expect(await request?.json()).toEqual({ name: 'a' });
  });

  it('wirft HttpError mit Status und Body', async () => {
    const fetchFn = (async () => new Response('nope', { status: 403 })) as typeof fetch;
    const client = createHttpClient({ baseUrl: 'https://api.example.org', fetch: fetchFn });

    const error = await client.get('/api/secret').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(HttpError);
    expect(error).toMatchObject({ status: 403, body: 'nope' });
  });
  it('lässt get, post, put und delete unverändert: 204 ohne Daten, 304 als Fehler', async () => {
    const fetchFn = (async (input: RequestInfo | URL) =>
      String(input).endsWith('/leer') ? new Response(null, { status: 204 }) : new Response(null, { status: 304 })) as typeof fetch;
    const client = createHttpClient({ baseUrl: 'https://api.example.org', fetch: fetchFn });

    await expect(client.delete('/api/leer')).resolves.toBeUndefined();
    await expect(client.get('/api/items')).rejects.toMatchObject({ name: 'HttpError', status: 304 });
  });
});

describe('createHttpClient.request', () => {
  it('sendet eigene Header samt Token und liefert Status, Daten und Header', async () => {
    let request: Request | undefined;
    const fetchFn = (async (input: RequestInfo | URL, init?: RequestInit) => {
      request = new Request(input, init);
      return Response.json({ items: [] }, { headers: { ETag: '"v2"' } });
    }) as typeof fetch;
    const client = createHttpClient({ baseUrl: 'https://api.example.org', getAccessToken: async () => 'token-123', fetch: fetchFn });

    const response = await client.request<{ items: unknown[] }>('GET', '/api/items', {
      headers: { 'If-None-Match': '"v1"', Authorization: 'Bearer fremd' },
    });

    expect(response).toMatchObject({ status: 200, data: { items: [] } });
    expect(response.headers.get('ETag')).toBe('"v2"');
    expect(request?.headers.get('If-None-Match')).toBe('"v1"');
    expect(request?.headers.get('Accept')).toBe('application/json');
    expect(request?.headers.get('Authorization')).toBe('Bearer token-123');
  });

  it('nimmt 304 ohne Fehler und ohne Daten an', async () => {
    const fetchFn = (async () => new Response(null, { status: 304, headers: { ETag: '"v1"' } })) as typeof fetch;
    const client = createHttpClient({ baseUrl: 'https://api.example.org', fetch: fetchFn });

    const response = await client.request('GET', '/api/items', { headers: { 'If-None-Match': '"v1"' } });

    expect(response.status).toBe(304);
    expect(response.data).toBeUndefined();
  });

  it('sendet einen Body als JSON und wirft außerhalb von 2xx und 304 HttpError', async () => {
    let request: Request | undefined;
    const fetchFn = (async (input: RequestInfo | URL, init?: RequestInit) => {
      request = new Request(input, init);
      return new Response('veraltet', { status: 412 });
    }) as typeof fetch;
    const client = createHttpClient({ baseUrl: 'https://api.example.org', fetch: fetchFn });

    const error = await client.request('PUT', '/api/items/1', { body: { title: 'a' }, headers: { 'If-Match': '"v1"' } }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(HttpError);
    expect(error).toMatchObject({ status: 412, body: 'veraltet' });
    expect(request?.headers.get('Content-Type')).toBe('application/json');
    expect(await request?.json()).toEqual({ title: 'a' });
  });
});

describe('createHttpClient: Pfade und Origin', () => {
  /** Client mit Token, der Adresse und Authorization jeder gesendeten Anfrage festhält. */
  function setup(baseUrl: string) {
    const sent: { url: string; authorization: string | null }[] = [];
    const getAccessToken = vi.fn(async () => 'token-123');
    const fetchFn = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const request = new Request(input, init);
      sent.push({ url: request.url, authorization: request.headers.get('Authorization') });
      return Response.json({});
    }) as typeof fetch;
    return { client: createHttpClient({ baseUrl, getAccessToken, fetch: fetchFn }), sent, getAccessToken };
  }

  it.each([
    ['https://api.example.org', '/api/items?filter=a%20b&top=5', 'https://api.example.org/api/items?filter=a%20b&top=5'],
    ['https://api.example.org/', '/', 'https://api.example.org/'],
    ['http://localhost:7071', '/api/health', 'http://localhost:7071/api/health'],
    ['https://example.org/backend/', '/api/items', 'https://example.org/backend/api/items'],
    ['https://api.example.org', '/api/@evil.example/x', 'https://api.example.org/api/@evil.example/x'],
  ])('setzt %s und %s zu %s zusammen, mit Token', async (baseUrl, path, url) => {
    const { client, sent } = setup(baseUrl);

    await client.get(path);
    await client.request('GET', path);

    expect(sent).toEqual([
      { url, authorization: 'Bearer token-123' },
      { url, authorization: 'Bearer token-123' },
    ]);
  });

  it.each(['.evil.example/x', '@evil.example/x', ':8443/x', '//evil.example/x', '/\\evil.example/x', 'https://evil.example/x', 'api/items', ''])(
    'lehnt den Pfad %j ab, ohne Token und ohne Anfrage',
    async (path) => {
      const { client, sent, getAccessToken } = setup('https://api.example.org');

      await expect(client.get(path)).rejects.toThrow('muss mit genau einem / beginnen');
      await expect(client.post(path, { a: 1 })).rejects.toThrow('muss mit genau einem / beginnen');
      await expect(client.request('PUT', path, { body: {} })).rejects.toThrow('muss mit genau einem / beginnen');

      expect(sent).toEqual([]);
      expect(getAccessToken).not.toHaveBeenCalled();
    },
  );

  it('nimmt bei relativer baseUrl die Origin der Seite und sendet an keine andere', async () => {
    vi.stubGlobal('location', new URL('https://app.example.org/start'));
    const { client, sent, getAccessToken } = setup('/');

    await client.get('/api/me');
    // Der URL-Parser entfernt den Tab; übrig bliebe //evil.example/x, also ein anderer Host.
    await expect(client.get('/\t/evil.example/x')).rejects.toThrow('nicht zur API (https://app.example.org)');

    expect(sent).toEqual([{ url: 'https://app.example.org/api/me', authorization: 'Bearer token-123' }]);
    expect(getAccessToken).toHaveBeenCalledTimes(1);
  });
});

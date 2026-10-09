import { describe, expect, it } from 'vitest';
import { createHttpClient, HttpError, loadRuntimeConfig } from './index.js';

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
});

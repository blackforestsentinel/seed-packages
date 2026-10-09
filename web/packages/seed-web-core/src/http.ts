/** Fehler einer API-Antwort außerhalb von 2xx. */
export class HttpError extends Error {
  readonly status: number;
  readonly body: string;

  constructor(method: string, path: string, status: number, body: string) {
    super(`${method} ${path} fehlgeschlagen (HTTP ${status})`);
    this.name = 'HttpError';
    this.status = status;
    this.body = body;
  }
}

export interface HttpClientOptions {
  baseUrl: string;
  /** Liefert ein Bearer-Token, z. B. aus @blackforestsentinel/seed-web-auth. */
  getAccessToken?: () => Promise<string | undefined>;
  fetch?: typeof fetch;
}

export interface HttpClient {
  get<T>(path: string): Promise<T>;
  post<T>(path: string, body?: unknown): Promise<T>;
  put<T>(path: string, body?: unknown): Promise<T>;
  delete<T = void>(path: string): Promise<T>;
}

/** JSON-Client für die Function-API des Projekts. */
export function createHttpClient(options: HttpClientOptions): HttpClient {
  const { baseUrl, getAccessToken, fetch: fetchFn = fetch } = options;
  const base = baseUrl.replace(/\/+$/, '');

  async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
    const headers = new Headers({ Accept: 'application/json' });
    if (body !== undefined) {
      headers.set('Content-Type', 'application/json');
    }

    const token = await getAccessToken?.();
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }

    const response = await fetchFn(`${base}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (!response.ok) {
      throw new HttpError(method, path, response.status, await response.text());
    }

    if (response.status === 204 || response.headers.get('Content-Length') === '0') {
      return undefined as T;
    }

    return (await response.json()) as T;
  }

  return {
    get: (path) => send('GET', path),
    post: (path, body) => send('POST', path, body),
    put: (path, body) => send('PUT', path, body),
    delete: (path) => send('DELETE', path),
  };
}

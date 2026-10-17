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

export interface RequestOptions {
  /** Wird als JSON gesendet. */
  body?: unknown;
  /** Eigene Header, etwa If-None-Match; Authorization setzt der Client aus getAccessToken. */
  headers?: HeadersInit;
}

/** Antwort von request(): Status, Daten und Header. */
export interface HttpResponse<T> {
  status: number;
  /** undefined bei 204, 304 und leerem Body. */
  data: T | undefined;
  headers: Headers;
}

export interface HttpClient {
  get<T>(path: string): Promise<T>;
  post<T>(path: string, body?: unknown): Promise<T>;
  put<T>(path: string, body?: unknown): Promise<T>;
  delete<T = void>(path: string): Promise<T>;
  /**
   * Anfrage mit eigenen Headern, die Status und Antwort-Header zurückgibt, etwa für bedingte
   * Anfragen mit If-None-Match. 304 gilt hier nicht als Fehler, alles andere außerhalb von 2xx wirft
   * HttpError wie bei den anderen Methoden. Fremde Origins geben Header wie ETag nur heraus, wenn die
   * API sie per Access-Control-Expose-Headers freigibt.
   */
  request<T>(method: string, path: string, options?: RequestOptions): Promise<HttpResponse<T>>;
}

/** JSON-Client für die Function-API des Projekts. */
export function createHttpClient(options: HttpClientOptions): HttpClient {
  const { baseUrl, getAccessToken, fetch: fetchFn = fetch } = options;
  const base = baseUrl.replace(/\/+$/, '');

  async function call(method: string, path: string, { body, headers: extra }: RequestOptions = {}): Promise<Response> {
    const headers = new Headers({ Accept: 'application/json' });
    if (body !== undefined) {
      headers.set('Content-Type', 'application/json');
    }
    new Headers(extra).forEach((value, name) => headers.set(name, value));

    const token = await getAccessToken?.();
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }

    return fetchFn(`${base}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  }

  async function read<T>(response: Response): Promise<T | undefined> {
    if (response.status === 204 || response.status === 304 || response.headers.get('Content-Length') === '0') {
      return undefined;
    }
    return (await response.json()) as T;
  }

  async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
    const response = await call(method, path, { body });
    if (!response.ok) {
      throw new HttpError(method, path, response.status, await response.text());
    }
    return (await read<T>(response)) as T;
  }

  return {
    get: (path) => send('GET', path),
    post: (path, body) => send('POST', path, body),
    put: (path, body) => send('PUT', path, body),
    delete: (path) => send('DELETE', path),
    async request<T>(method: string, path: string, requestOptions?: RequestOptions): Promise<HttpResponse<T>> {
      const response = await call(method, path, requestOptions);
      if (!response.ok && response.status !== 304) {
        throw new HttpError(method, path, response.status, await response.text());
      }
      return { status: response.status, data: await read<T>(response), headers: response.headers };
    },
  };
}

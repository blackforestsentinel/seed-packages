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
  /**
   * Adresse der API, etwa apiBaseUrl aus config.json; ein Pfad darin (https://example.org/backend)
   * steht vor jedem Pfad der Anfragen. Relativ nur im Browser, dann gilt die Origin der Seite.
   */
  baseUrl: string;
  /** Liefert ein Bearer-Token, z. B. aus @blackforestsentinel/seed-web-auth. Es geht nur an die Origin von baseUrl. */
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

/**
 * JSON-Client für die Function-API des Projekts. Pfade beginnen mit genau einem /, etwa /api/items;
 * andere lehnt er ab, bevor ein Token geholt oder eine Anfrage gesendet wird.
 */
export function createHttpClient(options: HttpClientOptions): HttpClient {
  const { baseUrl, getAccessToken, fetch: fetchFn = fetch } = options;
  const base = baseUrl.replace(/\/+$/, '');

  /** Volle Adresse für einen Pfad der API; wirft, wenn sie nicht bei der API landet. */
  function resolve(path: string): string {
    // Ohne / am Anfang verlängert der Pfad den Host (".evil.example/x", "@evil.example/x",
    // ":8443/x"), mit // oder /\ nennt er einen anderen Host.
    if (!/^\/(?![/\\])/.test(path)) {
      throw new Error(`Pfad „${path}“ muss mit genau einem / beginnen, etwa /api/items.`);
    }
    const page = globalThis.location?.href;
    const url = new URL(`${base}${path}`, page);
    // Zweite Sicherung, etwa für eine relative baseUrl: Der URL-Parser entfernt Tabs und
    // Zeilenumbrüche, dann wird aus "/\t/evil.example" doch "//evil.example". Verglichen werden
    // Schema und Host, weil origin bei Schemas wie chrome-extension: nur "null" ist.
    const api = new URL(base || '/', page);
    if (url.protocol !== api.protocol || url.host !== api.host) {
      throw new Error(`Pfad „${path}“ führt zu ${url.protocol}//${url.host}, nicht zur API (${api.protocol}//${api.host}).`);
    }
    return url.href;
  }

  async function call(method: string, path: string, { body, headers: extra }: RequestOptions = {}): Promise<Response> {
    const url = resolve(path);
    const headers = new Headers({ Accept: 'application/json' });
    if (body !== undefined) {
      headers.set('Content-Type', 'application/json');
    }
    new Headers(extra).forEach((value, name) => headers.set(name, value));

    const token = await getAccessToken?.();
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }

    return fetchFn(url, {
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

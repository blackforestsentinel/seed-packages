/** Antwort von Graph; bei Bildern ist body der Inhalt als Base64. */
export interface GraphResponse {
  status: number;
  /** Header mit kleingeschriebenen Namen. */
  headers: Record<string, string>;
  body: unknown;
}

/** Fehler einer Graph-Anfrage, die nicht als Antwort weitergegeben wird (Batch, Netz, Drosselung). */
export class GraphError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'GraphError';
    this.status = status;
  }
}

export interface GraphClientOptions {
  /** Access-Token für Graph, z. B. auth.getAccessToken(directory.scopes). */
  getToken: () => Promise<string>;
  fetch?: typeof fetch;
  baseUrl?: string;
  /** Wartezeit, in der GET-Anfragen für einen $batch gesammelt werden, Default 10 ms. */
  batchDelay?: number;
  /** Für Tests: Warten bei Drosselung. */
  sleep?: (ms: number) => Promise<void>;
}

export interface GraphRequestOptions {
  headers?: Record<string, string>;
  /** Antwort ist ein Bild; body kommt als Base64. */
  binary?: boolean;
}

export interface GraphClient {
  /** GET, gesammelt in $batch und gleiche Anfragen zusammengefasst. */
  get(path: string, options?: GraphRequestOptions): Promise<GraphResponse>;
  /** Direkter Aufruf ohne $batch, etwa für POST. */
  send(method: string, path: string, body?: unknown, options?: GraphRequestOptions): Promise<GraphResponse>;
}

interface Queued {
  path: string;
  options: GraphRequestOptions;
  attempt: number;
  resolve(response: GraphResponse): void;
  reject(error: unknown): void;
}

const BATCH_LIMIT = 20;
const MAX_ATTEMPTS = 4;
const MAX_RETRY_DELAY = 60_000;

/**
 * Schlanker Client für Microsoft Graph: GET-Anfragen innerhalb weniger Millisekunden gehen
 * gemeinsam als $batch (bis 20 je Aufruf), gleiche laufende Anfragen teilen sich eine Antwort, und
 * gedrosselte Anfragen (429) wiederholt er nach Retry-After.
 */
export function createGraphClient(options: GraphClientOptions): GraphClient {
  const { getToken, baseUrl = 'https://graph.microsoft.com/v1.0', batchDelay = 10 } = options;
  const fetchFn: typeof fetch = options.fetch ?? ((input, init) => fetch(input, init));
  const sleep = options.sleep ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)));
  const base = baseUrl.replace(/\/+$/, '');

  const inflight = new Map<string, Promise<GraphResponse>>();
  let queue: Queued[] = [];
  let timer: ReturnType<typeof setTimeout> | null = null;

  function enqueue(item: Queued) {
    queue.push(item);
    timer ??= setTimeout(flush, batchDelay);
  }

  function flush() {
    timer = null;
    const items = queue;
    queue = [];
    for (let i = 0; i < items.length; i += BATCH_LIMIT) {
      const chunk = items.slice(i, i + BATCH_LIMIT);
      void (chunk.length === 1 ? single(chunk[0]!) : batch(chunk));
    }
  }

  async function retryLater(items: Queued[], delay: number, error: GraphError) {
    const retry = items.filter((item) => item.attempt + 1 < MAX_ATTEMPTS);
    items.filter((item) => !retry.includes(item)).forEach((item) => item.reject(error));
    if (retry.length === 0) {
      return;
    }
    await sleep(delay);
    retry.forEach((item) => enqueue({ ...item, attempt: item.attempt + 1 }));
  }

  async function single(item: Queued) {
    try {
      item.resolve(await send('GET', item.path, undefined, item.options));
    } catch (error) {
      item.reject(error);
    }
  }

  async function batch(items: Queued[]) {
    let response: Response;
    try {
      const token = await getToken();
      response = await fetchFn(`${base}/$batch`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          requests: items.map((item, index) => ({
            id: String(index),
            method: 'GET',
            url: item.path,
            ...(item.options.headers ? { headers: item.options.headers } : {}),
          })),
        }),
      });
    } catch (error) {
      items.forEach((item) => item.reject(error));
      return;
    }

    if (response.status === 429) {
      const attempt = Math.min(...items.map((item) => item.attempt));
      await retryLater(items, retryDelay(response.headers.get('Retry-After'), attempt), new GraphError(429, 'Graph drosselt die Anfragen.'));
      return;
    }
    if (!response.ok) {
      const error = new GraphError(response.status, `Graph $batch fehlgeschlagen (HTTP ${response.status})`);
      items.forEach((item) => item.reject(error));
      return;
    }

    let result: { responses?: { id: string; status: number; headers?: Record<string, string>; body?: unknown }[] };
    try {
      result = (await response.json()) as typeof result;
    } catch (error) {
      items.forEach((item) => item.reject(error));
      return;
    }

    const answered = new Set<Queued>();
    const throttled: Queued[] = [];
    let delay = 0;
    for (const entry of result.responses ?? []) {
      const item = items[Number(entry.id)];
      if (!item || answered.has(item)) {
        continue;
      }
      answered.add(item);
      const headers = lowerCase(entry.headers);
      if (entry.status === 429) {
        throttled.push(item);
        delay = Math.max(delay, retryDelay(headers['retry-after'] ?? null, item.attempt));
        continue;
      }
      item.resolve({ status: entry.status, headers, body: entry.body ?? null });
    }
    items
      .filter((item) => !answered.has(item))
      .forEach((item) => item.reject(new GraphError(0, `Graph $batch ohne Antwort für ${item.path}`)));
    if (throttled.length > 0) {
      await retryLater(throttled, delay, new GraphError(429, 'Graph drosselt die Anfragen.'));
    }
  }

  async function send(method: string, path: string, body?: unknown, requestOptions: GraphRequestOptions = {}, attempt = 0): Promise<GraphResponse> {
    const token = await getToken();
    const headers: Record<string, string> = { Authorization: `Bearer ${token}`, ...requestOptions.headers };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }
    const response = await fetchFn(`${base}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (response.status === 429) {
      if (attempt + 1 >= MAX_ATTEMPTS) {
        throw new GraphError(429, 'Graph drosselt die Anfragen.');
      }
      await sleep(retryDelay(response.headers.get('Retry-After'), attempt));
      return send(method, path, body, requestOptions, attempt + 1);
    }

    const result: GraphResponse = { status: response.status, headers: lowerCase(Object.fromEntries(response.headers)), body: null };
    if (response.ok && requestOptions.binary) {
      result.body = toBase64(await response.arrayBuffer());
    } else if (response.status !== 204) {
      const text = await response.text();
      try {
        result.body = text ? JSON.parse(text) : null;
      } catch {
        result.body = text;
      }
    }
    return result;
  }

  return {
    get(path, requestOptions = {}) {
      const key = `${requestOptions.binary ? 'bin' : 'json'} ${path} ${JSON.stringify(requestOptions.headers ?? {})}`;
      let pending = inflight.get(key);
      if (!pending) {
        pending = new Promise<GraphResponse>((resolve, reject) => {
          enqueue({ path, options: requestOptions, attempt: 0, resolve, reject });
        }).finally(() => inflight.delete(key));
        inflight.set(key, pending);
      }
      return pending;
    },
    send: (method, path, body, requestOptions) => send(method, path, body, requestOptions),
  };
}

/** Retry-After in Sekunden, sonst exponentiell ab einer Sekunde; höchstens eine Minute. */
function retryDelay(header: string | null, attempt: number): number {
  const seconds = header === null ? Number.NaN : Number(header);
  const delay = Number.isFinite(seconds) && seconds >= 0 ? seconds * 1000 : 1000 * 2 ** attempt;
  return Math.min(delay, MAX_RETRY_DELAY);
}

function lowerCase(headers: Record<string, string> | undefined): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [name, value] of Object.entries(headers ?? {})) {
    result[name.toLowerCase()] = value;
  }
  return result;
}

function toBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(binary);
}

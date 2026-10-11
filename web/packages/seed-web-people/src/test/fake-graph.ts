import type { DirectoryAuth } from '../provider.js';

/** Erfundene Personen; keine echten Daten. */
export const people = {
  erika: {
    id: '11111111-0000-4000-8000-000000000001',
    displayName: 'Erika Mustermann',
    mail: 'erika.mustermann@example.org',
    userPrincipalName: 'erika.mustermann@example.org',
    jobTitle: 'Buchhalterin',
    department: 'Buchhaltung',
  },
  max: {
    id: '11111111-0000-4000-8000-000000000002',
    displayName: 'Max Mustermann',
    mail: 'max.mustermann@example.org',
    userPrincipalName: 'max.mustermann@example.org',
    jobTitle: 'Entwickler',
    department: 'IT',
  },
  eric: {
    id: '11111111-0000-4000-8000-000000000003',
    displayName: 'Eric Beispiel',
    mail: 'eric.beispiel@example.org',
    userPrincipalName: 'eric.beispiel@example.org',
    jobTitle: null,
    department: 'Vertrieb',
  },
  erna: {
    id: '11111111-0000-4000-8000-000000000004',
    displayName: 'Erna Platzhalter',
    mail: 'erna.platzhalter@example.org',
    userPrincipalName: 'erna.platzhalter@example.org',
    jobTitle: 'Leiterin',
    department: 'Einkauf',
  },
};

export type FakeUser = (typeof people)[keyof typeof people];

/** Kleines JPEG-Ersatzbild (nur Bytes, Inhalt egal). */
export const photoBytes: Uint8Array<ArrayBuffer> = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4, 0xff, 0xd9]);
export const photoDataUrl = `data:image/jpeg;base64,${btoa(String.fromCharCode(...photoBytes))}`;

export interface FakeResponse {
  status: number;
  body?: unknown;
  headers?: Record<string, string>;
  /** Bildinhalt statt JSON. */
  bytes?: Uint8Array<ArrayBuffer>;
}

export interface GraphCall {
  method: string;
  path: string;
  body?: unknown;
  /** Header mit kleingeschriebenen Namen, ohne Authorization im $batch. */
  headers: Record<string, string>;
  /** Teil eines $batch. */
  batched: boolean;
}

export type Handler = (call: GraphCall) => FakeResponse | undefined;

export interface FakeGraph {
  fetch: typeof fetch;
  /** Alle Anfragen, auch die einzelnen in einem $batch. */
  calls: GraphCall[];
  /** Nur die HTTP-Aufrufe (ein $batch zählt einmal). */
  requests: { method: string; path: string; body?: unknown }[];
  /** Zusätzliche Antworten, die vor den Standardantworten greifen. */
  use(handler: Handler): void;
}

export interface FakeGraphOptions {
  users?: FakeUser[];
  /** IDs mit Foto. */
  photos?: string[];
  /** /me/people: Treffer in dieser Reihenfolge. */
  relevant?: FakeUser[];
  /** Teams-Status je ID. */
  presence?: Record<string, { availability: string; activity: string }>;
  /** App-Rollen der API und ihre Zuweisungen. */
  appRoles?: { id: string; value: string }[];
  assignments?: { principalId: string; principalType: 'User' | 'Group'; principalDisplayName: string; appRoleId: string }[];
  groups?: Record<string, FakeUser[]>;
}

const BASE = 'https://graph.microsoft.com/v1.0';

/** Gefälschtes Microsoft Graph mit $batch, Fotos, Suche, Status und Rollenzuweisungen. */
export function createFakeGraph(options: FakeGraphOptions = {}): FakeGraph {
  const users = options.users ?? Object.values(people);
  const photos = new Set(options.photos ?? []);
  const calls: GraphCall[] = [];
  const requests: FakeGraph['requests'] = [];
  const handlers: Handler[] = [];

  function answer(call: GraphCall): FakeResponse {
    calls.push(call);
    for (const handler of handlers) {
      const response = handler(call);
      if (response) {
        return response;
      }
    }
    const url = new URL(BASE + call.path);
    const path = decodeURIComponent(url.pathname.replace('/v1.0', ''));
    const search = url.searchParams.get('$search') ?? '';
    const term = search.replace(/"(displayName|mail):/g, '"').match(/"([^"]*)"/)?.[1]?.toLowerCase() ?? '';
    const matches = (user: FakeUser) =>
      user.displayName.toLowerCase().split(' ').some((word) => word.startsWith(term)) || user.mail.startsWith(term);

    let match: RegExpMatchArray | null;
    if ((match = path.match(/^\/users\/([^/]+)\/photos\/(\d+)x\d+\/\$value$/))) {
      return photos.has(match[1]!)
        ? { status: 200, bytes: photoBytes, headers: { 'Content-Type': 'image/jpeg' } }
        : { status: 404, body: { error: { code: 'ImageNotFound', message: 'Kein Foto' } } };
    }
    if ((match = path.match(/^\/users\/([^/]+)$/))) {
      const user = users.find((entry) => entry.id === match![1]);
      return user ? { status: 200, body: user } : { status: 404, body: { error: { code: 'Request_ResourceNotFound' } } };
    }
    if (path === '/users') {
      return { status: 200, body: { value: users.filter(matches) } };
    }
    if (path === '/me/people') {
      const relevant = (options.relevant ?? []).filter(matches);
      return {
        status: 200,
        body: {
          value: relevant.map((user) => ({
            id: user.id,
            displayName: user.displayName,
            jobTitle: user.jobTitle,
            department: user.department,
            userPrincipalName: user.userPrincipalName,
            scoredEmailAddresses: [{ address: user.mail }],
            personType: { class: 'Person', subclass: 'OrganizationUser' },
          })),
        },
      };
    }
    if (path === '/communications/getPresencesByUserId') {
      const ids = (call.body as { ids: string[] }).ids;
      return {
        status: 200,
        body: { value: ids.filter((id) => options.presence?.[id]).map((id) => ({ id, ...options.presence![id] })) },
      };
    }
    if ((match = path.match(/^\/servicePrincipals\(appId='([^']+)'\)$/))) {
      return options.appRoles ? { status: 200, body: { id: 'sp-1', appRoles: options.appRoles } } : { status: 404, body: {} };
    }
    if (path.match(/^\/servicePrincipals\(appId='[^']+'\)\/appRoleAssignedTo$/)) {
      return { status: 200, body: { value: options.assignments ?? [] } };
    }
    if ((match = path.match(/^\/groups\/([^/]+)\/transitiveMembers\/microsoft\.graph\.user$/))) {
      return { status: 200, body: { value: options.groups?.[match[1]!] ?? [] } };
    }
    return { status: 404, body: { error: { code: 'NotFound', message: path } } };
  }

  const fetchFn = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = init?.method ?? 'GET';
    const path = url.slice(BASE.length);
    const headers = Object.fromEntries(new Headers(init?.headers).entries());
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
    requests.push({ method, path, body });

    if (path === '/$batch') {
      const batch = body as { requests: { id: string; method: string; url: string; headers?: Record<string, string> }[] };
      const responses = batch.requests.map((request) => {
        const headers = Object.fromEntries(new Headers(request.headers ?? {}).entries());
        const response = answer({ method: request.method, path: request.url, headers, batched: true });
        return {
          id: request.id,
          status: response.status,
          headers: response.headers ?? {},
          body: response.bytes ? btoa(String.fromCharCode(...response.bytes)) : response.body,
        };
      });
      return Response.json({ responses });
    }

    const response = answer({ method, path, body, headers, batched: false });
    if (response.bytes) {
      return new Response(new Blob([response.bytes.slice()]), { status: response.status, headers: response.headers });
    }
    return Response.json(response.body ?? null, { status: response.status, headers: response.headers });
  }) as typeof fetch;

  return { fetch: fetchFn, calls, requests, use: (handler) => handlers.push(handler) };
}

/** Speicher wie sessionStorage, je Test neu. */
export function memoryStorage(): Storage {
  const values = new Map<string, string>();
  return {
    get length() {
      return values.size;
    },
    clear: () => values.clear(),
    getItem: (key) => values.get(key) ?? null,
    key: (index) => [...values.keys()][index] ?? null,
    removeItem: (key) => void values.delete(key),
    setItem: (key, value) => void values.set(key, value),
  };
}

/** Anmeldung ohne MSAL für Tests. */
export function fakeAuth(overrides: Partial<DirectoryAuth> = {}): DirectoryAuth & { tokenScopes: string[][] } {
  const tokenScopes: string[][] = [];
  return {
    account: {
      homeAccountId: 'home-1',
      environment: 'login.microsoftonline.com',
      tenantId: 'tenant-1',
      username: 'erika.mustermann@example.org',
      localAccountId: people.erika.id,
      name: 'Erika Mustermann',
    },
    login: async () => {},
    logout: async () => {},
    getAccessToken: async (scopes?: string[]) => {
      tokenScopes.push(scopes ?? []);
      return 'graph-token';
    },
    tokenScopes,
    ...overrides,
  };
}

export const directoryConfig = {
  scopes: ['https://graph.microsoft.com/User.Read', 'https://graph.microsoft.com/User.ReadBasic.All', 'https://graph.microsoft.com/People.Read'],
  presence: true,
  roleFilter: false,
};

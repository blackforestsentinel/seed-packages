import { afterEach, describe, expect, it, vi } from 'vitest';
import { createLocalDirectory, DirectoryError } from './directory.js';
import { createGraphClient } from './graph-client.js';
import { createGraphDirectory, toPresence, type GraphDirectoryOptions } from './graph-directory.js';
import { createFakeGraph, directoryConfig, memoryStorage, people, photoDataUrl, principals, type FakeGraph } from './test/fake-graph.js';
import { avatarColor, AVATAR_COLORS, initials } from './utils.js';

function directory(graph: FakeGraph, options: Partial<GraphDirectoryOptions> = {}) {
  return createGraphDirectory({
    config: directoryConfig,
    getAccessToken: async () => 'graph-token',
    fetch: graph.fetch,
    storage: memoryStorage(),
    batchDelay: 1,
    ...options,
  });
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('initials und avatarColor', () => {
  it('nimmt ersten und letzten Namensteil, auch bei „Nachname, Vorname“', () => {
    expect(initials('Erika Mustermann')).toBe('EM');
    expect(initials('Mustermann, Erika')).toBe('EM');
    expect(initials('Erika Anna Mustermann (Extern)')).toBe('EM');
    expect(initials('erika.mustermann@example.org')).toBe('EM');
    expect(initials('Max')).toBe('M');
    expect(initials('')).toBe('');
    expect(initials(null)).toBe('');
  });

  it('berechnet die Farbe stabil aus der ID', () => {
    const color = avatarColor(people.erika.id);
    expect(AVATAR_COLORS).toContain(color);
    expect(avatarColor(people.erika.id)).toBe(color);
    expect(new Set(Object.values(people).map((person) => avatarColor(person.id))).size).toBeGreaterThan(1);
  });
});

describe('createGraphClient', () => {
  it('bündelt GET-Anfragen in einem $batch und fasst gleiche zusammen', async () => {
    const graph = createFakeGraph();
    const client = createGraphClient({ getToken: async () => 'token', fetch: graph.fetch, batchDelay: 1 });

    const results = await Promise.all([
      client.get(`/users/${people.erika.id}`),
      client.get(`/users/${people.max.id}`),
      client.get(`/users/${people.erika.id}`),
    ]);

    expect(graph.requests).toHaveLength(1);
    expect(graph.requests[0]).toMatchObject({ method: 'POST', path: '/$batch' });
    expect((graph.requests[0]!.body as { requests: unknown[] }).requests).toHaveLength(2);
    expect(results.map((result) => (result.body as { displayName: string }).displayName)).toEqual([
      'Erika Mustermann',
      'Max Mustermann',
      'Erika Mustermann',
    ]);
  });

  it('schickt einzelne Anfragen direkt und mehr als 20 in mehreren $batch', async () => {
    const graph = createFakeGraph();
    const client = createGraphClient({ getToken: async () => 'token', fetch: graph.fetch, batchDelay: 1 });

    await client.get(`/users/${people.erika.id}`);
    expect(graph.requests).toEqual([{ method: 'GET', path: `/users/${people.erika.id}`, body: undefined }]);

    await Promise.all(Array.from({ length: 25 }, (_, index) => client.get(`/users/unknown-${index}`)));
    const batches = graph.requests.slice(1);
    expect(batches.map((request) => (request.body as { requests: unknown[] }).requests.length)).toEqual([20, 5]);
  });

  it('wiederholt gedrosselte Anfragen im $batch nach Retry-After', async () => {
    const graph = createFakeGraph();
    let throttled = false;
    graph.use((call) => {
      if (call.path.startsWith(`/users/${people.max.id}`) && !throttled) {
        throttled = true;
        return { status: 429, headers: { 'Retry-After': '3' } };
      }
      return undefined;
    });
    const waits: number[] = [];
    const client = createGraphClient({
      getToken: async () => 'token',
      fetch: graph.fetch,
      batchDelay: 1,
      sleep: async (ms) => {
        waits.push(ms);
      },
    });

    const [erika, max] = await Promise.all([client.get(`/users/${people.erika.id}`), client.get(`/users/${people.max.id}`)]);

    expect(erika.status).toBe(200);
    expect(max.status).toBe(200);
    expect(waits).toEqual([3000]);
    expect(graph.requests.map((request) => request.method + ' ' + request.path)).toEqual(['POST /$batch', `GET /users/${people.max.id}`]);
  });

  it('wiederholt direkte Anfragen nach 429', async () => {
    const graph = createFakeGraph();
    let count = 0;
    graph.use(() => (count++ === 0 ? { status: 429, headers: { 'Retry-After': '1' } } : undefined));
    const waits: number[] = [];
    const client = createGraphClient({ getToken: async () => 'token', fetch: graph.fetch, sleep: async (ms) => void waits.push(ms) });

    const response = await client.send('GET', `/users/${people.erika.id}`);

    expect(response.status).toBe(200);
    expect(waits).toEqual([1000]);
  });
});

describe('createGraphDirectory', () => {
  it('holt das Token für die Scopes aus auth.directory', async () => {
    const graph = createFakeGraph();
    const scopes: string[][] = [];
    const dir = directory(graph, { getAccessToken: async (requested) => (scopes.push(requested), 'token') });

    await dir.getPerson(people.erika.id);

    expect(scopes).toEqual([directoryConfig.scopes]);
  });

  it('liefert Fotos als data:-URL und merkt sich Personen ohne Foto', async () => {
    const graph = createFakeGraph({ photos: [people.erika.id] });
    const dir = directory(graph);

    const [photo, none] = await Promise.all([dir.getPhoto(people.erika.id, 48), dir.getPhoto(people.max.id, 48)]);

    expect(photo).toBe(photoDataUrl);
    expect(none).toBeNull();
    expect(graph.calls.map((call) => call.path)).toEqual([
      `/users/${people.erika.id}/photos/48x48/$value`,
      `/users/${people.max.id}/photos/48x48/$value`,
    ]);

    await dir.getPhoto(people.max.id, 48);
    expect(dir.peekPhoto(people.max.id, 48)).toBeNull();
    expect(graph.calls).toHaveLength(2);
  });

  it('übernimmt den Cache aus dem sessionStorage und lädt nach Ablauf neu', async () => {
    const graph = createFakeGraph();
    const storage = memoryStorage();
    let now = 1_000_000;
    const first = directory(graph, { storage, now: () => now });
    await first.getPerson(people.erika.id);

    const second = directory(graph, { storage, now: () => now });
    expect(second.peekPerson(people.erika.id)?.displayName).toBe('Erika Mustermann');
    await second.getPerson(people.erika.id);
    expect(graph.calls).toHaveLength(1);

    now += 31 * 60_000;
    expect(second.peekPerson(people.erika.id)).toBeUndefined();
    await second.getPerson(people.erika.id);
    expect(graph.calls).toHaveLength(2);

    second.clear();
    expect(storage.length).toBe(0);
  });

  it('fragt ohne Recht auf Position und Abteilung nur die Grunddaten ab', async () => {
    const graph = createFakeGraph();
    graph.use((call) => (call.path.includes('jobTitle') ? { status: 403, body: { error: { code: 'Authorization_RequestDenied' } } } : undefined));
    const dir = directory(graph);

    const person = await dir.getPerson(people.erika.id);

    expect(person?.displayName).toBe('Erika Mustermann');
    expect(graph.calls.map((call) => call.path)).toEqual([
      `/users/${people.erika.id}?$select=id,displayName,mail,userPrincipalName,jobTitle,department,officeLocation`,
      `/users/${people.erika.id}?$select=id,displayName,mail,userPrincipalName`,
    ]);
  });

  it('sucht zuerst relevante Personen, dann im Verzeichnis, in einem $batch', async () => {
    const graph = createFakeGraph({ relevant: [people.eric] });
    const dir = directory(graph);

    const result = await dir.search('er');

    expect(result.map((person) => person.displayName)).toEqual(['Eric Beispiel', 'Erika Mustermann', 'Erna Platzhalter']);
    expect(graph.requests).toHaveLength(1);
    const users = graph.calls.find((call) => call.path.startsWith('/users?'))!;
    expect(users.headers.consistencylevel).toBe('eventual');
    expect(decodeURIComponent(users.path)).toContain('$search="displayName:er" OR "mail:er"');
    // Treffer aus dem Verzeichnis landen im Profil-Cache.
    expect(dir.peekPerson(people.erna.id)?.department).toBe('Einkauf');
  });

  it('bündelt den Teams-Status und übersetzt ihn', async () => {
    vi.useFakeTimers();
    try {
      const graph = createFakeGraph({
        presence: {
          [people.erika.id]: { availability: 'Available', activity: 'Available' },
          [people.max.id]: { availability: 'Busy', activity: 'InAMeeting' },
        },
      });
      const dir = directory(graph);
      const changes: string[] = [];

      const stopErika = dir.subscribePresence(people.erika.id, () => changes.push('erika'));
      const stopMax = dir.subscribePresence(people.max.id, () => changes.push('max'));
      await vi.advanceTimersByTimeAsync(100);

      expect(graph.requests).toEqual([
        { method: 'POST', path: '/communications/getPresencesByUserId', body: { ids: [people.erika.id, people.max.id] } },
      ]);
      expect(dir.peekPresence(people.erika.id)?.status).toBe('available');
      expect(dir.peekPresence(people.max.id)?.status).toBe('busy');
      expect(changes.sort()).toEqual(['erika', 'max']);

      // Nach einer Minute wieder, gleiches Ergebnis meldet keine Änderung.
      await vi.advanceTimersByTimeAsync(60_100);
      expect(graph.requests).toHaveLength(2);
      expect(changes).toHaveLength(2);

      stopErika();
      stopMax();
      await vi.advanceTimersByTimeAsync(120_000);
      expect(graph.requests).toHaveLength(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('schaltet den Status ohne Presence.Read.All ab', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const graph = createFakeGraph();
    graph.use((call) => (call.path.startsWith('/communications') ? { status: 403, body: {} } : undefined));
    const dir = directory(graph);
    let notified = 0;

    dir.subscribePresence(people.erika.id, () => notified++);
    await vi.waitFor(() => expect(dir.presence).toBe(false));

    expect(notified).toBe(1);
    expect(warn).toHaveBeenCalledOnce();
  });

  it('übersetzt Graph-Status in die Status von Fluent', () => {
    expect(toPresence({ availability: 'AvailableIdle', activity: 'Available' }).status).toBe('available');
    expect(toPresence({ availability: 'BeRightBack', activity: 'BeRightBack' }).status).toBe('away');
    expect(toPresence({ availability: 'DoNotDisturb', activity: 'Presenting' }).status).toBe('do-not-disturb');
    expect(toPresence({ availability: 'Offline', activity: 'OffWork', outOfOfficeSettings: { isOutOfOffice: true } }).status).toBe('out-of-office');
    expect(toPresence({ availability: 'PresenceUnknown', activity: 'PresenceUnknown' }).status).toBe('unknown');
  });

  it('löst Personen mit App-Rolle samt Gruppen auf und filtert nach Rollen', async () => {
    const graph = createFakeGraph({
      appRoles: [
        { id: 'role-admin', value: 'Admin' },
        { id: 'role-reader', value: 'Reader' },
      ],
      assignments: [
        { principalId: people.erika.id, principalType: 'User', principalDisplayName: 'Erika Mustermann', appRoleId: 'role-admin' },
        { principalId: 'group-1', principalType: 'Group', principalDisplayName: 'Einkauf', appRoleId: 'role-admin' },
        { principalId: people.max.id, principalType: 'User', principalDisplayName: 'Max Mustermann', appRoleId: 'role-reader' },
        { principalId: people.eric.id, principalType: 'User', principalDisplayName: 'Eric Beispiel', appRoleId: '00000000-0000-0000-0000-000000000000' },
      ],
      groups: { 'group-1': [people.erna] },
    });
    const dir = directory(graph, { config: { ...directoryConfig, roleFilter: true, apiClientId: 'api-client-id' } });

    const admins = await dir.getAppRoleMembers(['Admin']);
    const all = await dir.getAppRoleMembers();

    expect(admins.map((person) => person.displayName)).toEqual(['Erika Mustermann', 'Erna Platzhalter']);
    expect(all.map((person) => person.displayName)).toEqual(['Erika Mustermann', 'Erna Platzhalter', 'Max Mustermann']);
    const groupCall = graph.calls.find((call) => call.path.startsWith('/groups/'))!;
    expect(groupCall.headers.consistencylevel).toBe('eventual');

    const before = graph.calls.length;
    await dir.getAppRoleMembers(['Admin']);
    expect(graph.calls).toHaveLength(before);
  });

  it('meldet einen fehlenden Rollenfilter und fehlenden Admin-Consent als DirectoryError', async () => {
    const graph = createFakeGraph({ appRoles: [] });
    await expect(directory(graph).getAppRoleMembers()).rejects.toMatchObject({ code: 'not-configured' });

    graph.use((call) => (call.path.includes('appRoleAssignedTo') ? { status: 403, body: { error: { message: 'Insufficient privileges' } } } : undefined));
    const dir = directory(graph, { config: { ...directoryConfig, roleFilter: true, apiClientId: 'api-client-id' } });
    const error = await dir.getAppRoleMembers().catch((e: unknown) => e);
    expect(error).toBeInstanceOf(DirectoryError);
    expect(error).toMatchObject({ code: 'forbidden' });
    expect((error as Error).message).toContain('Application.Read.All');
  });

  it('pausiert Graph nach einem fehlgeschlagenen Token, statt es immer wieder zu versuchen', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const graph = createFakeGraph();
    let attempts = 0;
    const consent = Object.assign(new Error('Kein Token'), { name: 'SeedAuthScopeError' });
    const dir = directory(graph, {
      getAccessToken: async () => {
        attempts++;
        throw consent;
      },
    });

    await expect(dir.getPerson(people.erika.id)).rejects.toBe(consent);
    await expect(dir.getPerson(people.max.id)).rejects.toBe(consent);

    expect(attempts).toBe(1);
    expect(graph.requests).toHaveLength(0);
    expect(warn).toHaveBeenCalledOnce();
  });
});

describe('Service Principals', () => {
  const spConfig = { ...directoryConfig, servicePrincipals: true };
  const roleConfig = { ...spConfig, roleFilter: true, apiClientId: 'api-client-id' };

  it('sucht mit principalTypes auch Service Principals, gefiltert nach Art, im selben $batch', async () => {
    const graph = createFakeGraph();
    const dir = directory(graph, { config: spConfig });

    const result = await dir.search('er', { principalTypes: ['user', 'servicePrincipal'], servicePrincipalTypes: ['ManagedIdentity'] });

    // Verzeichnis und Service Principals im Wechsel, damit Anwendungen nicht hinten herunterfallen.
    expect(result.map((person) => person.displayName)).toEqual(['Erika Mustermann', 'Erfassung Nachtlauf', 'Eric Beispiel', 'Erna Platzhalter']);
    expect(result[0]).toMatchObject({ kind: 'user' });
    expect(result[1]).toEqual({
      id: principals.nightly.id,
      displayName: 'Erfassung Nachtlauf',
      kind: 'servicePrincipal',
      appId: principals.nightly.appId,
      servicePrincipalType: 'ManagedIdentity',
    });
    expect(graph.requests).toHaveLength(1);
    const call = graph.calls.find((entry) => entry.path.startsWith('/servicePrincipals?'))!;
    expect(call.headers.consistencylevel).toBe('eventual');
    expect(decodeURIComponent(call.path)).toContain(`$search="displayName:er"&$filter=servicePrincipalType in ('ManagedIdentity')&$count=true`);
    expect(call.path).toContain('$select=id,displayName,appId,servicePrincipalType');
    expect(dir.peekPerson(principals.nightly.id)?.kind).toBe('servicePrincipal');
  });

  it('sucht nur Service Principals, ohne /me/people und /users', async () => {
    const graph = createFakeGraph();
    const dir = directory(graph, { config: spConfig });

    const result = await dir.search('erf', { principalTypes: ['servicePrincipal'] });

    expect(result.map((person) => person.displayName)).toEqual(['Erfassung Nachtlauf', 'Erfassung Export']);
    expect(graph.calls.map((call) => call.path.split('?')[0])).toEqual(['/servicePrincipals']);
    expect(decodeURIComponent(graph.calls[0]!.path)).not.toContain('$filter');
  });

  it('zeigt Personen weiter, wenn nur die Suche nach Service Principals scheitert', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const graph = createFakeGraph();
    graph.use((call) => (call.path.startsWith('/servicePrincipals?') ? { status: 403, body: { error: { code: 'Authorization_RequestDenied' } } } : undefined));
    const dir = directory(graph, { config: spConfig });

    const result = await dir.search('erika', { principalTypes: ['user', 'servicePrincipal'] });
    expect(result.map((person) => person.displayName)).toEqual(['Erika Mustermann']);
    expect(warn).toHaveBeenCalledOnce();

    const error = await dir.search('erf', { principalTypes: ['servicePrincipal'] }).catch((e: unknown) => e);
    expect(error).toMatchObject({ code: 'forbidden' });
    expect((error as Error).message).toContain('Application.Read.All');
  });

  it('verlangt auth.directory.servicePrincipals für Suche und App-Rollen', async () => {
    const graph = createFakeGraph({ appRoles: [] });
    const dir = directory(graph, { config: { ...directoryConfig, roleFilter: true, apiClientId: 'api-client-id' } });

    expect(dir.servicePrincipals).toBe(false);
    await expect(dir.search('er', { principalTypes: ['user', 'servicePrincipal'] })).rejects.toMatchObject({ code: 'not-configured' });
    await expect(dir.getAppRoleMembers(undefined, { principalTypes: ['servicePrincipal'] })).rejects.toMatchObject({ code: 'not-configured' });
    expect(graph.requests).toHaveLength(0);
  });

  it('erkennt Service Principals über /servicePrincipals/{id} und lädt für sie weder Foto noch Status', async () => {
    const graph = createFakeGraph({ presence: { [principals.nightly.id]: { availability: 'Available', activity: 'Available' } } });
    const dir = directory(graph, { config: spConfig });

    const principal = await dir.getPerson(principals.nightly.id);

    expect(principal).toMatchObject({ kind: 'servicePrincipal', displayName: 'Erfassung Nachtlauf', servicePrincipalType: 'ManagedIdentity' });
    expect(graph.calls.map((call) => call.path.split('?')[0])).toEqual([`/users/${principals.nightly.id}`, `/servicePrincipals/${principals.nightly.id}`]);
    expect((await dir.getPerson(people.erika.id))?.kind).toBe('user');

    const before = graph.requests.length;
    expect(await dir.getPhoto(principals.nightly.id, 48)).toBeNull();
    const stop = dir.subscribePresence(principals.nightly.id, () => {});
    await new Promise((resolve) => setTimeout(resolve, 80));
    stop();
    expect(graph.requests).toHaveLength(before);
  });

  it('fragt ohne auth.directory.servicePrincipals nur /users/{id}', async () => {
    const graph = createFakeGraph();
    const dir = directory(graph);

    expect(await dir.getPerson(principals.nightly.id)).toBeNull();
    expect(graph.calls.map((call) => call.path.split('?')[0])).toEqual([`/users/${principals.nightly.id}`]);
  });

  it('liefert auf Wunsch Service Principals mit App-Rolle, ohne Wunsch nur Personen', async () => {
    const graph = createFakeGraph({
      appRoles: [
        { id: 'role-writer', value: 'Writer' },
        { id: 'role-reader', value: 'Reader' },
      ],
      assignments: [
        { principalId: people.erika.id, principalType: 'User', principalDisplayName: 'Erika Mustermann', appRoleId: 'role-writer' },
        { principalId: principals.nightly.id, principalType: 'ServicePrincipal', principalDisplayName: 'Erfassung Nachtlauf', appRoleId: 'role-writer' },
        { principalId: principals.export.id, principalType: 'ServicePrincipal', principalDisplayName: 'Erfassung Export', appRoleId: 'role-reader' },
        { principalId: 'group-1', principalType: 'Group', principalDisplayName: 'Einkauf', appRoleId: 'role-writer' },
      ],
      groups: { 'group-1': [people.erna] },
    });
    const dir = directory(graph, { config: roleConfig });

    const users = await dir.getAppRoleMembers(['Writer']);
    const both = await dir.getAppRoleMembers(['Writer'], { principalTypes: ['user', 'servicePrincipal'] });
    const groupCalls = () => graph.calls.filter((call) => call.path.startsWith('/groups/')).length;
    const before = groupCalls();
    const apps = await dir.getAppRoleMembers(undefined, { principalTypes: ['servicePrincipal'] });

    expect(users.map((person) => person.displayName)).toEqual(['Erika Mustermann', 'Erna Platzhalter']);
    expect(both.map((person) => person.displayName)).toEqual(['Erfassung Nachtlauf', 'Erika Mustermann', 'Erna Platzhalter']);
    expect(both[0]).toMatchObject({ kind: 'servicePrincipal', appId: principals.nightly.appId, servicePrincipalType: 'ManagedIdentity' });
    expect(apps.map((person) => person.displayName)).toEqual(['Erfassung Export', 'Erfassung Nachtlauf']);
    // Nur Service Principals: Gruppen bleiben unaufgelöst.
    expect(groupCalls()).toBe(before);
    expect(dir.peekPerson(principals.export.id)?.kind).toBe('servicePrincipal');
  });
});

describe('createLocalDirectory', () => {
  it('sucht in der Liste und filtert nach Rollen, ohne Fotos und Status', async () => {
    const dir = createLocalDirectory([
      { id: 'a', displayName: 'Erika Mustermann', mail: 'erika@example.org', roles: ['Admin'] },
      { id: 'b', displayName: 'Max Mustermann', department: 'IT' },
      { id: 'c', displayName: 'Jörg Übermut', roles: ['Reader'] },
    ]);

    expect((await dir.search('must')).map((person) => person.id)).toEqual(['a', 'b']);
    expect((await dir.search('joerg')).map((person) => person.id)).toEqual([]);
    expect((await dir.search('jorg')).map((person) => person.id)).toEqual(['c']);
    expect((await dir.search('ERIKA@')).map((person) => person.id)).toEqual(['a']);
    expect((await dir.getAppRoleMembers(['Admin'])).map((person) => person.id)).toEqual(['a']);
    expect((await dir.getAppRoleMembers()).map((person) => person.id)).toEqual(['a', 'c']);
    expect(await dir.getPhoto('a', 48)).toBeNull();
    expect(dir.presence).toBe(false);
  });

  it('kennt Anwendungen mit kind servicePrincipal', async () => {
    const dir = createLocalDirectory([
      { id: 'a', displayName: 'Erika Mustermann', roles: ['Writer'] },
      { id: 'm', displayName: 'Erfassung Nachtlauf', kind: 'servicePrincipal', servicePrincipalType: 'ManagedIdentity', appId: 'app-m', roles: ['Writer'] },
      { id: 'x', displayName: 'Erfassung Export', kind: 'servicePrincipal', servicePrincipalType: 'Application', roles: ['Writer'] },
    ]);

    expect((await dir.search('er')).map((person) => person.id)).toEqual(['a']);
    expect((await dir.search('er', { principalTypes: ['user', 'servicePrincipal'] })).map((person) => person.id)).toEqual(['a', 'm', 'x']);
    expect((await dir.search('er', { principalTypes: ['servicePrincipal'], servicePrincipalTypes: ['ManagedIdentity'] })).map((person) => person.id)).toEqual(['m']);
    expect(await dir.getPerson('m')).toEqual({ id: 'm', displayName: 'Erfassung Nachtlauf', kind: 'servicePrincipal', appId: 'app-m', servicePrincipalType: 'ManagedIdentity' });
    expect((await dir.getAppRoleMembers(['Writer'])).map((person) => person.id)).toEqual(['a']);
    expect((await dir.getAppRoleMembers(['Writer'], { principalTypes: ['servicePrincipal'] })).map((person) => person.id)).toEqual(['m', 'x']);
    expect(dir.servicePrincipals).toBe(true);
  });
});

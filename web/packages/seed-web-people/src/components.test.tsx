// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { SeedUserProvider, type SeedUser } from '@blackforestsentinel/seed-web-auth/react';
import type { SeedAuthConfig, SeedLocalAuthConfig } from '@blackforestsentinel/seed-web-auth';
import {
  AccessDenied,
  Avatar,
  AvatarGroup,
  createGraphDirectory,
  CurrentUserMenu,
  DirectoryProvider,
  PeoplePicker,
  PersonLabel,
  RoleBadge,
  SignInPage,
  type Directory,
  type DirectoryAuth,
  type Person,
} from './index.js';
import { createFakeGraph, directoryConfig, fakeAuth, memoryStorage, people, photoDataUrl, type FakeGraph, type FakeGraphOptions } from './test/fake-graph.js';

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

const graphConfig: SeedAuthConfig = {
  clientId: 'spa-id',
  tenantId: 'tenant-id',
  apiScope: 'api://api-id/access_as_user',
  directory: directoryConfig,
};

const admin: SeedUser = {
  name: 'Erika Mustermann',
  username: 'erika.mustermann@example.org',
  objectId: people.erika.id,
  tenantId: 'tenant-id',
  roles: ['Admin'],
  roleDetails: [{ value: 'Admin', displayName: 'Administrator' }],
  capabilities: ['settings.manage'],
};

function graphDirectory(graph: FakeGraph, config = directoryConfig): Directory {
  return createGraphDirectory({
    config,
    getAccessToken: async () => 'graph-token',
    fetch: graph.fetch,
    storage: memoryStorage(),
    batchDelay: 1,
  });
}

function setup(
  node: ReactNode,
  options: { graph?: FakeGraphOptions; directory?: (graph: FakeGraph) => Directory; auth?: DirectoryAuth | null; user?: SeedUser | null; config?: SeedAuthConfig | SeedLocalAuthConfig } = {},
) {
  const graph = createFakeGraph(options.graph);
  const directory = options.config ? undefined : (options.directory ?? graphDirectory)(graph);
  const auth = options.auth === undefined ? fakeAuth() : options.auth;
  render(
    <SeedUserProvider user={options.user === undefined ? admin : options.user}>
      <DirectoryProvider config={options.config ?? graphConfig} auth={auth} directory={directory} fetch={graph.fetch}>
        {node}
      </DirectoryProvider>
    </SeedUserProvider>,
  );
  return { graph, auth };
}

describe('Avatar', () => {
  it('zeigt das Foto als data:-URL und sonst Initialen', async () => {
    setup(
      <>
        <Avatar id={people.erika.id} name="Erika Mustermann" />
        <Avatar id={people.max.id} name="Max Mustermann" />
      </>,
      { graph: { photos: [people.erika.id] } },
    );

    const erika = screen.getByRole('img', { name: 'Erika Mustermann' });
    const max = screen.getByRole('img', { name: 'Max Mustermann' });
    expect(erika.dataset.state).toBe('loading');

    await waitFor(() => expect(erika.dataset.state).toBe('image'));
    expect(erika.querySelector('img')?.getAttribute('src')).toBe(photoDataUrl);
    await waitFor(() => expect(max.dataset.state).toBe('initials'));
    expect(max.textContent).toBe('MM');
    expect(max.dataset.color).toMatch(/^[a-z-]+$/);
  });

  it('lädt Fotos für eine Liste in einem $batch und den Namen nur ohne name', async () => {
    const { graph } = setup(
      <>
        <Avatar id={people.erika.id} />
        <Avatar id={people.max.id} name="Max Mustermann" />
        <Avatar id={people.eric.id} name="Eric Beispiel" />
        <Avatar id={people.eric.id} name="Eric Beispiel" />
      </>,
    );

    expect(await screen.findByRole('img', { name: 'Erika Mustermann' })).toBeTruthy();
    await waitFor(() => expect(screen.getAllByText('EB')).toHaveLength(2));

    expect(graph.requests).toHaveLength(1);
    expect(graph.calls.map((call) => call.path).sort()).toEqual(
      [
        `/users/${people.erika.id}/photos/64x64/$value`,
        `/users/${people.erika.id}?$select=id,displayName,mail,userPrincipalName,jobTitle,department,officeLocation`,
        `/users/${people.eric.id}/photos/64x64/$value`,
        `/users/${people.max.id}/photos/64x64/$value`,
      ].sort(),
    );
  });

  it('zeigt ohne auth.directory sofort Initialen und ruft Graph nicht auf', async () => {
    const fetchSpy = vi.fn();
    render(
      <DirectoryProvider config={{ ...graphConfig, directory: undefined }} auth={fakeAuth()} fetch={fetchSpy}>
        <Avatar id={people.erika.id} name="Erika Mustermann" size={48} shape="square" />
        <Avatar id={people.max.id} size={44} />
      </DirectoryProvider>,
    );

    const erika = screen.getByRole('img', { name: 'Erika Mustermann' });
    expect(erika.dataset.state).toBe('initials');
    expect(erika.dataset.size).toBe('48');
    expect(erika.dataset.shape).toBe('square');
    const unknown = screen.getByRole('img', { name: 'Person' });
    expect(unknown.dataset.state).toBe('icon');
    expect(unknown.style.getPropertyValue('--seed-avatar-size')).toBe('44px');
    await act(async () => {});
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('zeigt den Teams-Status als Punkt und im Namen für Screenreader', async () => {
    setup(<Avatar id={people.erika.id} name="Erika Mustermann" presence />, {
      graph: { presence: { [people.erika.id]: { availability: 'DoNotDisturb', activity: 'Presenting' } } },
    });

    const avatar = await screen.findByRole('img', { name: 'Erika Mustermann, Nicht stören' });
    expect(avatar.dataset.presence).toBe('do-not-disturb');
    expect(avatar.querySelector('.seed-presence')?.getAttribute('data-status')).toBe('do-not-disturb');
  });
});

describe('PersonLabel und AvatarGroup', () => {
  it('lädt den Namen und zeigt die zweite Zeile aus dem Profil', async () => {
    setup(<PersonLabel id={people.erika.id} secondaryField="department" />);

    expect(await screen.findByText('Erika Mustermann')).toBeTruthy();
    expect(screen.getByText('Buchhaltung')).toBeTruthy();
  });

  it('öffnet die Karte per Tastatur und schließt sie mit Escape', async () => {
    setup(<PersonLabel id={people.erika.id} name="Erika Mustermann" card />);

    const trigger = screen.getByRole('button', { name: /Erika Mustermann/ });
    act(() => trigger.focus());
    const card = await screen.findByRole('group', { name: 'Erika Mustermann' });
    expect(await within(card).findByText('Buchhalterin')).toBeTruthy();
    expect(within(card).getByRole('link', { name: /erika.mustermann@example.org/ }).getAttribute('href')).toBe('mailto:erika.mustermann@example.org');
    expect(within(card).getByRole('link', { name: /Chat in Teams/ }).getAttribute('href')).toBe(
      'https://teams.microsoft.com/l/chat/0/0?users=erika.mustermann%40example.org',
    );
    expect(trigger.getAttribute('aria-expanded')).toBe('true');

    fireEvent.keyDown(trigger, { key: 'Escape' });
    expect(screen.queryByRole('group', { name: 'Erika Mustermann' })).toBeNull();
  });

  it('zeigt höchstens max Avatare und „+n“ mit den übrigen Namen', () => {
    setup(
      <AvatarGroup
        max={3}
        people={[
          { id: 'a', name: 'Erika Mustermann' },
          { id: 'b', name: 'Max Mustermann' },
          { id: 'c', name: 'Eric Beispiel' },
          { id: 'd', name: 'Erna Platzhalter' },
        ]}
      />,
      { config: { mode: 'local' } },
    );

    const group = screen.getByRole('group', { name: '4 Personen' });
    expect(within(group).getAllByRole('img')).toHaveLength(3);
    expect(within(group).getByRole('img', { name: '2 weitere: Eric Beispiel, Erna Platzhalter' }).textContent).toBe('+2');
  });
});

describe('PeoplePicker', () => {
  function Picker(props: Partial<Parameters<typeof PeoplePicker>[0]>) {
    return <PeoplePicker debounceMs={0} {...props} />;
  }

  const message = () => document.querySelector('.seed-picker__message')?.textContent;
  const names = (options: HTMLElement[]) => options.map((option) => option.querySelector('.seed-picker__option-name')?.textContent);

  it('sucht ab zwei Zeichen und wählt per Tastatur aus', async () => {
    const onChange = vi.fn();
    const { graph } = setup(<Picker onChange={onChange} />, { graph: { relevant: [people.eric] } });
    const input = screen.getByRole('combobox', { name: 'Person suchen' });

    fireEvent.change(input, { target: { value: 'e' } });
    expect(message()).toBe('Mindestens 2 Zeichen eingeben');
    expect(screen.getByRole('status').textContent).toBe('Mindestens 2 Zeichen eingeben');
    expect(graph.requests).toHaveLength(0);

    fireEvent.change(input, { target: { value: 'er' } });
    const listbox = await screen.findByRole('listbox');
    const options = await within(listbox).findAllByRole('option');
    expect(names(options)).toEqual(['Eric Beispiel', 'Erika Mustermann', 'Erna Platzhalter']);
    expect(options[1]!.querySelector('.seed-picker__option-secondary')?.textContent).toBe('Buchhalterin · Buchhaltung');
    expect(screen.getByRole('status').textContent).toBe('3 Vorschläge');
    expect(input.getAttribute('aria-expanded')).toBe('true');
    expect(input.getAttribute('aria-activedescendant')).toBe(options[0]!.id);

    fireEvent.keyDown(input, { key: 'ArrowDown' });
    expect(input.getAttribute('aria-activedescendant')).toBe(options[1]!.id);
    expect(options[1]!.getAttribute('aria-selected')).toBe('true');
    fireEvent.keyDown(input, { key: 'ArrowUp' });
    fireEvent.keyDown(input, { key: 'ArrowUp' });
    expect(input.getAttribute('aria-activedescendant')).toBe(options[2]!.id);

    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onChange).toHaveBeenLastCalledWith([expect.objectContaining({ id: people.erna.id, displayName: 'Erna Platzhalter' })]);
    expect((input as HTMLInputElement).value).toBe('');
    expect(screen.getByText('Erna Platzhalter')).toBeTruthy();
    expect(input.getAttribute('aria-expanded')).toBe('false');
  });

  it('nimmt mehrere Personen als Chips und entfernt sie per Rücktaste und Knopf', async () => {
    const onChange = vi.fn();
    setup(<Picker multiple onChange={onChange} defaultValue={[{ id: people.max.id, displayName: 'Max Mustermann' }]} />);
    const input = screen.getByRole('combobox');

    fireEvent.change(input, { target: { value: 'erika' } });
    fireEvent.click(await screen.findByRole('option', { name: /Erika Mustermann/ }));
    expect(onChange).toHaveBeenLastCalledWith([expect.objectContaining({ id: people.max.id }), expect.objectContaining({ id: people.erika.id })]);

    // Bereits gewählte Personen erscheinen nicht noch einmal.
    fireEvent.change(input, { target: { value: 'muster' } });
    await waitFor(() => expect(message()).toBe('Keine Treffer'));

    fireEvent.change(input, { target: { value: '' } });
    fireEvent.keyDown(input, { key: 'Backspace' });
    expect(onChange).toHaveBeenLastCalledWith([expect.objectContaining({ id: people.max.id })]);

    fireEvent.click(screen.getByRole('button', { name: 'Max Mustermann entfernen' }));
    expect(onChange).toHaveBeenLastCalledWith([]);
  });

  it('schließt mit Escape und wendet filter und renderOption an', async () => {
    setup(
      <Picker
        filter={(person) => person.department !== 'Vertrieb'}
        renderOption={(person, state) => `${person.displayName}${state.active ? ' (aktiv)' : ''}`}
      />,
    );
    const input = screen.getByRole('combobox');

    fireEvent.change(input, { target: { value: 'er' } });
    const options = await screen.findAllByRole('option');
    expect(options.map((option) => option.textContent)).toEqual(['Erika Mustermann (aktiv)', 'Erna Platzhalter']);

    fireEvent.keyDown(input, { key: 'Escape' });
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(input.getAttribute('aria-expanded')).toBe('false');
    fireEvent.keyDown(input, { key: 'Escape' });
    expect((input as HTMLInputElement).value).toBe('');
  });

  it('zeigt mit onlyWithAppRole nur Personen mit Rolle, auch aus Gruppen', async () => {
    const roleConfig = { ...directoryConfig, roleFilter: true, apiClientId: 'api-client-id' };
    const { graph } = setup(<Picker onlyWithAppRole roles={['Admin']} />, {
      graph: {
        appRoles: [{ id: 'role-admin', value: 'Admin' }],
        assignments: [
          { principalId: people.erika.id, principalType: 'User', principalDisplayName: 'Erika Mustermann', appRoleId: 'role-admin' },
          { principalId: 'group-einkauf', principalType: 'Group', principalDisplayName: 'Einkauf', appRoleId: 'role-admin' },
        ],
        groups: { 'group-einkauf': [people.erna] },
      },
      directory: (fake) => graphDirectory(fake, roleConfig),
    });
    // Die Verzeichnissuche findet Erna nicht; sie kommt aus der aufgelösten Gruppe.
    graph.use((call) => (call.path.startsWith('/users?') ? { status: 200, body: { value: [people.erika, people.eric] } } : undefined));

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'er' } });
    const options = await screen.findAllByRole('option');

    expect(names(options)).toEqual(['Erika Mustermann', 'Erna Platzhalter']);
    expect(graph.calls.some((call) => call.path.startsWith('/groups/group-einkauf/transitiveMembers/microsoft.graph.user'))).toBe(true);
  });

  it('meldet onlyWithAppRole ohne roleFilter, statt alle zu zeigen', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    const { graph } = setup(<Picker onlyWithAppRole />);
    const input = screen.getByRole('combobox');

    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: 'er' } });

    expect(message()).toMatch(/Filter nach App-Rolle ist nicht eingerichtet/);
    expect(input.closest('.seed-picker')?.getAttribute('data-state')).toBe('error');
    expect(screen.queryAllByRole('option')).toHaveLength(0);
    expect(error).toHaveBeenCalledWith(expect.stringContaining('roleFilter'));
    await act(async () => {});
    expect(graph.requests).toHaveLength(0);
  });

  it('sucht im lokalen Modus in auth.directory.people', async () => {
    const fetchSpy = vi.fn();
    render(
      <DirectoryProvider
        config={{
          mode: 'local',
          directory: {
            people: [
              { id: 'p1', displayName: 'Erika Mustermann', department: 'Buchhaltung' },
              { id: 'p2', displayName: 'Max Mustermann', department: 'IT' },
            ],
          },
        }}
        fetch={fetchSpy}
      >
        <Picker />
        <PersonLabel id="p2" />
      </DirectoryProvider>,
    );

    expect(screen.getByText('Max Mustermann')).toBeTruthy();
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'eri' } });
    expect(names(await screen.findAllByRole('option'))).toEqual(['Erika Mustermann']);
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('erklärt, dass die Suche ohne Verzeichnis nicht eingerichtet ist', () => {
    render(
      <DirectoryProvider config={{ mode: 'local' }}>
        <Picker />
      </DirectoryProvider>,
    );

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'erika' } });
    expect(message()).toBe('Die Personensuche ist nicht eingerichtet.');
  });

  it('übernimmt eine gesteuerte Auswahl im Einzelmodus', async () => {
    const selected: Person[] = [{ id: people.max.id, displayName: 'Max Mustermann' }];
    const onChange = vi.fn();
    setup(<Picker value={selected} onChange={onChange} />);
    const input = screen.getByRole('combobox');

    expect(screen.getByText('Max Mustermann')).toBeTruthy();
    expect(input.getAttribute('placeholder')).toBeNull();
    fireEvent.change(input, { target: { value: 'erika' } });
    fireEvent.click(await screen.findByRole('option', { name: /Erika Mustermann/ }));

    expect(onChange).toHaveBeenLastCalledWith([expect.objectContaining({ id: people.erika.id })]);
    // Gesteuert: ohne neues value bleibt die Anzeige.
    expect(screen.getByText('Max Mustermann')).toBeTruthy();
  });
});

describe('CurrentUserMenu', () => {
  it('zeigt Avatar, Name und Rolle mit Anzeigenamen und meldet ab', async () => {
    const logout = vi.fn(async () => {});
    const auth = fakeAuth({ logout });
    const storage = memoryStorage();
    const graph = createFakeGraph({ photos: [people.erika.id] });
    const directory = createGraphDirectory({ config: directoryConfig, getAccessToken: async () => 't', fetch: graph.fetch, storage, batchDelay: 1 });
    render(
      <SeedUserProvider user={admin}>
        <DirectoryProvider config={graphConfig} auth={auth} directory={directory}>
          <CurrentUserMenu />
        </DirectoryProvider>
      </SeedUserProvider>,
    );

    expect(screen.getByText('Erika Mustermann')).toBeTruthy();
    expect(screen.getByText('Administrator')).toBeTruthy();
    await waitFor(() => expect(document.querySelector('.seed-avatar img')).toBeTruthy());
    expect(storage.length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('button', { name: 'Abmelden' }));
    await waitFor(() => expect(logout).toHaveBeenCalledOnce());
    // Abmelden leert die Zwischenspeicher.
    expect(storage.length).toBe(0);
  });

  it('zeigt den Namen aus der Anmeldung, solange /api/me lädt, und ohne Anmeldung nichts', () => {
    const { unmount } = render(
      <SeedUserProvider load={() => new Promise<SeedUser>(() => {})}>
        <DirectoryProvider config={graphConfig} auth={fakeAuth()}>
          <CurrentUserMenu />
        </DirectoryProvider>
      </SeedUserProvider>,
    );
    expect(screen.getByText('Erika Mustermann')).toBeTruthy();
    unmount();

    const { container } = render(
      <SeedUserProvider user={null}>
        <DirectoryProvider config={{ mode: 'local' }}>
          <CurrentUserMenu />
        </DirectoryProvider>
      </SeedUserProvider>,
    );
    expect(container.textContent).toBe('');
  });

  it('bedient das Menü mit der Tastatur', async () => {
    const onHelp = vi.fn();
    setup(
      <CurrentUserMenu
        onSignOut={null}
        menuItems={[
          { label: 'Konto', href: '/konto' },
          { label: 'Version 1.0', disabled: true },
          { label: 'Hilfe', onSelect: onHelp },
        ]}
      />,
      { config: { mode: 'local' }, auth: null },
    );

    expect(screen.queryByRole('button', { name: 'Abmelden' })).toBeNull();
    const trigger = screen.getByRole('button', { name: /Erika Mustermann/ });
    expect(trigger.getAttribute('aria-haspopup')).toBe('menu');

    fireEvent.keyDown(trigger, { key: 'ArrowDown' });
    const menu = screen.getByRole('menu', { name: 'Kontomenü' });
    const items = within(menu).getAllByRole('menuitem');
    await waitFor(() => expect(document.activeElement).toBe(items[0]));

    fireEvent.keyDown(menu, { key: 'ArrowDown' });
    await waitFor(() => expect(document.activeElement).toBe(items[2]));
    fireEvent.click(items[2]!);
    expect(onHelp).toHaveBeenCalledOnce();
    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(trigger);

    fireEvent.click(trigger);
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
    expect(screen.queryByRole('menu')).toBeNull();
  });
});

describe('AccessDenied, SignInPage und RoleBadge', () => {
  it('erklärt fehlende Rollen, nennt die Ansprechperson und bietet einen Kontowechsel', async () => {
    const logout = vi.fn(async () => {});
    setup(<AccessDenied appName="Kundenportal" contact={{ name: 'IT-Service', email: 'it@example.org' }} />, {
      user: { ...admin, roles: [], roleDetails: [], capabilities: [] },
      auth: fakeAuth({ logout }),
      config: { mode: 'local' },
    });

    expect(screen.getByRole('heading', { name: 'Kein Zugriff' })).toBeTruthy();
    expect(screen.getByText(/hast in „Kundenportal“ aber noch keine Rolle/)).toBeTruthy();
    expect(screen.getByRole('link', { name: 'IT-Service' }).getAttribute('href')).toBe('mailto:it@example.org');
    expect(screen.getByText('Angemeldet als')).toBeTruthy();
    expect(screen.getByText('erika.mustermann@example.org')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Mit anderem Konto anmelden' }));
    await waitFor(() => expect(logout).toHaveBeenCalledOnce());
  });

  it('nennt bei vorhandener Rolle die fehlende Capability', () => {
    setup(<AccessDenied capability="invoices.write" headingLevel={2} />, { config: { mode: 'local' }, auth: null });

    expect(screen.getByRole('heading', { level: 2, name: 'Kein Zugriff' })).toBeTruthy();
    expect(screen.getByText(/Für diesen Bereich von dieser Anwendung fehlt dir eine Berechtigung/)).toBeTruthy();
    expect(screen.getByText('invoices.write')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Mit anderem Konto anmelden' })).toBeNull();
  });

  it('meldet über die Anmeldeseite an und zeigt Fehler', async () => {
    const login = vi.fn(async () => {
      throw new Error('popup blocked');
    });
    setup(<SignInPage appName="Kundenportal" />, { auth: fakeAuth({ login }), user: null, config: { mode: 'local' } });

    expect(screen.getByRole('heading', { name: 'Kundenportal' })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Anmelden' }));

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(login).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Anmelden' })).toBeTruthy();
  });

  it('zeigt Rollen mit dem Anzeigenamen aus /api/me', () => {
    setup(
      <>
        <RoleBadge role="Admin" />
        <RoleBadge role="Reader" appearance="outline" />
      </>,
      { config: { mode: 'local' } },
    );

    expect(screen.getByText('Administrator').dataset.role).toBe('Admin');
    expect(screen.getByText('Reader').dataset.appearance).toBe('outline');
  });
});

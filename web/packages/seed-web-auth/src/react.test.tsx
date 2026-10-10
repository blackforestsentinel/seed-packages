// @vitest-environment jsdom
import { act, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it } from 'vitest';
import { IfCapability, SeedUserProvider, useSeedUser } from './react.js';
import { hasCapability, loadSeedUser, type SeedUser } from './user.js';

declare global {
  var IS_REACT_ACT_ENVIRONMENT: boolean;
}
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const admin: SeedUser = {
  name: 'Erika Muster',
  username: 'erika@example.org',
  objectId: 'oid',
  tenantId: 'tid',
  roles: ['Admin'],
  capabilities: ['invoices.read', 'invoices.write'],
};

const containers: HTMLElement[] = [];

async function render(node: ReactNode) {
  const container = document.createElement('div');
  containers.push(container);
  const root = createRoot(container);
  await act(async () => root.render(node));
  return container;
}

afterEach(() => {
  containers.splice(0).forEach((c) => c.remove());
});

function Status() {
  const state = useSeedUser();
  return <span>{state.status}</span>;
}

describe('IfCapability', () => {
  it('zeigt den Inhalt nur mit der Capability', async () => {
    const container = await render(
      <SeedUserProvider user={admin}>
        <IfCapability capability="invoices.write">schreiben</IfCapability>
        <IfCapability capability="invoices.delete" fallback="nicht erlaubt">
          löschen
        </IfCapability>
      </SeedUserProvider>,
    );

    expect(container.textContent).toBe('schreibennicht erlaubt');
  });

  it('blendet ohne Provider alles aus', async () => {
    const container = await render(<IfCapability capability="invoices.read">lesen</IfCapability>);

    expect(container.textContent).toBe('');
  });
});

describe('SeedUserProvider', () => {
  it('lädt die Person einmal und zeigt danach die Capabilities', async () => {
    let calls = 0;
    let resolve: (user: SeedUser) => void = () => {};
    const load = () => {
      calls++;
      return new Promise<SeedUser>((r) => (resolve = r));
    };

    const container = await render(
      <SeedUserProvider load={load}>
        <Status />
        <IfCapability capability="invoices.read">lesen</IfCapability>
      </SeedUserProvider>,
    );
    expect(container.textContent).toBe('loading');

    await act(async () => resolve(admin));

    expect(container.textContent).toBe('readylesen');
    expect(calls).toBe(1);
  });

  it('meldet Fehler und blendet aus', async () => {
    const container = await render(
      <SeedUserProvider load={() => Promise.reject(new Error('offline'))}>
        <Status />
        <IfCapability capability="invoices.read">lesen</IfCapability>
      </SeedUserProvider>,
    );

    expect(container.textContent).toBe('error');
  });
});

describe('loadSeedUser', () => {
  it('liest /api/me und ergänzt fehlende Felder', async () => {
    const paths: string[] = [];
    const http = {
      get: async <T,>(path: string) => {
        paths.push(path);
        return { name: 'Erika Muster', capabilities: ['invoices.read'] } as T;
      },
    };

    const user = await loadSeedUser(http);

    expect(paths).toEqual(['/api/me']);
    expect(user).toEqual({
      name: 'Erika Muster',
      username: null,
      objectId: null,
      tenantId: null,
      roles: [],
      capabilities: ['invoices.read'],
    });
    expect(hasCapability(user, 'invoices.read')).toBe(true);
    expect(hasCapability(user, 'invoices.write')).toBe(false);
    expect(hasCapability(null, 'invoices.read')).toBe(false);
  });
});

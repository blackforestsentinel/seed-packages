import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { SeedAuth, SeedAuthConfig, SeedLocalAuthConfig } from '@blackforestsentinel/seed-web-auth';
import { createEmptyDirectory, createLocalDirectory, type Directory } from './directory.js';
import { createGraphDirectory } from './graph-directory.js';

/** Was die Komponenten von der Anmeldung brauchen; passt zu SeedAuth aus seed-web-auth. */
export type DirectoryAuth = Pick<SeedAuth, 'account' | 'getAccessToken' | 'login' | 'logout'>;

interface DirectoryContextValue {
  directory: Directory;
  auth: DirectoryAuth | null;
}

const empty = createEmptyDirectory();
const DirectoryContext = createContext<DirectoryContextValue>({ directory: empty, auth: null });

export interface DirectoryProviderProps {
  /** Auth-Teil aus config.json (config.auth); entscheidet über Graph, lokale Liste oder nichts. */
  config?: SeedAuthConfig | SeedLocalAuthConfig | null;
  /** Anmeldung aus createSeedAuth; null im lokalen Modus und ohne sso. */
  auth?: DirectoryAuth | null;
  /** Eigenes Verzeichnis statt des aus config gebauten, etwa für Tests. */
  directory?: Directory;
  /** Eigenes fetch für Graph, etwa für Tests. */
  fetch?: typeof fetch;
  children?: ReactNode;
}

/**
 * Stellt das Verzeichnis und die Anmeldung für alle Komponenten und Hooks bereit. Gehört in
 * SeedUserProvider (seed-web-auth/react), damit Name und Rollen aus /api/me ankommen.
 */
export function DirectoryProvider({ config, auth = null, directory, fetch, children }: DirectoryProviderProps) {
  const value = useMemo(
    () => ({ directory: directory ?? createDirectory(config, auth, { fetch }), auth }),
    [config, auth, directory, fetch],
  );
  return <DirectoryContext.Provider value={value}>{children}</DirectoryContext.Provider>;
}

/**
 * Baut das Verzeichnis aus config.auth: mit auth.directory und Anmeldung Graph, im lokalen Modus die
 * Liste auth.directory.people, sonst eines ohne Daten.
 */
export function createDirectory(
  config: SeedAuthConfig | SeedLocalAuthConfig | null | undefined,
  auth: DirectoryAuth | null,
  options: { fetch?: typeof fetch } = {},
): Directory {
  if (!config) {
    return empty;
  }
  if ('mode' in config && config.mode === 'local') {
    return config.directory ? createLocalDirectory(config.directory.people ?? []) : empty;
  }
  const directory = (config as SeedAuthConfig).directory;
  if (!directory || !auth || !Array.isArray(directory.scopes) || directory.scopes.length === 0) {
    return empty;
  }
  return createGraphDirectory({
    config: directory,
    getAccessToken: (scopes) => auth.getAccessToken(scopes),
    cacheKey: auth.account?.homeAccountId,
    fetch: options.fetch,
  });
}

/** Das Verzeichnis aus DirectoryProvider; ohne Provider eines ohne Daten. */
export function useDirectory(): Directory {
  return useContext(DirectoryContext).directory;
}

/** Anmeldung aus DirectoryProvider oder null. */
export function useDirectoryAuth(): DirectoryAuth | null {
  return useContext(DirectoryContext).auth;
}

/**
 * Abmelden samt Leeren der Zwischenspeicher, damit keine Profile im Tab bleiben; null ohne Anmeldung
 * (lokaler Modus, kein sso).
 */
export function useSignOut(): (() => Promise<void>) | null {
  const { directory, auth } = useContext(DirectoryContext);
  return useMemo(
    () =>
      auth
        ? async () => {
            directory.clear();
            await auth.logout();
          }
        : null,
    [directory, auth],
  );
}

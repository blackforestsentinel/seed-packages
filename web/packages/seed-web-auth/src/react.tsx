import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { SeedUser } from './user.js';

export { hasCapability, loadSeedUser } from './user.js';
export type { SeedUser, SeedUserSource } from './user.js';

/** Stand der angemeldeten Person: wird geladen, ist da (oder null ohne Anmeldung) oder ist fehlgeschlagen. */
export type SeedUserState =
  | { status: 'loading'; user: null }
  | { status: 'ready'; user: SeedUser | null }
  | { status: 'error'; user: null; error: unknown };

// Ohne Provider gibt es keine Person und damit keine Capabilities: Geschützte Elemente bleiben aus.
const SeedUserContext = createContext<SeedUserState>({ status: 'ready', user: null });

export interface SeedUserProviderProps {
  /** Lädt die Person einmal beim Einhängen, z. B. () => loadSeedUser(http). */
  load?: () => Promise<SeedUser>;
  /** Bereits bekannte Person oder null; hat Vorrang vor load. */
  user?: SeedUser | null;
  children?: ReactNode;
}

/** Stellt die angemeldete Person mit ihren Capabilities für useSeedUser, useCapabilities und IfCapability bereit. */
export function SeedUserProvider({ load, user, children }: SeedUserProviderProps) {
  const [loaded, setLoaded] = useState<SeedUserState>(load ? { status: 'loading', user: null } : { status: 'ready', user: null });
  // load ist oft eine Inline-Funktion; ein Effekt darauf würde bei jedem Rendern neu laden.
  const loadRef = useRef(load);

  useEffect(() => {
    const loadUser = loadRef.current;
    if (user !== undefined || !loadUser) {
      return;
    }

    let active = true;
    loadUser().then(
      (result) => active && setLoaded({ status: 'ready', user: result }),
      (error: unknown) => active && setLoaded({ status: 'error', user: null, error }),
    );
    return () => {
      active = false;
    };
  }, [user]);

  const state: SeedUserState = user !== undefined ? { status: 'ready', user } : loaded;
  return <SeedUserContext.Provider value={state}>{children}</SeedUserContext.Provider>;
}

/** Angemeldete Person samt Ladezustand. */
export function useSeedUser(): SeedUserState {
  return useContext(SeedUserContext);
}

/** Capabilities der angemeldeten Person; leer, solange sie lädt oder ohne Anmeldung. */
export function useCapabilities(): ReadonlySet<string> {
  const { user } = useContext(SeedUserContext);
  return useMemo(() => new Set(user?.capabilities ?? []), [user]);
}

export interface IfCapabilityProps {
  capability: string;
  /** Anzeige ohne die Capability, Default nichts. */
  fallback?: ReactNode;
  children?: ReactNode;
}

/**
 * Zeigt den Inhalt nur mit der Capability. Nur für die Anzeige: Durchgesetzt wird sie in der API
 * per [RequireCapability].
 */
export function IfCapability({ capability, fallback = null, children }: IfCapabilityProps) {
  return <>{useCapabilities().has(capability) ? children : fallback}</>;
}

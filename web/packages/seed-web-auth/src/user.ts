/**
 * Angemeldete Person aus GET /api/me (SeedUserInfo aus Bfs.Seed.Auth). Die Capabilities löst die
 * API aus den App-Rollen auf; das Frontend kennt die Zuordnung nicht und blendet nur ein oder aus.
 */
export interface SeedUser {
  name: string | null;
  username: string | null;
  objectId: string | null;
  tenantId: string | null;
  roles: string[];
  capabilities: string[];
}

/** Passt zu createHttpClient aus seed-web-core, der das Bearer-Token anhängt. */
export interface SeedUserSource {
  get<T>(path: string): Promise<T>;
}

/** Lädt die angemeldete Person samt Capabilities von der API. */
export async function loadSeedUser(http: SeedUserSource, path = '/api/me'): Promise<SeedUser> {
  const user = (await http.get<Partial<SeedUser> | null>(path)) ?? {};
  return {
    name: user.name ?? null,
    username: user.username ?? null,
    objectId: user.objectId ?? null,
    tenantId: user.tenantId ?? null,
    roles: Array.isArray(user.roles) ? user.roles : [],
    capabilities: Array.isArray(user.capabilities) ? user.capabilities : [],
  };
}

/**
 * Ob die Person die Capability hat. Nur für die Anzeige: Durchgesetzt wird sie in der API per
 * [RequireCapability].
 */
export function hasCapability(user: SeedUser | null | undefined, capability: string): boolean {
  return user?.capabilities.includes(capability) ?? false;
}

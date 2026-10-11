/** App-Rolle mit dem Anzeigenamen aus project.yaml, etwa Admin als „Administrator“. */
export interface SeedRoleDetail {
  value: string;
  displayName: string;
}

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
  /** Anzeigenamen der Rollen; ältere Versionen von Bfs.Seed.Auth liefern sie nicht. */
  roleDetails?: SeedRoleDetail[];
  capabilities: string[];
}

/** Passt zu createHttpClient aus seed-web-core, der das Bearer-Token anhängt. */
export interface SeedUserSource {
  get<T>(path: string): Promise<T>;
}

/** Lädt die angemeldete Person samt Capabilities von der API. */
export async function loadSeedUser(http: SeedUserSource, path = '/api/me'): Promise<SeedUser> {
  const user = (await http.get<Partial<SeedUser> | null>(path)) ?? {};
  const result: SeedUser = {
    name: user.name ?? null,
    username: user.username ?? null,
    objectId: user.objectId ?? null,
    tenantId: user.tenantId ?? null,
    roles: Array.isArray(user.roles) ? user.roles : [],
    capabilities: Array.isArray(user.capabilities) ? user.capabilities : [],
  };
  if (Array.isArray(user.roleDetails)) {
    result.roleDetails = user.roleDetails.filter(
      (role): role is SeedRoleDetail => typeof role?.value === 'string' && typeof role.displayName === 'string',
    );
  }
  return result;
}

/** Anzeigenamen der Rollen der Person in der Reihenfolge von roles; ohne Anzeigenamen der Wert. */
export function roleDisplayNames(user: SeedUser | null | undefined): string[] {
  return (user?.roles ?? []).map((role) => user?.roleDetails?.find((detail) => detail.value === role)?.displayName || role);
}

/**
 * Ob die Person die Capability hat. Nur für die Anzeige: Durchgesetzt wird sie in der API per
 * [RequireCapability].
 */
export function hasCapability(user: SeedUser | null | undefined, capability: string): boolean {
  return user?.capabilities.includes(capability) ?? false;
}

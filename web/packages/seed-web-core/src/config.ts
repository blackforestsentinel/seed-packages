/** Basis der Laufzeitkonfiguration, die die Seed-Pipeline je Umgebung als /config.json schreibt. */
export interface RuntimeConfig {
  apiBaseUrl: string;
}

export interface LoadRuntimeConfigOptions {
  /** Pfad der Konfigurationsdatei, Default /config.json. */
  url?: string;
  fetch?: typeof fetch;
}

/**
 * Lädt die Laufzeitkonfiguration. Weil die Pipeline die Datei erst beim Deploy schreibt,
 * reicht ein Build für alle Umgebungen.
 */
export async function loadRuntimeConfig<T extends RuntimeConfig = RuntimeConfig>(
  options: LoadRuntimeConfigOptions = {},
): Promise<T> {
  const { url = '/config.json', fetch: fetchFn = fetch } = options;

  const response = await fetchFn(url, { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`${url} konnte nicht geladen werden (HTTP ${response.status})`);
  }

  const config = (await response.json()) as Partial<T>;
  if (typeof config.apiBaseUrl !== 'string' || config.apiBaseUrl === '') {
    throw new Error(`${url} enthält keine apiBaseUrl`);
  }

  return { ...config, apiBaseUrl: config.apiBaseUrl.replace(/\/+$/, '') } as T;
}

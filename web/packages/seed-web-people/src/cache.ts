interface Entry {
  value: unknown;
  expires: number;
}

/**
 * Zwischenspeicher mit Lebensdauer im Speicher und optional im sessionStorage, damit ein Neuladen
 * der Seite nicht alles neu anfragt. Der sessionStorage endet mit dem Tab. Auch „gibt es nicht“
 * (null) wird gemerkt, etwa für Personen ohne Foto.
 */
export class TtlCache {
  private readonly memory = new Map<string, Entry>();
  private readonly prefix: string;
  private readonly storage: Storage | null;
  private readonly now: () => number;

  constructor(prefix: string, storage: Storage | null, now: () => number = Date.now) {
    this.prefix = prefix;
    this.storage = storage;
    this.now = now;
  }

  /** Wert oder undefined, wenn unbekannt oder abgelaufen. */
  get<T>(key: string): T | undefined {
    const entry = this.memory.get(key) ?? this.load(key);
    if (!entry) {
      return undefined;
    }
    if (entry.expires <= this.now()) {
      this.delete(key);
      return undefined;
    }
    return entry.value as T;
  }

  set(key: string, value: unknown, ttl: number, persist = true) {
    const entry = { value, expires: this.now() + ttl };
    this.memory.set(key, entry);
    if (persist && this.storage) {
      const text = JSON.stringify({ v: value, e: entry.expires });
      if (!this.write(this.prefix + key, text)) {
        // Voll: abgelaufene eigene Einträge entfernen und einmal neu versuchen, sonst nur im Speicher.
        this.prune();
        this.write(this.prefix + key, text);
      }
    }
  }

  delete(key: string) {
    this.memory.delete(key);
    try {
      this.storage?.removeItem(this.prefix + key);
    } catch {
      // gesperrter Speicher
    }
  }

  /** Leert Speicher und alle eigenen Einträge im sessionStorage, etwa beim Abmelden. */
  clear() {
    this.memory.clear();
    this.removeWhere(() => true);
  }

  private load(key: string): Entry | undefined {
    try {
      const text = this.storage?.getItem(this.prefix + key);
      if (!text) {
        return undefined;
      }
      const parsed = JSON.parse(text) as { v: unknown; e: number };
      const entry = { value: parsed.v, expires: parsed.e };
      this.memory.set(key, entry);
      return entry;
    } catch {
      return undefined;
    }
  }

  private write(key: string, text: string): boolean {
    try {
      this.storage?.setItem(key, text);
      return true;
    } catch {
      return false;
    }
  }

  private prune() {
    const now = this.now();
    this.removeWhere((text) => {
      try {
        return (JSON.parse(text) as { e: number }).e <= now;
      } catch {
        return true;
      }
    });
  }

  private removeWhere(predicate: (text: string) => boolean) {
    try {
      const storage = this.storage;
      if (!storage) {
        return;
      }
      for (let i = storage.length - 1; i >= 0; i--) {
        const key = storage.key(i);
        if (key?.startsWith(this.prefix) && predicate(storage.getItem(key) ?? '')) {
          storage.removeItem(key);
        }
      }
    } catch {
      // gesperrter Speicher
    }
  }
}

/** sessionStorage, falls verfügbar; in privaten Fenstern oder per Richtlinie kann er fehlen. */
export function defaultStorage(): Storage | null {
  try {
    return globalThis.sessionStorage ?? null;
  } catch {
    return null;
  }
}

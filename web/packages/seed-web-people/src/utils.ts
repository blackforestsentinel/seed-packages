import type { Person, PresenceStatus } from './types.js';

/** Farbpalette der Avatare wie in Fluent 2. */
export const AVATAR_COLORS = [
  'dark-red',
  'cranberry',
  'red',
  'pumpkin',
  'peach',
  'marigold',
  'gold',
  'brass',
  'brown',
  'forest',
  'seafoam',
  'dark-green',
  'light-teal',
  'teal',
  'steel',
  'blue',
  'royal-blue',
  'cornflower',
  'navy',
  'lavender',
  'purple',
  'grape',
  'lilac',
  'pink',
  'magenta',
  'plum',
  'beige',
  'mink',
  'platinum',
  'anchor',
] as const;

export type AvatarColor = (typeof AVATAR_COLORS)[number];

/** Farbe aus der ID (oder dem Namen), mit derselben Streuung wie Fluent; gleiche ID, gleiche Farbe. */
export function avatarColor(key: string): AvatarColor {
  let hash = 0;
  for (let i = key.length - 1; i >= 0; i--) {
    const code = key.charCodeAt(i);
    const shift = i % 8;
    hash ^= (code << shift) + (code >> (8 - shift));
  }
  return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length]!;
}

/**
 * Initialen aus dem Anzeigenamen: erster und letzter Namensteil. „Muster, Erika“ wird wie
 * „Erika Muster“ gelesen, Zusätze in Klammern und die Domain einer Mail-Adresse zählen nicht.
 */
export function initials(name: string | null | undefined): string {
  if (!name) {
    return '';
  }
  let text = name.replace(/\([^)]*\)|\[[^\]]*\]|\{[^}]*\}/g, ' ').replace(/@.*$/, '');
  const parts = text.split(',');
  if (parts.length === 2 && parts[0]!.trim() && parts[1]!.trim()) {
    text = `${parts[1]} ${parts[0]}`;
  }
  const words = text.split(/[\s._-]+/).filter((word) => /[\p{L}\p{N}]/u.test(word));
  if (words.length === 0) {
    return '';
  }
  const first = firstLetter(words[0]!);
  const last = words.length > 1 ? firstLetter(words[words.length - 1]!) : '';
  return (first + last).toLocaleUpperCase('de');
}

function firstLetter(word: string): string {
  return word.match(/[\p{L}\p{N}]/u)?.[0] ?? '';
}

/** Klassennamen zusammensetzen, leere auslassen. */
export function cx(...names: (string | false | null | undefined)[]): string {
  return names.filter(Boolean).join(' ');
}

/** Deutsche Bezeichnungen der Status, wie Teams sie zeigt. */
export const PRESENCE_LABELS: Record<PresenceStatus, string> = {
  available: 'Verfügbar',
  away: 'Abwesend',
  busy: 'Beschäftigt',
  'do-not-disturb': 'Nicht stören',
  offline: 'Offline',
  'out-of-office': 'Nicht im Büro',
  unknown: 'Status unbekannt',
};

/** Deutsche Bezeichnungen der Arten von Service Principals, wie das Entra-Portal sie nennt. */
export const SERVICE_PRINCIPAL_TYPE_LABELS: Record<string, string> = {
  ManagedIdentity: 'Verwaltete Identität',
  Application: 'Anwendung',
  Legacy: 'Anwendung',
  SocialIdp: 'Identitätsanbieter',
};

/** Ob der Eintrag ein Service Principal ist; ohne kind gilt user. */
export function isServicePrincipal(person: Pick<Person, 'kind'> | null | undefined): boolean {
  return person?.kind === 'servicePrincipal';
}

/** Bezeichnung eines Service Principals für die Anzeige, etwa „Verwaltete Identität“. */
export function servicePrincipalLabel(type: string | null | undefined): string {
  return (type && SERVICE_PRINCIPAL_TYPE_LABELS[type]) || 'Anwendung';
}

/** Avatar-Größen, für die styles.css feste Regeln hat; andere setzen --seed-avatar-size direkt. */
export const AVATAR_SIZES = [16, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 96] as const;

/** Größe des Status-Punkts zur Avatar-Größe, wie in Fluent. */
export function presenceSizeFor(avatarSize: number): number {
  if (avatarSize >= 96) {
    return 28;
  }
  if (avatarSize >= 64) {
    return 20;
  }
  if (avatarSize >= 56) {
    return 16;
  }
  if (avatarSize >= 40) {
    return 12;
  }
  return avatarSize >= 28 ? 10 : 6;
}

/** Link zum Teams-Chat mit der Person. */
export function teamsChatUrl(address: string): string {
  return `https://teams.microsoft.com/l/chat/0/0?users=${encodeURIComponent(address)}`;
}

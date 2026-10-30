import { useState, type CSSProperties } from 'react';
import { resolvePrincipalKind, usePerson, usePhoto, usePresence } from '../hooks.js';
import { useDirectory } from '../provider.js';
import type { PresenceStatus, PrincipalKind, ServicePrincipalType } from '../types.js';
import { AVATAR_SIZES, avatarColor, cx, initials, PRESENCE_LABELS, presenceSizeFor, servicePrincipalLabel, type AvatarColor } from '../utils.js';
import { AppIcon, KeyIcon, PersonIcon } from './icons.js';
import { PresenceBadge } from './PresenceBadge.js';

export interface AvatarProps {
  /** Objekt-ID der Person; lädt Foto und, wenn name fehlt, den Namen. */
  id?: string | null;
  /** Anzeigename; ohne ihn kommt er aus dem Verzeichnis. */
  name?: string | null;
  /** Eigenes Bild (URL oder data:-URL) statt des Fotos aus Graph. */
  image?: string | null;
  /**
   * user oder servicePrincipal; ohne Angabe aus dem Verzeichnis. Service Principals zeigen ein Symbol
   * statt Foto und Initialen und keinen Status.
   */
  kind?: PrincipalKind;
  /** Für Service Principals: wählt das Symbol (ManagedIdentity: Schlüssel, sonst App). */
  servicePrincipalType?: ServicePrincipalType | null;
  /** Größe in Pixeln, 16 bis 96; Default 32. */
  size?: number;
  /** circular (Default für Personen) oder square mit abgerundeten Ecken (Default für Anwendungen). */
  shape?: 'circular' | 'square';
  /** true lädt den Teams-Status zur ID; ein Status setzt ihn fest. */
  presence?: boolean | PresenceStatus;
  /** false lädt kein Foto, nur Initialen. */
  photo?: boolean;
  /** colorful (Default): Farbe aus der ID; neutral, brand oder eine feste Farbe der Palette. */
  color?: 'colorful' | 'neutral' | 'brand' | AvatarColor;
  /** Nur Schmuck, etwa wenn der Name daneben steht; dann ohne eigene Beschriftung. */
  decorative?: boolean;
  /** Beschriftung für Screenreader, Default Name und Status. */
  label?: string;
  className?: string;
  classNames?: { image?: string; initials?: string; icon?: string; badge?: string };
}

/**
 * Profilbild; ohne Bild Initialen auf einer Farbe, die aus der ID berechnet wird. Während das Foto
 * lädt, zeigt es einen Platzhalter. Anwendungen und verwaltete Identitäten bekommen ein Symbol.
 */
export function Avatar({
  id,
  name,
  image,
  kind,
  servicePrincipalType,
  size = 32,
  shape,
  presence,
  photo = true,
  color = 'colorful',
  decorative = false,
  label,
  className,
  classNames,
}: AvatarProps) {
  // Kennt das Verzeichnis Service Principals, zeigt erst das Profil, ob die ID eine Person ist. Das
  // Foto lädt trotzdem gleich mit (im selben $batch), der Status erst für bekannte Personen.
  const directory = useDirectory();
  const resolving = !kind && !!id && directory.servicePrincipals === true;
  const person = usePerson(name && !resolving ? null : id);
  const resolvedKind = resolvePrincipalKind(kind, person, resolving);
  const app = resolvedKind === 'servicePrincipal';
  const principalType = servicePrincipalType ?? person.person?.servicePrincipalType ?? null;
  const loadPhoto = photo && !image && !!id && !app;
  const picture = usePhoto(loadPhoto ? id : null, size);
  const live = usePresence(presence === true && resolvedKind === 'user' ? id : null);
  const [failed, setFailed] = useState<string | null>(null);

  const displayName = name || person.person?.displayName || null;
  const src = image || (app ? null : picture.url);
  const text = initials(displayName);
  const status = app ? null : typeof presence === 'string' ? presence : (live.presence?.status ?? null);

  let state: 'image' | 'loading' | 'initials' | 'icon';
  if (src && src !== failed) {
    state = 'image';
  } else if (app) {
    state = 'icon';
  } else if ((loadPhoto && picture.status === 'loading') || ((!name || resolving) && person.status === 'loading')) {
    state = 'loading';
  } else {
    state = text ? 'initials' : 'icon';
  }

  const colorKey = id || displayName;
  const colorName = color === 'colorful' ? (colorKey ? avatarColor(colorKey) : 'neutral') : color;
  const kindLabel = app ? servicePrincipalLabel(principalType) : null;
  const accessibleName =
    label ?? [displayName ?? (app ? null : 'Person'), kindLabel, status ? PRESENCE_LABELS[status] : null].filter(Boolean).join(', ');
  const Icon = !app ? PersonIcon : principalType === 'ManagedIdentity' ? KeyIcon : AppIcon;

  return (
    <span
      className={cx('seed-avatar', className)}
      {...avatarSizeAttributes(size)}
      data-shape={shape ?? (app ? 'square' : 'circular')}
      data-state={state}
      data-color={colorName}
      data-kind={app ? 'servicePrincipal' : undefined}
      data-presence={status ?? undefined}
      {...(decorative ? { 'aria-hidden': true } : { role: 'img', 'aria-label': accessibleName })}
    >
      {state === 'image' && (
        <img className={cx('seed-avatar__image', classNames?.image)} src={src!} alt="" onError={() => setFailed(src)} />
      )}
      {state === 'initials' && (
        <span className={cx('seed-avatar__initials', classNames?.initials)} aria-hidden="true">
          {text}
        </span>
      )}
      {state === 'icon' && <Icon className={cx('seed-avatar__icon', classNames?.icon)} />}
      {status && <PresenceBadge status={status} size={presenceSizeFor(size)} decorative className={cx('seed-avatar__badge', classNames?.badge)} />}
    </span>
  );
}

/**
 * Größe als data-size für die Größen mit festen Regeln in styles.css, sonst als CSS-Variablen
 * per CSSOM (erlaubt unter style-src 'self').
 */
export function avatarSizeAttributes(size: number): { 'data-size'?: number; style?: CSSProperties } {
  if ((AVATAR_SIZES as readonly number[]).includes(size)) {
    return { 'data-size': size };
  }
  return {
    style: {
      '--seed-avatar-size': `${size}px`,
      '--seed-avatar-font-size': `${Math.max(10, Math.round(size * 0.36))}px`,
      '--seed-avatar-badge-size': `${presenceSizeFor(size)}px`,
    } as CSSProperties,
  };
}

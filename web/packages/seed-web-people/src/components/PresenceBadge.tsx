import type { CSSProperties } from 'react';
import type { PresenceStatus } from '../types.js';
import { cx, PRESENCE_LABELS } from '../utils.js';

export interface PresenceBadgeProps {
  status: PresenceStatus;
  /** Durchmesser in Pixeln; Fluent nutzt 6, 10, 12, 16, 20 und 28. Default 10. */
  size?: number;
  /** Text für Screenreader, Default die deutsche Bezeichnung des Status. */
  label?: string;
  /** Nur Schmuck, etwa wenn der Status daneben als Text steht. */
  decorative?: boolean;
  className?: string;
}

const SIZES = [6, 10, 12, 16, 20, 28];

/**
 * Teams-Status als Punkt mit Symbol wie in Fluent: grün mit Haken, rot, rot mit Strich, gelb mit Uhr,
 * grauer Ring mit Kreuz, lila Ring mit Pfeil (nicht im Büro).
 */
export function PresenceBadge({ status, size = 10, label, decorative = false, className }: PresenceBadgeProps) {
  const known = SIZES.includes(size);
  return (
    <span
      className={cx('seed-presence', className)}
      data-status={status}
      data-size={known ? size : undefined}
      style={known ? undefined : ({ '--seed-presence-size': `${size}px` } as CSSProperties)}
      {...(decorative ? { 'aria-hidden': true } : { role: 'img', 'aria-label': label ?? PRESENCE_LABELS[status] })}
    >
      <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
        {glyph(status)}
      </svg>
    </span>
  );
}

function glyph(status: PresenceStatus) {
  switch (status) {
    case 'available':
      return (
        <>
          <circle className="seed-presence__fill" cx="8" cy="8" r="8" />
          <path className="seed-presence__glyph" d="m4.75 8.25 2.2 2.2 4.3-4.6" />
        </>
      );
    case 'busy':
      return <circle className="seed-presence__fill" cx="8" cy="8" r="8" />;
    case 'do-not-disturb':
      return (
        <>
          <circle className="seed-presence__fill" cx="8" cy="8" r="8" />
          <path className="seed-presence__glyph" d="M4.75 8h6.5" />
        </>
      );
    case 'away':
      return (
        <>
          <circle className="seed-presence__fill" cx="8" cy="8" r="8" />
          <path className="seed-presence__glyph" d="M8 4.5V8l2.25 1.75" />
        </>
      );
    case 'offline':
      return (
        <>
          <circle className="seed-presence__ring" cx="8" cy="8" r="7" />
          <path className="seed-presence__mark" d="m5.75 5.75 4.5 4.5m0-4.5-4.5 4.5" />
        </>
      );
    case 'out-of-office':
      return (
        <>
          <circle className="seed-presence__ring" cx="8" cy="8" r="7" />
          <path className="seed-presence__mark" d="M11 8H5.5m2.25-2.5L5.25 8l2.5 2.5" />
        </>
      );
    default:
      return <circle className="seed-presence__ring" cx="8" cy="8" r="7" />;
  }
}

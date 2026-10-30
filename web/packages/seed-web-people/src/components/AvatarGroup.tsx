import { useDirectory } from '../provider.js';
import type { PrincipalKind } from '../types.js';
import { cx } from '../utils.js';
import { Avatar, avatarSizeAttributes } from './Avatar.js';

export type AvatarGroupPerson = string | { id?: string | null; name?: string | null; kind?: PrincipalKind };

export interface AvatarGroupProps {
  /** Objekt-IDs oder Personen mit ID und Namen. */
  people: readonly AvatarGroupPerson[];
  /** Höchstens so viele Avatare, danach „+n“; Default 4. */
  max?: number;
  /** Default 32. */
  size?: number;
  shape?: 'circular' | 'square';
  /** Beschriftung der Gruppe, Default „n Personen“. */
  label?: string;
  className?: string;
  classNames?: { avatar?: string; overflow?: string };
}

/** Mehrere Avatare überlappend, ab max mit „+n“; der Titel von „+n“ nennt die übrigen Namen. */
export function AvatarGroup({ people, max = 4, size = 32, shape = 'circular', label, className, classNames }: AvatarGroupProps) {
  const directory = useDirectory();
  const entries = people.map((entry) => (typeof entry === 'string' ? { id: entry, name: null, kind: undefined } : entry));
  // Bei Überlauf zählt „+n“ als eigener Platz.
  const visible = entries.length > max ? entries.slice(0, Math.max(max - 1, 1)) : entries;
  const hidden = entries.slice(visible.length);
  const hiddenNames = hidden
    .map((entry) => entry.name || (entry.id ? directory.peekPerson(entry.id)?.displayName : null))
    .filter((name): name is string => !!name);
  const overflowLabel = `${hidden.length} weitere${hiddenNames.length > 0 ? `: ${hiddenNames.join(', ')}` : ''}`;

  return (
    <span
      className={cx('seed-avatar-group', className)}
      role="group"
      aria-label={label ?? `${entries.length} ${entries.length === 1 ? 'Person' : 'Personen'}`}
    >
      {visible.map((entry, index) => (
        <Avatar
          key={entry.id ?? `${entry.name}-${index}`}
          id={entry.id}
          name={entry.name}
          kind={entry.kind}
          size={size}
          shape={shape}
          className={cx('seed-avatar-group__item', classNames?.avatar)}
        />
      ))}
      {hidden.length > 0 && (
        <span
          className={cx('seed-avatar seed-avatar-group__overflow', classNames?.overflow)}
          {...avatarSizeAttributes(size)}
          data-shape={shape}
          data-state="initials"
          data-color="neutral"
          role="img"
          aria-label={overflowLabel}
          title={hiddenNames.join(', ') || undefined}
        >
          <span className="seed-avatar__initials" aria-hidden="true">
            +{hidden.length}
          </span>
        </span>
      )}
    </span>
  );
}

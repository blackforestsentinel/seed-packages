import { useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { usePerson, usePresence } from '../hooks.js';
import type { Person } from '../types.js';
import { cx, PRESENCE_LABELS } from '../utils.js';
import { Avatar } from './Avatar.js';
import { PersonCard } from './PersonCard.js';

export interface PersonLabelProps {
  /** Objekt-ID; lädt Name, Foto und auf Wunsch den Status. */
  id?: string | null;
  /** Anzeigename; ohne ihn kommt er aus dem Verzeichnis. Ohne Verzeichnis zeigt das Label genau diesen. */
  name?: string | null;
  /** Bekannte Daten, etwa aus dem PeoplePicker. */
  person?: Partial<Person> | null;
  /** Zweite Zeile, z. B. ein Zeitpunkt oder die Rolle. */
  secondary?: ReactNode;
  /** Zweite Zeile aus dem Profil, wenn secondary fehlt. */
  secondaryField?: 'mail' | 'jobTitle' | 'department';
  /** Avatar-Größe; Default 24, mit zweiter Zeile 32. */
  size?: number;
  /** Teams-Status am Avatar. */
  presence?: boolean;
  /** PersonCard beim Überfahren und per Tastatur. */
  card?: boolean;
  className?: string;
  classNames?: { avatar?: string; text?: string; name?: string; secondary?: string; card?: string };
}

/** Avatar, Name und optional eine zweite Zeile, etwa für „Freigegeben von“ in Tabellen. */
export function PersonLabel({
  id,
  name,
  person,
  secondary,
  secondaryField,
  size,
  presence = false,
  card = false,
  className,
  classNames,
}: PersonLabelProps) {
  const needsProfile = !name || (secondary === undefined && !!secondaryField && !person?.[secondaryField]);
  const loaded = usePerson(needsProfile ? id : null);
  const live = usePresence(presence ? id : null);
  const data: Partial<Person> = { ...person, ...loaded.person };
  const displayName = name || person?.displayName || loaded.person?.displayName || (loaded.status === 'loading' ? '' : 'Unbekannte Person');
  const second = secondary ?? (secondaryField ? data[secondaryField] : null);
  const status = live.presence?.status ?? null;

  const content = (
    <>
      <Avatar
        id={id}
        name={displayName || null}
        size={size ?? (second ? 32 : 24)}
        presence={status ?? false}
        decorative
        className={cx('seed-person-label__avatar', classNames?.avatar)}
      />
      <span className={cx('seed-person-label__text', classNames?.text)}>
        <span className={cx('seed-person-label__name', classNames?.name)}>
          {displayName}
          {status && <span className="seed-visually-hidden">, {PRESENCE_LABELS[status]}</span>}
        </span>
        {second ? <span className={cx('seed-person-label__secondary', classNames?.secondary)}>{second}</span> : null}
      </span>
    </>
  );

  const state = loaded.status === 'loading' ? 'loading' : 'ready';
  if (!card || (!id && !person)) {
    return (
      <span className={cx('seed-person-label', className)} data-state={state}>
        {content}
      </span>
    );
  }
  return (
    <HoverCard className={className} state={state} cardClassName={classNames?.card} card={<PersonCard id={id} person={person} name={name} presence={presence} />}>
      {content}
    </HoverCard>
  );
}

interface HoverCardProps {
  className?: string;
  cardClassName?: string;
  state: string;
  card: ReactNode;
  children: ReactNode;
}

const CARD_WIDTH = 320;

/**
 * Karte unter dem Label beim Überfahren (mit kurzer Verzögerung) und bei Tastaturfokus; Escape
 * schließt sie. Sie steht im DOM direkt hinter dem Auslöser, damit Tab in ihre Links führt, und
 * liegt per position: fixed über allem, damit Tabellen mit overflow sie nicht abschneiden.
 */
function HoverCard({ className, cardClassName, state, card, children }: HoverCardProps) {
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const cardId = useId();

  function schedule(next: boolean, delay: number) {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setOpen(next), delay);
  }

  useLayoutEffect(() => {
    if (!open || !triggerRef.current) {
      return;
    }
    const rect = triggerRef.current.getBoundingClientRect();
    const width = typeof window === 'undefined' ? CARD_WIDTH : window.innerWidth;
    setPosition({ top: rect.bottom + 4, left: Math.max(8, Math.min(rect.left, width - CARD_WIDTH - 8)) });
  }, [open]);

  useEffect(() => {
    if (!open) {
      return;
    }
    const close = () => setOpen(false);
    window.addEventListener('scroll', close, true);
    window.addEventListener('resize', close);
    return () => {
      window.removeEventListener('scroll', close, true);
      window.removeEventListener('resize', close);
    };
  }, [open]);

  useEffect(() => () => clearTimeout(timer.current), []);

  return (
    <span
      className="seed-person-hover"
      onMouseEnter={() => schedule(true, 400)}
      onMouseLeave={() => schedule(false, 250)}
      onFocus={() => schedule(true, 0)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          schedule(false, 0);
        }
      }}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && open) {
          event.stopPropagation();
          clearTimeout(timer.current);
          setOpen(false);
          triggerRef.current?.focus();
        }
      }}
    >
      <button
        ref={triggerRef}
        type="button"
        className={cx('seed-person-label', className)}
        data-state={state}
        data-interactive=""
        aria-expanded={open}
        aria-controls={open ? cardId : undefined}
        onClick={() => {
          clearTimeout(timer.current);
          setOpen((current) => !current);
        }}
      >
        {children}
      </button>
      {open && (
        <span
          id={cardId}
          className={cx('seed-person-hover__card', cardClassName)}
          style={position ? ({ top: `${position.top}px`, left: `${position.left}px` } as CSSProperties) : undefined}
        >
          {card}
        </span>
      )}
    </span>
  );
}

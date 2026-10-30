import { useId, type ReactNode } from 'react';
import { useCurrentUser } from '../hooks.js';
import { useSignOut } from '../provider.js';
import { cx } from '../utils.js';
import { LockIcon } from './icons.js';
import { PersonLabel } from './PersonLabel.js';

export interface AccessDeniedContact {
  name?: string;
  email?: string;
  /** Link, etwa ein Formular oder eine Teams-Seite. */
  url?: string;
}

export interface AccessDeniedProps {
  /** Name der App im Text. */
  appName?: string;
  /**
   * no-role: angemeldet, aber ohne App-Rolle; missing-capability: Rolle da, aber nicht für diesen
   * Bereich. Default nach den Rollen der Person.
   */
  reason?: 'no-role' | 'missing-capability';
  /** Fehlende Capability, wird bei missing-capability genannt. */
  capability?: string;
  title?: ReactNode;
  /** Eigener Text statt der Erklärung. */
  message?: ReactNode;
  /** Wen man fragen kann. */
  contact?: AccessDeniedContact;
  /** „Angemeldet als …“ zeigen, Default true. */
  showUser?: boolean;
  /** Mit anderem Konto anmelden; Default Abmelden aus DirectoryProvider, null blendet es aus. */
  onSignOut?: (() => void | Promise<void>) | null;
  /** Eigene Knöpfe, etwa „Zur Startseite“. */
  actions?: ReactNode;
  /** Überschriftenebene, Default 1. */
  headingLevel?: 1 | 2 | 3;
  labels?: { signOut?: string; signedInAs?: string; contact?: string };
  className?: string;
  classNames?: {
    icon?: string;
    title?: string;
    message?: string;
    hint?: string;
    contact?: string;
    user?: string;
    actions?: string;
    signOut?: string;
  };
}

/**
 * Seite für Personen, die angemeldet sind, aber keine Rolle oder nicht die nötige Capability haben:
 * was fehlt, wen man fragen kann und wie man das Konto wechselt.
 */
export function AccessDenied({
  appName,
  reason,
  capability,
  title,
  message,
  contact,
  showUser = true,
  onSignOut,
  actions,
  headingLevel = 1,
  labels,
  className,
  classNames,
}: AccessDeniedProps) {
  const { user } = useCurrentUser();
  const defaultSignOut = useSignOut();
  const signOut = onSignOut === undefined ? defaultSignOut : onSignOut;
  const titleId = useId();
  const kind = reason ?? (user && user.roles.length > 0 ? 'missing-capability' : 'no-role');
  const app = appName ? `„${appName}“` : 'dieser Anwendung';
  const Heading = `h${headingLevel}` as const;

  const text =
    message ??
    (kind === 'no-role'
      ? `Du bist angemeldet, hast in ${app} aber noch keine Rolle. Ohne Rolle sind keine Inhalte freigegeben.`
      : `Für diesen Bereich von ${app} fehlt dir eine Berechtigung.`);
  const hint =
    message === undefined
      ? kind === 'no-role'
        ? 'Rollen vergibt die verantwortliche Person in Entra ID. Nach der Zuweisung meldest du dich einmal ab und wieder an.'
        : 'Frag die verantwortliche Person, ob deine Rolle erweitert werden kann.'
      : null;

  return (
    <section className={cx('seed-access-denied', className)} data-reason={kind} aria-labelledby={titleId}>
      <span className={cx('seed-access-denied__icon', classNames?.icon)} aria-hidden="true">
        <LockIcon />
      </span>
      <Heading id={titleId} className={cx('seed-access-denied__title', classNames?.title)}>
        {title ?? 'Kein Zugriff'}
      </Heading>
      <p className={cx('seed-access-denied__message', classNames?.message)}>{text}</p>
      {hint && <p className={cx('seed-access-denied__hint', classNames?.hint)}>{hint}</p>}
      {kind === 'missing-capability' && capability && (
        <p className={cx('seed-access-denied__hint', classNames?.hint)}>
          Benötigt: <code className="seed-access-denied__code">{capability}</code>
        </p>
      )}
      {contact && (contact.name || contact.email || contact.url) && (
        <p className={cx('seed-access-denied__contact', classNames?.contact)}>
          {labels?.contact ?? 'Ansprechperson'}: <ContactLink contact={contact} />
        </p>
      )}
      {showUser && user && (
        <div className={cx('seed-access-denied__user', classNames?.user)}>
          <span className="seed-access-denied__user-label">{labels?.signedInAs ?? 'Angemeldet als'}</span>
          <PersonLabel id={user.id} name={user.displayName} person={{ kind: 'user' }} secondary={user.username ?? undefined} />
        </div>
      )}
      {(signOut || actions) && (
        <div className={cx('seed-access-denied__actions', classNames?.actions)}>
          {actions}
          {signOut && (
            <button type="button" className={cx('seed-access-denied__sign-out', classNames?.signOut)} onClick={() => void signOut()}>
              {labels?.signOut ?? 'Mit anderem Konto anmelden'}
            </button>
          )}
        </div>
      )}
    </section>
  );
}

function ContactLink({ contact }: { contact: AccessDeniedContact }) {
  const text = contact.name || contact.email || contact.url;
  if (contact.email) {
    return <a href={`mailto:${contact.email}`}>{text}</a>;
  }
  if (contact.url) {
    return (
      <a href={contact.url} target="_blank" rel="noopener noreferrer">
        {text}
      </a>
    );
  }
  return <>{text}</>;
}

import type { ReactNode } from 'react';
import { resolvePrincipalKind, usePerson, usePresence } from '../hooks.js';
import { useDirectory } from '../provider.js';
import type { Person } from '../types.js';
import { cx, PRESENCE_LABELS, servicePrincipalLabel, teamsChatUrl } from '../utils.js';
import { Avatar } from './Avatar.js';
import { ChatIcon, MailIcon } from './icons.js';
import { PresenceBadge } from './PresenceBadge.js';

export interface PersonCardProps {
  /** Objekt-ID; lädt Profil, Foto und Status. */
  id?: string | null;
  /** Bekannte Daten, etwa aus dem PeoplePicker; das Profil aus dem Verzeichnis ergänzt sie. */
  person?: Partial<Person> | null;
  /** Anzeigename, hat Vorrang. */
  name?: string | null;
  /** Teams-Status zeigen; nur für Personen. */
  presence?: boolean;
  /** Link zum Teams-Chat, Default true; nur für Personen. */
  chat?: boolean;
  /** Avatar-Größe, Default 64. */
  size?: number;
  /** Eigene Knöpfe unter den Angaben. */
  actions?: ReactNode;
  className?: string;
  classNames?: {
    avatar?: string;
    body?: string;
    name?: string;
    detail?: string;
    presence?: string;
    links?: string;
    link?: string;
    actions?: string;
  };
}

/**
 * Karte mit größerem Bild, Name, Position, Abteilung, Mail und Link zum Teams-Chat; für Anwendungen
 * und verwaltete Identitäten Art und App-ID. Aus span-Elementen gebaut, damit sie auch in Fließtext
 * und Tabellenzellen stehen darf (Hovercard von PersonLabel).
 */
export function PersonCard({ id, person, name, presence = false, chat = true, size = 64, actions, className, classNames }: PersonCardProps) {
  const loaded = usePerson(id);
  const directory = useDirectory();
  const resolving = !person?.kind && !!id && directory.servicePrincipals === true;
  const kind = resolvePrincipalKind(person?.kind, loaded, resolving);
  const app = kind === 'servicePrincipal';
  const live = usePresence(presence && kind === 'user' ? id : null);
  const data: Partial<Person> = { ...person, ...withoutEmpty(loaded.person) };
  const displayName = name || data.displayName || (loaded.status === 'loading' ? '' : 'Unbekannte Person');
  const status = live.presence?.status ?? null;
  const address = app ? null : data.mail || data.userPrincipalName || null;
  const chatAddress = app ? null : data.userPrincipalName || data.mail || null;

  return (
    <span
      className={cx('seed-person-card', className)}
      role="group"
      aria-label={displayName || (app ? servicePrincipalLabel(data.servicePrincipalType) : 'Person')}
      data-state={loaded.status === 'loading' ? 'loading' : 'ready'}
      data-kind={app ? 'servicePrincipal' : undefined}
    >
      <Avatar
        id={id}
        name={displayName || null}
        kind={kind}
        servicePrincipalType={data.servicePrincipalType}
        size={size}
        presence={status ?? false}
        decorative
        className={cx('seed-person-card__avatar', classNames?.avatar)}
      />
      <span className={cx('seed-person-card__body', classNames?.body)}>
        <span className={cx('seed-person-card__name', classNames?.name)}>{displayName}</span>
        {app && (
          <span className={cx('seed-person-card__detail', classNames?.detail)} data-field="servicePrincipalType">
            {servicePrincipalLabel(data.servicePrincipalType)}
          </span>
        )}
        {app && data.appId && (
          <span className={cx('seed-person-card__detail', classNames?.detail)} data-field="appId">
            App-ID {data.appId}
          </span>
        )}
        {data.jobTitle && (
          <span className={cx('seed-person-card__detail', classNames?.detail)} data-field="jobTitle">
            {data.jobTitle}
          </span>
        )}
        {data.department && (
          <span className={cx('seed-person-card__detail', classNames?.detail)} data-field="department">
            {data.department}
          </span>
        )}
        {status && (
          <span className={cx('seed-person-card__presence', classNames?.presence)}>
            <PresenceBadge status={status} size={12} decorative />
            {PRESENCE_LABELS[status]}
          </span>
        )}
        {(address || (chat && chatAddress)) && (
          <span className={cx('seed-person-card__links', classNames?.links)}>
            {address && (
              <a className={cx('seed-person-card__link', classNames?.link)} href={`mailto:${address}`}>
                <MailIcon className="seed-person-card__icon" />
                <span className="seed-person-card__link-text">{address}</span>
              </a>
            )}
            {chat && chatAddress && (
              <a className={cx('seed-person-card__link', classNames?.link)} href={teamsChatUrl(chatAddress)} target="_blank" rel="noopener noreferrer">
                <ChatIcon className="seed-person-card__icon" />
                <span className="seed-person-card__link-text">Chat in Teams</span>
              </a>
            )}
          </span>
        )}
        {actions && <span className={cx('seed-person-card__actions', classNames?.actions)}>{actions}</span>}
      </span>
    </span>
  );
}

function withoutEmpty(person: Person | null): Partial<Person> {
  if (!person) {
    return {};
  }
  return Object.fromEntries(Object.entries(person).filter(([, value]) => value !== null && value !== undefined && value !== '')) as Partial<Person>;
}

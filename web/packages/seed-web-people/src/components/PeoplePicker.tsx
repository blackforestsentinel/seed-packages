import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { matchesSearch, principalTypesOf } from '../directory.js';
import { useAppRoleMembers, usePeopleSearch } from '../hooks.js';
import { useDirectory } from '../provider.js';
import type { Person, PrincipalKind, ServicePrincipalType } from '../types.js';
import { cx, servicePrincipalLabel } from '../utils.js';
import { Avatar } from './Avatar.js';
import { DismissIcon } from './icons.js';

export interface PeoplePickerLabels {
  /** Beschriftung des Suchfelds für Screenreader, wenn kein aria-labelledby gesetzt ist. */
  input: string;
  placeholder: string;
  searching: string;
  noResults: string;
  error: string;
  notConfigured: string;
  roleFilterNotConfigured: string;
  roleFilterFailed: string;
  servicePrincipalsNotConfigured: string;
  tooShort: (minChars: number) => string;
  results: (count: number) => string;
  remove: (name: string) => string;
}

const DEFAULT_LABELS: PeoplePickerLabels = {
  input: 'Person suchen',
  placeholder: 'Name oder Mail-Adresse',
  searching: 'Suche …',
  noResults: 'Keine Treffer',
  error: 'Die Suche ist fehlgeschlagen.',
  notConfigured: 'Die Personensuche ist nicht eingerichtet.',
  roleFilterNotConfigured: 'Der Filter nach App-Rolle ist nicht eingerichtet (auth.directory.roleFilter in config.json).',
  roleFilterFailed: 'Die Personen mit App-Rolle ließen sich nicht laden.',
  servicePrincipalsNotConfigured: 'Die Suche nach Anwendungen ist nicht eingerichtet (auth.directory.servicePrincipals in config.json).',
  tooShort: (minChars) => `Mindestens ${minChars} Zeichen eingeben`,
  results: (count) => (count === 1 ? '1 Vorschlag' : `${count} Vorschläge`),
  remove: (name) => `${name} entfernen`,
};

// Andere Vorgaben für Suchfeld und Platzhalter, wenn der Picker auch Anwendungen findet.
const MIXED_LABELS: Partial<PeoplePickerLabels> = { input: 'Person oder Anwendung suchen', placeholder: 'Name, Mail-Adresse oder Anwendung' };
const APP_LABELS: Partial<PeoplePickerLabels> = { input: 'Anwendung suchen', placeholder: 'Name der Anwendung' };

export interface PeoplePickerOptionState {
  active: boolean;
}

export interface PeoplePickerProps {
  /** Auswahl, gesteuert; zusammen mit onChange. */
  value?: Person[];
  /** Anfangsauswahl, ungesteuert. */
  defaultValue?: Person[];
  onChange?: (people: Person[]) => void;
  /** Mehrere Personen als Chips; sonst ersetzt eine neue Auswahl die alte. */
  multiple?: boolean;
  /** ID des Suchfelds, etwa für <label htmlFor>. */
  id?: string;
  'aria-labelledby'?: string;
  placeholder?: string;
  disabled?: boolean;
  /** Ab so vielen Zeichen wird gesucht, Default 2. */
  minChars?: number;
  /** Default 250 ms. */
  debounceMs?: number;
  /** Höchstzahl der Vorschläge, Default 8. */
  maxSuggestions?: number;
  /** Eigener Filter für Vorschläge, etwa nur eine Abteilung. */
  filter?: (person: Person) => boolean;
  /** Eigene Darstellung eines Vorschlags. */
  renderOption?: (person: Person, state: PeoplePickerOptionState) => ReactNode;
  /**
   * Nur Personen mit App-Rolle der API zeigen (Gruppen aufgelöst). Braucht auth.directory.roleFilter;
   * ohne ihn zeigt der Picker niemanden und meldet den Fehler.
   */
  onlyWithAppRole?: boolean;
  /** Mit onlyWithAppRole: nur diese Rollen (Werte aus project.yaml), sonst jede. */
  roles?: string[];
  /**
   * Was der Picker findet: Default ['user']; mit 'servicePrincipal' auch Anwendungen und verwaltete
   * Identitäten. Braucht auth.directory.servicePrincipals; ohne zeigt der Picker einen Fehler.
   */
  principalTypes?: PrincipalKind[];
  /** Nur diese Arten von Service Principals, etwa ['ManagedIdentity']; Default alle. */
  servicePrincipalTypes?: ServicePrincipalType[];
  labels?: Partial<PeoplePickerLabels>;
  className?: string;
  classNames?: {
    control?: string;
    chip?: string;
    chipRemove?: string;
    input?: string;
    popup?: string;
    listbox?: string;
    option?: string;
    message?: string;
  };
}

type Phase =
  | 'idle'
  | 'too-short'
  | 'loading'
  | 'ready'
  | 'empty'
  | 'error'
  | 'not-configured'
  | 'role-filter-missing'
  | 'service-principals-missing';

/**
 * Personenauswahl nach dem ARIA-Muster „Combobox“: Suche ab zwei Zeichen mit Verzögerung, Vorschläge
 * zuerst aus den relevanten Personen, dann aus dem Verzeichnis, Pfeiltasten, Enter, Escape und
 * Rücktaste für den letzten Chip.
 */
export function PeoplePicker({
  value,
  defaultValue,
  onChange,
  multiple = false,
  id,
  'aria-labelledby': labelledBy,
  placeholder,
  disabled = false,
  minChars = 2,
  debounceMs = 250,
  maxSuggestions = 8,
  filter,
  renderOption,
  onlyWithAppRole = false,
  roles,
  principalTypes,
  servicePrincipalTypes,
  labels: labelOverrides,
  className,
  classNames,
}: PeoplePickerProps) {
  const types = principalTypesOf({ principalTypes });
  const wantsApps = types.includes('servicePrincipal');
  const wantsUsers = types.includes('user');
  const labels = { ...DEFAULT_LABELS, ...(wantsApps ? (wantsUsers ? MIXED_LABELS : APP_LABELS) : {}), ...labelOverrides };
  const directory = useDirectory();
  const [internal, setInternal] = useState<Person[]>(defaultValue ?? []);
  const selected = value ?? internal;
  const [text, setText] = useState('');
  const [open, setOpen] = useState(false);
  // Aktiver Vorschlag als Personen-ID: bleibt beim Nachladen der Liste stehen, sonst der erste.
  const [activeId, setActiveId] = useState<string | null>(null);
  const baseId = useId();
  const inputId = id ?? `${baseId}-input`;
  const listboxId = `${baseId}-listbox`;
  const statusId = `${baseId}-status`;
  const inputRef = useRef<HTMLInputElement>(null);

  const roleFilterMissing = onlyWithAppRole && !directory.roleFilter;
  useEffect(() => {
    if (roleFilterMissing) {
      console.error(
        'PeoplePicker: onlyWithAppRole braucht auth.directory.roleFilter: true und apiClientId in config.json (project.yaml: auth.directory mit roleFilter). Ohne Filter zeigt der Picker niemanden.',
      );
    }
  }, [roleFilterMissing]);
  const appsMissing = wantsApps && !directory.servicePrincipals;
  useEffect(() => {
    if (appsMissing) {
      console.error(
        "PeoplePicker: principalTypes mit 'servicePrincipal' braucht auth.directory.servicePrincipals: true in config.json (project.yaml: auth.directory mit servicePrincipals). Ohne zeigt der Picker niemanden.",
      );
    }
  }, [appsMissing]);
  const blocked = roleFilterMissing || appsMissing;

  const search = usePeopleSearch(text, {
    minChars,
    debounceMs,
    top: onlyWithAppRole ? 25 : Math.max(10, maxSuggestions),
    enabled: !blocked,
    principalTypes: types,
    servicePrincipalTypes,
  });
  const members = useAppRoleMembers(roles, { enabled: onlyWithAppRole && !blocked, principalTypes: types });

  useEffect(() => {
    if (members.status === 'error') {
      console.error('PeoplePicker: Personen mit App-Rolle ließen sich nicht laden.', members.error);
    }
  }, [members.status, members.error]);

  const suggestions = useMemo(() => {
    let candidates = search.people;
    if (onlyWithAppRole) {
      if (members.status !== 'ready') {
        return [];
      }
      // Treffer der Suche mit Rolle zuerst, dann passende Personen aus der Rollenliste, die die
      // Suche (begrenzt auf die ersten Treffer) nicht gebracht hat.
      const byId = new Map<string, Person>();
      for (const person of candidates) {
        if (members.ids.has(person.id)) {
          byId.set(person.id, person);
        }
      }
      for (const person of members.members) {
        if (!byId.has(person.id) && search.query && matchesSearch(person, search.query)) {
          byId.set(person.id, person);
        }
      }
      candidates = [...byId.values()];
    }
    const selectedIds = new Set(selected.map((person) => person.id));
    return candidates.filter((person) => !selectedIds.has(person.id) && (!filter || filter(person))).slice(0, maxSuggestions);
  }, [search.people, search.query, onlyWithAppRole, members.status, members.ids, members.members, selected, filter, maxSuggestions]);

  const activeIndex = suggestions.length === 0 ? -1 : Math.max(0, suggestions.findIndex((person) => person.id === activeId));

  const trimmed = text.trim();
  let phase: Phase;
  if (roleFilterMissing) {
    phase = 'role-filter-missing';
  } else if (appsMissing) {
    phase = 'service-principals-missing';
  } else if (directory.mode === 'off') {
    phase = trimmed ? 'not-configured' : 'idle';
  } else if (!trimmed) {
    phase = 'idle';
  } else if (trimmed.length < minChars) {
    phase = 'too-short';
  } else if (search.status === 'error' || (onlyWithAppRole && members.status === 'error')) {
    phase = 'error';
  } else if (search.status === 'loading' || (onlyWithAppRole && members.status === 'loading')) {
    phase = 'loading';
  } else {
    phase = suggestions.length > 0 ? 'ready' : 'empty';
  }

  const messages: Record<Phase, string> = {
    idle: '',
    'too-short': labels.tooShort(minChars),
    loading: labels.searching,
    ready: labels.results(suggestions.length),
    empty: labels.noResults,
    error: onlyWithAppRole && members.status === 'error' ? labels.roleFilterFailed : labels.error,
    'not-configured': labels.notConfigured,
    'role-filter-missing': labels.roleFilterNotConfigured,
    'service-principals-missing': labels.servicePrincipalsNotConfigured,
  };
  const popupVisible = open && !disabled && phase !== 'idle';

  function update(next: Person[]) {
    if (value === undefined) {
      setInternal(next);
    }
    onChange?.(next);
  }

  function select(person: Person) {
    update(multiple ? [...selected, person] : [person]);
    setText('');
    setOpen(false);
    inputRef.current?.focus();
  }

  function remove(person: Person) {
    update(selected.filter((entry) => entry.id !== person.id));
    inputRef.current?.focus();
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        setOpen(true);
        if (popupVisible && suggestions.length > 0) {
          setActiveId(suggestions[(activeIndex + 1) % suggestions.length]!.id);
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        setOpen(true);
        if (popupVisible && suggestions.length > 0) {
          setActiveId(suggestions[activeIndex <= 0 ? suggestions.length - 1 : activeIndex - 1]!.id);
        }
        break;
      case 'Enter':
        if (popupVisible && phase === 'ready' && activeIndex >= 0 && suggestions[activeIndex]) {
          event.preventDefault();
          select(suggestions[activeIndex]);
        }
        break;
      case 'Escape':
        if (popupVisible) {
          event.preventDefault();
          setOpen(false);
        } else if (text) {
          event.preventDefault();
          setText('');
        }
        break;
      case 'Backspace':
        if (!text && selected.length > 0) {
          remove(selected[selected.length - 1]!);
        }
        break;
      case 'Tab':
        setOpen(false);
        break;
    }
  }

  const state =
    phase === 'error' || phase === 'role-filter-missing' || phase === 'service-principals-missing' ? 'error' : phase === 'loading' ? 'loading' : 'ready';

  return (
    <div
      className={cx('seed-picker', className)}
      data-state={state}
      data-open={popupVisible || undefined}
      data-multiple={multiple || undefined}
      data-disabled={disabled || undefined}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setOpen(false);
        }
      }}
    >
      <div
        className={cx('seed-picker__control', classNames?.control)}
        onMouseDown={(event) => {
          if (event.target === event.currentTarget) {
            event.preventDefault();
            inputRef.current?.focus();
          }
        }}
      >
        {selected.map((person) => (
          <span key={person.id} className={cx('seed-picker__chip', classNames?.chip)}>
            <Avatar
              id={person.id}
              name={person.displayName}
              kind={person.kind}
              servicePrincipalType={person.servicePrincipalType}
              size={20}
              decorative
              className="seed-picker__chip-avatar"
            />
            <span className="seed-picker__chip-name">{person.displayName}</span>
            {!disabled && (
              <button
                type="button"
                className={cx('seed-picker__chip-remove', classNames?.chipRemove)}
                aria-label={labels.remove(person.displayName)}
                onClick={() => remove(person)}
              >
                <DismissIcon />
              </button>
            )}
          </span>
        ))}
        <input
          ref={inputRef}
          id={inputId}
          className={cx('seed-picker__input', classNames?.input)}
          type="text"
          role="combobox"
          aria-expanded={popupVisible}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={popupVisible && phase === 'ready' && activeIndex >= 0 ? `${listboxId}-${activeIndex}` : undefined}
          aria-label={labelledBy ? undefined : labels.input}
          aria-labelledby={labelledBy}
          aria-describedby={statusId}
          aria-invalid={state === 'error' || undefined}
          autoComplete="off"
          spellCheck={false}
          placeholder={!multiple && selected.length > 0 ? undefined : (placeholder ?? labels.placeholder)}
          value={text}
          disabled={disabled}
          onChange={(event) => {
            setText(event.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
        />
      </div>
      <div className={cx('seed-picker__popup', classNames?.popup)} hidden={!popupVisible}>
        <ul id={listboxId} role="listbox" aria-label="Vorschläge" className={cx('seed-picker__listbox', classNames?.listbox)}>
          {phase === 'ready' &&
            suggestions.map((person, index) => (
              <li
                key={person.id}
                id={`${listboxId}-${index}`}
                role="option"
                aria-selected={index === activeIndex}
                data-active={index === activeIndex || undefined}
                className={cx('seed-picker__option', classNames?.option)}
                onMouseDown={(event) => event.preventDefault()}
                onMouseEnter={() => setActiveId(person.id)}
                onClick={() => select(person)}
              >
                {renderOption ? renderOption(person, { active: index === activeIndex }) : <DefaultOption person={person} />}
              </li>
            ))}
        </ul>
        {/* Screenreader hören den Hinweis über die Live-Region unten, nicht doppelt. */}
        {phase !== 'ready' && (
          <div className={cx('seed-picker__message', classNames?.message)} data-kind={phase} aria-hidden="true">
            {messages[phase]}
          </div>
        )}
      </div>
      <span id={statusId} className="seed-visually-hidden" role="status" aria-live="polite">
        {popupVisible && phase !== 'loading' ? messages[phase] : ''}
      </span>
    </div>
  );
}

function DefaultOption({ person }: { person: Person }) {
  const details =
    person.kind === 'servicePrincipal'
      ? [servicePrincipalLabel(person.servicePrincipalType), person.appId].filter(Boolean).join(' · ')
      : [person.jobTitle, person.department].filter(Boolean).join(' · ') || person.mail || person.userPrincipalName;
  return (
    <span className="seed-picker__option-content">
      <Avatar id={person.id} name={person.displayName} kind={person.kind} servicePrincipalType={person.servicePrincipalType} size={32} decorative />
      <span className="seed-picker__option-text">
        <span className="seed-picker__option-name">{person.displayName}</span>
        {details && <span className="seed-picker__option-secondary">{details}</span>}
      </span>
    </span>
  );
}

import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useCurrentUser, type CurrentUser } from '../hooks.js';
import { useSignOut } from '../provider.js';
import { cx } from '../utils.js';
import { Avatar } from './Avatar.js';
import { ChevronDownIcon, SignOutIcon } from './icons.js';

export interface CurrentUserMenuItem {
  /** Schlüssel für React, Default der Index. */
  key?: string;
  label: ReactNode;
  icon?: ReactNode;
  onSelect?: () => void;
  /** Link statt Aktion. */
  href?: string;
  /** Link in neuem Tab. */
  external?: boolean;
  disabled?: boolean;
}

export interface CurrentUserMenuProps {
  /** Eigene Einträge (Konto, Version, Hilfe); dann öffnet ein Klick auf Avatar und Name ein Menü. */
  menuItems?: CurrentUserMenuItem[];
  /** Eigene Darstellung von Avatar, Name und Rolle. */
  renderTrigger?: (user: CurrentUser) => ReactNode;
  /** Rolle unter dem Namen, Default true. */
  showRole?: boolean;
  /** Teams-Status am Avatar. */
  presence?: boolean;
  /** Avatar-Größe, Default 32. */
  size?: number;
  /**
   * Abmelden; Default ist auth.logout() aus DirectoryProvider samt Leeren der Zwischenspeicher.
   * null blendet den Knopf aus.
   */
  onSignOut?: (() => void | Promise<void>) | null;
  labels?: { signOut?: string; menu?: string };
  className?: string;
  classNames?: {
    trigger?: string;
    avatar?: string;
    name?: string;
    role?: string;
    signOut?: string;
    menu?: string;
    menuItem?: string;
  };
}

/**
 * Angemeldete Person für die Kopfzeile: Avatar, Name, Rolle (Anzeigename aus /api/me) und Abmelden.
 * Mit menuItems ein Menü nach dem ARIA-Muster „Menu Button“.
 */
export function CurrentUserMenu({
  menuItems,
  renderTrigger,
  showRole = true,
  presence = false,
  size = 32,
  onSignOut,
  labels,
  className,
  classNames,
}: CurrentUserMenuProps) {
  const { status, user } = useCurrentUser();
  const defaultSignOut = useSignOut();
  const signOut = onSignOut === undefined ? defaultSignOut : onSignOut;
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const menuId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const itemRefs = useRef<(HTMLElement | null)[]>([]);
  const items = menuItems ?? [];
  const hasMenu = items.length > 0;

  useEffect(() => {
    if (open) {
      itemRefs.current[active]?.focus();
    }
  }, [open, active]);

  useEffect(() => {
    if (!open) {
      return;
    }
    const close = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  if (!user) {
    return status === 'loading' ? (
      <div className={cx('seed-user-menu', className)} data-state="loading" aria-busy="true">
        <span className="seed-avatar" data-size={32} data-state="loading" aria-hidden="true" />
      </div>
    ) : null;
  }

  const enabled = items.map((item, index) => (item.disabled ? -1 : index)).filter((index) => index >= 0);

  function openMenu(index: number) {
    setActive(index);
    setOpen(true);
  }

  function closeMenu(focusTrigger: boolean) {
    setOpen(false);
    if (focusTrigger) {
      triggerRef.current?.focus();
    }
  }

  function move(step: number) {
    if (enabled.length === 0) {
      return;
    }
    const position = enabled.indexOf(active);
    setActive(enabled[(position + step + enabled.length) % enabled.length]!);
  }

  function onTriggerKeyDown(event: KeyboardEvent) {
    if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      openMenu(enabled[0] ?? 0);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      openMenu(enabled[enabled.length - 1] ?? 0);
    }
  }

  function onMenuKeyDown(event: KeyboardEvent) {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        move(1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        move(-1);
        break;
      case 'Home':
        event.preventDefault();
        setActive(enabled[0] ?? 0);
        break;
      case 'End':
        event.preventDefault();
        setActive(enabled[enabled.length - 1] ?? 0);
        break;
      case 'Escape':
        event.preventDefault();
        closeMenu(true);
        break;
      case 'Tab':
        closeMenu(false);
        break;
    }
  }

  function select(item: CurrentUserMenuItem) {
    if (item.disabled) {
      return;
    }
    closeMenu(!item.href);
    item.onSelect?.();
  }

  const roleText = user.roleNames.join(', ');
  const content = renderTrigger ? (
    renderTrigger(user)
  ) : (
    <>
      <Avatar
        id={user.id}
        name={user.displayName}
        size={size}
        presence={presence}
        decorative
        className={cx('seed-user-menu__avatar', classNames?.avatar)}
      />
      <span className="seed-user-menu__text">
        <span className={cx('seed-user-menu__name', classNames?.name)}>{user.displayName}</span>
        {showRole && roleText && <span className={cx('seed-user-menu__role', classNames?.role)}>{roleText}</span>}
      </span>
    </>
  );

  return (
    <div ref={rootRef} className={cx('seed-user-menu', className)} data-state="ready" data-open={open || undefined}>
      {hasMenu ? (
        <button
          ref={triggerRef}
          type="button"
          className={cx('seed-user-menu__trigger', classNames?.trigger)}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls={open ? menuId : undefined}
          onClick={() => (open ? closeMenu(false) : openMenu(enabled[0] ?? 0))}
          onKeyDown={onTriggerKeyDown}
        >
          {content}
          <ChevronDownIcon className="seed-user-menu__chevron" />
        </button>
      ) : (
        <span className={cx('seed-user-menu__trigger', classNames?.trigger)}>{content}</span>
      )}
      {signOut && (
        <button
          type="button"
          className={cx('seed-user-menu__sign-out', classNames?.signOut)}
          aria-label={labels?.signOut ?? 'Abmelden'}
          title={labels?.signOut ?? 'Abmelden'}
          onClick={() => void signOut()}
        >
          <SignOutIcon />
        </button>
      )}
      {hasMenu && open && (
        <div id={menuId} role="menu" aria-label={labels?.menu ?? 'Kontomenü'} className={cx('seed-user-menu__menu', classNames?.menu)} onKeyDown={onMenuKeyDown}>
          {items.map((item, index) => {
            const props = {
              ref: (element: HTMLElement | null) => {
                itemRefs.current[index] = element;
              },
              role: 'menuitem',
              tabIndex: index === active ? 0 : -1,
              className: cx('seed-user-menu__item', classNames?.menuItem),
              'aria-disabled': item.disabled || undefined,
              onMouseEnter: () => !item.disabled && setActive(index),
            };
            const inner = (
              <>
                {item.icon && <span className="seed-user-menu__item-icon">{item.icon}</span>}
                <span className="seed-user-menu__item-label">{item.label}</span>
              </>
            );
            return item.href && !item.disabled ? (
              <a
                key={item.key ?? index}
                {...props}
                href={item.href}
                {...(item.external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
                onClick={() => select(item)}
              >
                {inner}
              </a>
            ) : (
              <button key={item.key ?? index} {...props} type="button" onClick={() => select(item)}>
                {inner}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

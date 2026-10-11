import { useId, useState, type ReactNode } from 'react';
import { useDirectoryAuth } from '../provider.js';
import { cx } from '../utils.js';

export interface SignInPageProps {
  /** Name der App als Überschrift. */
  appName: string;
  /** Text unter dem Namen; Default ein Hinweis auf das Konto der Organisation. */
  description?: ReactNode;
  /** Eigenes Logo über dem Namen. */
  logo?: ReactNode;
  /** Anmelden; Default auth.login() aus DirectoryProvider. */
  onSignIn?: () => void | Promise<void>;
  /** Fehlermeldung, etwa nach einer abgebrochenen Anmeldung. */
  error?: ReactNode;
  footer?: ReactNode;
  labels?: { signIn?: string; signingIn?: string };
  className?: string;
  classNames?: {
    card?: string;
    logo?: string;
    title?: string;
    description?: string;
    button?: string;
    error?: string;
    footer?: string;
  };
}

/** Einheitliche Anmeldeseite: App-Name, kurzer Text und ein Knopf, der zu Entra ID weiterleitet. */
export function SignInPage({ appName, description, logo, onSignIn, error, footer, labels, className, classNames }: SignInPageProps) {
  const auth = useDirectoryAuth();
  const signIn = onSignIn ?? (auth ? () => auth.login() : undefined);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const titleId = useId();

  async function start() {
    if (!signIn) {
      return;
    }
    setBusy(true);
    setFailed(false);
    try {
      await signIn();
    } catch {
      setBusy(false);
      setFailed(true);
    }
  }

  const message = error ?? (failed ? 'Die Anmeldung ließ sich nicht starten. Bitte versuche es noch einmal.' : null);

  return (
    <div className={cx('seed-sign-in', className)} data-state={busy ? 'busy' : 'ready'}>
      <section className={cx('seed-sign-in__card', classNames?.card)} aria-labelledby={titleId}>
        {logo && <div className={cx('seed-sign-in__logo', classNames?.logo)}>{logo}</div>}
        <h1 id={titleId} className={cx('seed-sign-in__title', classNames?.title)}>
          {appName}
        </h1>
        <p className={cx('seed-sign-in__description', classNames?.description)}>
          {description ?? 'Melde dich mit dem Microsoft-Konto deiner Organisation an.'}
        </p>
        {message && (
          <p className={cx('seed-sign-in__error', classNames?.error)} role="alert">
            {message}
          </p>
        )}
        <button
          type="button"
          className={cx('seed-sign-in__button', classNames?.button)}
          onClick={() => void start()}
          disabled={busy || !signIn}
          aria-busy={busy || undefined}
        >
          {busy ? (labels?.signingIn ?? 'Weiterleitung …') : (labels?.signIn ?? 'Anmelden')}
        </button>
        {footer && <div className={cx('seed-sign-in__footer', classNames?.footer)}>{footer}</div>}
      </section>
    </div>
  );
}

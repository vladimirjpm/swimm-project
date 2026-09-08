import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import '../deep/deep-theme.css';
import { useDeepThemeClass } from '../deep/use-deep-theme-class';

// Модал логина (фаза 4.2-4.3). Общий компонент — используется из шапки на home/competitions/groups.
// Режимы: login (Google + email/пароль) / register / forgot — переключаются внутри одного модала.
//
// Палитра — роли `--t-*` темы deep. ⚠ Модал уходит ПОРТАЛОМ в `body`, снаружи корня
// страницы, поэтому класс темы он ставит себе сам: иначе `--t-*` за пределами корня пустые
// и модал приезжает бесцветным. По той же причине здесь нет `hp-card-std` — эта карточка
// живёт в `home.css`, которого на половине страниц нет.

type Mode = 'login' | 'register' | 'forgot';

interface LoginModalProps {
  open: boolean;
  onClose: () => void;
  /** Вызывается после успешного логина (кука уже стоит) — обычно refresh() из useAuth. */
  onLoggedIn: () => void;
}

const inputClass =
  'w-full rounded-[11px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-4 py-[11px] text-[14px] font-semibold text-[var(--t-text)] outline-none placeholder:text-[var(--t-text-3)] focus:border-[var(--t-accent)]';

function GoogleIcon() {
  return (
    <svg width="17" height="17" viewBox="0 0 48 48" aria-hidden="true">
      <path fill="#FFC107" d="M43.6 20.1H42V20H24v8h11.3C33.7 32.7 29.2 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3.1 0 5.9 1.2 8 3l5.7-5.7C34.2 6.1 29.3 4 24 4 13 4 4 13 4 24s9 20 20 20 20-9 20-20c0-1.3-.1-2.6-.4-3.9z"/>
      <path fill="#FF3D00" d="M6.3 14.7l6.6 4.8C14.7 15.1 19 12 24 12c3.1 0 5.9 1.2 8 3l5.7-5.7C34.2 6.1 29.3 4 24 4 16.3 4 9.7 8.3 6.3 14.7z"/>
      <path fill="#4CAF50" d="M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.2 35.1 26.7 36 24 36c-5.2 0-9.6-3.3-11.3-8l-6.5 5C9.5 39.6 16.2 44 24 44z"/>
      <path fill="#1976D2" d="M43.6 20.1H42V20H24v8h11.3c-.8 2.3-2.3 4.3-4.2 5.7l6.2 5.2C40.9 35.7 44 30.3 44 24c0-1.3-.1-2.6-.4-3.9z"/>
    </svg>
  );
}

function LoginModal({ open, onClose, onLoggedIn }: LoginModalProps) {
  const [mode, setMode] = useState<Mode>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const deep = useDeepThemeClass();

  // Esc закрывает модал; сбрасываем состояние формы при каждом открытии.
  useEffect(() => {
    if (!open) return;
    setMode('login');
    setEmail('');
    setPassword('');
    setDisplayName('');
    setError(null);
    setStatus(null);
    setSubmitting(false);
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  const googleHref = `/auth/login/google?returnUrl=${encodeURIComponent(window.location.href)}`;

  const switchMode = (m: Mode) => {
    setMode(m);
    setError(null);
    setStatus(null);
  };

  const submitLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submitting) return;
    setError(null);
    setSubmitting(true);
    try {
      const r = await fetch('/auth/login/local', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: email.trim(), password }),
      });
      if (r.ok) {
        onLoggedIn();
        onClose();
        return;
      }
      if (r.status === 401) setError('Wrong email or password.');
      else if (r.status === 403) setError('Email not confirmed — check your inbox and follow the link we sent.');
      else if (r.status === 429) setError('Too many attempts. Try again later.');
      else setError(`Could not sign in (${r.status}).`);
    } catch {
      setError('Network unavailable. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  const submitRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submitting) return;
    setError(null);
    setStatus(null);
    setSubmitting(true);
    try {
      const r = await fetch('/auth/register', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: email.trim(),
          password,
          displayName: displayName.trim() || undefined,
        }),
      });
      if (r.ok) {
        setStatus('Email sent — confirm your address via the link we sent.');
        return;
      }
      if (r.status === 400) {
        const body = await r.json().catch(() => null);
        setError(body?.error ?? 'Invalid details.');
      } else if (r.status === 429) {
        setError('Too many attempts. Try again later.');
      } else {
        setError(`Could not create the account (${r.status}).`);
      }
    } catch {
      setError('Network unavailable. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  const submitForgot = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submitting) return;
    setError(null);
    setStatus(null);
    setSubmitting(true);
    try {
      const r = await fetch('/auth/forgot-password', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: email.trim() }),
      });
      if (r.ok) {
        setStatus('If that email is registered, a reset link is on its way.');
      } else if (r.status === 429) {
        setError('Too many attempts. Try again later.');
      } else {
        setError(`Could not send the email (${r.status}).`);
      }
    } catch {
      setError('Network unavailable. Please try again.');
    } finally {
      setSubmitting(false);
    }
  };

  const title = mode === 'login' ? 'Sign in' : mode === 'register' ? 'Create account' : 'Reset password';

  return createPortal(
    <div
      className={`${deep} fixed inset-0 z-[100] flex items-center justify-center bg-[var(--t-scrim)] p-4 backdrop-blur-[6px]`}
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="w-full max-w-[400px] rounded-[20px] border border-[var(--t-accent-border)] bg-[var(--t-surface-strong)] p-6 shadow-[var(--t-shadow)]"
      >
        <div className="mb-5 flex items-center justify-between">
          <h2 className="text-[18px] font-black tracking-[0.06em] text-[var(--t-text)]">{title}</h2>
          <button
            type="button"
            aria-label="Close"
            className="text-[18px] font-bold leading-none text-[var(--t-text-2)] hover:text-[var(--t-accent)]"
            onClick={onClose}
          >
            ✕
          </button>
        </div>

        {mode === 'login' && (
          <>
            <a
              href={googleHref}
              className="flex items-center justify-center gap-[10px] rounded-[13px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] px-4 py-[12px] text-[14px] font-extrabold text-[var(--t-text)] no-underline transition-colors hover:border-[var(--t-accent)]"
            >
              <GoogleIcon />
              Sign in with Google
            </a>

            <div className="my-5 flex items-center gap-3">
              <span className="h-px flex-1 bg-[var(--t-border)]" />
              <span className="hp-mono text-[10px] font-extrabold tracking-[0.2em] text-[var(--t-text-3)]">OR</span>
              <span className="h-px flex-1 bg-[var(--t-border)]" />
            </div>
          </>
        )}

        {mode === 'login' && (
          <form onSubmit={submitLogin} className="flex flex-col gap-3">
            <input
              type="email"
              required
              autoComplete="email"
              placeholder="Email"
              className={inputClass}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
            <input
              type="password"
              required
              autoComplete="current-password"
              placeholder="Password"
              className={inputClass}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            {error && (
              <p className="rounded-[11px] border border-[var(--t-danger-border)] bg-[var(--t-danger-soft)] px-4 py-[9px] text-[12px] font-bold text-[var(--t-danger)]">
                {error}
              </p>
            )}
            <button
              type="submit"
              disabled={submitting}
              className="mt-1 rounded-[13px] bg-[image:var(--t-accent-grad)] px-4 py-[12px] text-[14px] font-black text-[var(--t-accent-ink)] transition-opacity hover:opacity-90 disabled:opacity-50"
            >
              {submitting ? 'Signing in…' : 'Sign in'}
            </button>

            <div className="mt-1 flex items-center justify-between text-[12px] font-bold">
              <button
                type="button"
                className="text-[var(--t-accent)] hover:underline"
                onClick={() => switchMode('register')}
              >
                Create account
              </button>
              <button
                type="button"
                className="text-[var(--t-accent)] hover:underline"
                onClick={() => switchMode('forgot')}
              >
                Forgot password?
              </button>
            </div>
          </form>
        )}

        {mode === 'register' && (
          status ? (
            <div className="flex flex-col gap-3">
              <p className="rounded-[11px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] px-4 py-[9px] text-[12px] font-bold text-[var(--t-text)]">
                {status}
              </p>
              <button
                type="button"
                className="self-start text-[12px] font-bold text-[var(--t-accent)] hover:underline"
                onClick={() => switchMode('login')}
              >
                ← Back to sign in
              </button>
            </div>
          ) : (
            <form onSubmit={submitRegister} className="flex flex-col gap-3">
              <input
                type="text"
                autoComplete="name"
                placeholder="Display name (optional)"
                className={inputClass}
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
              />
              <input
                type="email"
                required
                autoComplete="email"
                placeholder="Email"
                className={inputClass}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
              <input
                type="password"
                required
                minLength={8}
                autoComplete="new-password"
                placeholder="Password"
                className={inputClass}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
              {error && (
                <p className="rounded-[11px] border border-[var(--t-danger-border)] bg-[var(--t-danger-soft)] px-4 py-[9px] text-[12px] font-bold text-[var(--t-danger)]">
                  {error}
                </p>
              )}
              <button
                type="submit"
                disabled={submitting}
                className="mt-1 rounded-[13px] bg-[image:var(--t-accent-grad)] px-4 py-[12px] text-[14px] font-black text-[var(--t-accent-ink)] transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {submitting ? 'Creating…' : 'Create account'}
              </button>

              <button
                type="button"
                className="mt-1 self-start text-[12px] font-bold text-[var(--t-accent)] hover:underline"
                onClick={() => switchMode('login')}
              >
                ← Back to sign in
              </button>
            </form>
          )
        )}

        {mode === 'forgot' && (
          status ? (
            <div className="flex flex-col gap-3">
              <p className="rounded-[11px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] px-4 py-[9px] text-[12px] font-bold text-[var(--t-text)]">
                {status}
              </p>
              <button
                type="button"
                className="self-start text-[12px] font-bold text-[var(--t-accent)] hover:underline"
                onClick={() => switchMode('login')}
              >
                ← Back to sign in
              </button>
            </div>
          ) : (
            <form onSubmit={submitForgot} className="flex flex-col gap-3">
              <input
                type="email"
                required
                autoComplete="email"
                placeholder="Email"
                className={inputClass}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
              {error && (
                <p className="rounded-[11px] border border-[var(--t-danger-border)] bg-[var(--t-danger-soft)] px-4 py-[9px] text-[12px] font-bold text-[var(--t-danger)]">
                  {error}
                </p>
              )}
              <button
                type="submit"
                disabled={submitting}
                className="mt-1 rounded-[13px] bg-[image:var(--t-accent-grad)] px-4 py-[12px] text-[14px] font-black text-[var(--t-accent-ink)] transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {submitting ? 'Sending…' : 'Send reset link'}
              </button>

              <button
                type="button"
                className="mt-1 self-start text-[12px] font-bold text-[var(--t-accent)] hover:underline"
                onClick={() => switchMode('login')}
              >
                ← Back to sign in
              </button>
            </form>
          )
        )}
      </div>
    </div>,
    document.body
  );
}

export default LoginModal;

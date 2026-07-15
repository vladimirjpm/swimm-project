import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';

// Модал логина (фаза 4.2-4.3). Общий компонент — используется из шапки на home/competitions/groups.
// Режимы: login (Google + email/пароль) / register / forgot — переключаются внутри одного модала.

type Mode = 'login' | 'register' | 'forgot';

interface LoginModalProps {
  open: boolean;
  onClose: () => void;
  /** Вызывается после успешного логина (кука уже стоит) — обычно refresh() из useAuth. */
  onLoggedIn: () => void;
}

const inputClass =
  'w-full rounded-[11px] border border-[#7dd3fc]/25 bg-[rgba(4,16,32,0.7)] px-4 py-[11px] text-[14px] font-semibold text-[#f3f8fd] outline-none placeholder:text-[#c9dcee]/40 focus:border-[#7dd3fc]/60';

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
      if (r.status === 401) setError('Неверный email или пароль.');
      else if (r.status === 403) setError('Email не подтверждён — проверь почту и перейди по ссылке из письма.');
      else if (r.status === 429) setError('Слишком много попыток. Попробуй позже.');
      else setError(`Не удалось войти (${r.status}).`);
    } catch {
      setError('Сеть недоступна. Попробуй ещё раз.');
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
        setStatus('Письмо отправлено — подтверди email по ссылке из письма.');
        return;
      }
      if (r.status === 400) {
        const body = await r.json().catch(() => null);
        setError(body?.error ?? 'Некорректные данные.');
      } else if (r.status === 429) {
        setError('Слишком много попыток. Попробуй позже.');
      } else {
        setError(`Не удалось зарегистрироваться (${r.status}).`);
      }
    } catch {
      setError('Сеть недоступна. Попробуй ещё раз.');
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
        setStatus('Если такой email зарегистрирован, письмо со ссылкой отправлено.');
      } else if (r.status === 429) {
        setError('Слишком много попыток. Попробуй позже.');
      } else {
        setError(`Не удалось отправить письмо (${r.status}).`);
      }
    } catch {
      setError('Сеть недоступна. Попробуй ещё раз.');
    } finally {
      setSubmitting(false);
    }
  };

  const title = mode === 'login' ? 'Sign in' : mode === 'register' ? 'Create account' : 'Reset password';

  return createPortal(
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-[rgba(2,10,24,0.72)] p-4 backdrop-blur-[6px]"
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="hp-card-std w-full max-w-[400px] rounded-[20px] border border-[#7dd3fc]/35 p-6 shadow-[0_28px_60px_rgba(2,10,24,0.7)]"
      >
        <div className="mb-5 flex items-center justify-between">
          <h2 className="text-[18px] font-black tracking-[0.06em] text-[#f3f8fd]">{title}</h2>
          <button
            type="button"
            aria-label="Close"
            className="text-[18px] font-bold leading-none text-[#cfe6f6] hover:text-[#7dd3fc]"
            onClick={onClose}
          >
            ✕
          </button>
        </div>

        {mode === 'login' && (
          <>
            <a
              href={googleHref}
              className="flex items-center justify-center gap-[10px] rounded-[13px] border border-[#7dd3fc]/35 bg-[rgba(56,189,248,0.1)] px-4 py-[12px] text-[14px] font-extrabold text-[#f3f8fd] no-underline transition-colors hover:bg-[rgba(56,189,248,0.18)]"
            >
              <GoogleIcon />
              Sign in with Google
            </a>

            <div className="my-5 flex items-center gap-3">
              <span className="h-px flex-1 bg-[#7dd3fc]/20" />
              <span className="hp-mono text-[10px] font-extrabold tracking-[0.2em] text-[#c9dcee]/50">OR</span>
              <span className="h-px flex-1 bg-[#7dd3fc]/20" />
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
              <p className="rounded-[11px] border border-[#f87171]/35 bg-[rgba(248,113,113,0.08)] px-4 py-[9px] text-[12px] font-bold text-[#fca5a5]">
                {error}
              </p>
            )}
            <button
              type="submit"
              disabled={submitting}
              className="mt-1 rounded-[13px] bg-[linear-gradient(140deg,#38bdf8,#0369a1)] px-4 py-[12px] text-[14px] font-black text-[#06263f] transition-opacity hover:opacity-90 disabled:opacity-50"
            >
              {submitting ? 'Signing in…' : 'Sign in'}
            </button>

            <div className="mt-1 flex items-center justify-between text-[12px] font-bold">
              <button
                type="button"
                className="text-[#7dd3fc] hover:underline"
                onClick={() => switchMode('register')}
              >
                Create account
              </button>
              <button
                type="button"
                className="text-[#7dd3fc] hover:underline"
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
              <p className="rounded-[11px] border border-[#7dd3fc]/35 bg-[rgba(56,189,248,0.1)] px-4 py-[9px] text-[12px] font-bold text-[#e0f2fe]">
                {status}
              </p>
              <button
                type="button"
                className="self-start text-[12px] font-bold text-[#7dd3fc] hover:underline"
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
                <p className="rounded-[11px] border border-[#f87171]/35 bg-[rgba(248,113,113,0.08)] px-4 py-[9px] text-[12px] font-bold text-[#fca5a5]">
                  {error}
                </p>
              )}
              <button
                type="submit"
                disabled={submitting}
                className="mt-1 rounded-[13px] bg-[linear-gradient(140deg,#38bdf8,#0369a1)] px-4 py-[12px] text-[14px] font-black text-[#06263f] transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {submitting ? 'Creating…' : 'Create account'}
              </button>

              <button
                type="button"
                className="mt-1 self-start text-[12px] font-bold text-[#7dd3fc] hover:underline"
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
              <p className="rounded-[11px] border border-[#7dd3fc]/35 bg-[rgba(56,189,248,0.1)] px-4 py-[9px] text-[12px] font-bold text-[#e0f2fe]">
                {status}
              </p>
              <button
                type="button"
                className="self-start text-[12px] font-bold text-[#7dd3fc] hover:underline"
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
                <p className="rounded-[11px] border border-[#f87171]/35 bg-[rgba(248,113,113,0.08)] px-4 py-[9px] text-[12px] font-bold text-[#fca5a5]">
                  {error}
                </p>
              )}
              <button
                type="submit"
                disabled={submitting}
                className="mt-1 rounded-[13px] bg-[linear-gradient(140deg,#38bdf8,#0369a1)] px-4 py-[12px] text-[14px] font-black text-[#06263f] transition-opacity hover:opacity-90 disabled:opacity-50"
              >
                {submitting ? 'Sending…' : 'Send reset link'}
              </button>

              <button
                type="button"
                className="mt-1 self-start text-[12px] font-bold text-[#7dd3fc] hover:underline"
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

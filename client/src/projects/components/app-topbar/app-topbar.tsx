import React, { useEffect, useRef, useState } from 'react';
import { useAuth } from '../../../hooks/useAuth';
import LoginModal from '../login-modal/login-modal';
import { routes } from '../../../utils/routes';

/**
 * AppTopbar — глобальная полоса навигации + auth (design_handoff_topbar/README.md).
 * Ставится на все страницы, КРОМЕ главной (home.html остаётся с HomeHeader).
 * Цвета полосы — токены --theme-topbar-* (index.css), деривируются из активной темы.
 *
 * Auth: компонент сам дёргает useAuth() (модульный синглтон, один /auth/me на
 * страницу) — пропсы user/onLogin/onLogout НЕобязательные override'ы для случаев,
 * когда страница хочет управлять auth снаружи. Хендофф писался до useAuth-синглтона.
 */

export type TopbarActivePage = 'home' | 'competitions' | 'groups' | 'normatives' | 'records' | 'about';

export interface AppTopbarProps {
  /** Активный пункт. Не передан (results_main) — ни один пункт не подсвечен. */
  active?: TopbarActivePage;
  /** Override auth-состояния; по умолчанию компонент читает useAuth() сам. */
  user?: { name: string; avatarUrl?: string } | null;
  onLogin?: () => void;
  onLogout?: () => void;
}

/** Пункты навигации. Normatives/Records — страниц НЕ существует, рендерим span. */
const NAV_LINKS: { label: string; href?: string; key?: TopbarActivePage }[] = [
  { label: 'Home', href: routes.home(), key: 'home' },
  { label: 'Competitions', href: routes.competitionsList(), key: 'competitions' },
  { label: 'Groups', href: routes.groupsList(), key: 'groups' },
  { label: 'Normatives' },
  { label: 'Records' },
  { label: 'About', href: routes.about(), key: 'about' },
];

function TopbarLogo() {
  return (
    <a href={routes.home()} className="flex shrink-0 items-center gap-2 no-underline">
      <span className="flex h-[26px] w-[26px] items-center justify-center rounded-[8px] bg-[var(--theme-topbar-accent)] text-[13px] font-black text-[var(--theme-topbar-accent-text)]">
        S
      </span>
      <span className="text-[11px] font-black tracking-[0.22em] text-[var(--theme-topbar-text)]">
        SWIM<span className="text-[var(--theme-topbar-accent)]">HUB</span>
      </span>
    </a>
  );
}

/** Аватар: картинка или кружок-инициал на accent-фоне (30px по хендоффу). */
function TopbarAvatar({ name, avatarUrl, size }: { name: string; avatarUrl: string | null; size: number }) {
  if (avatarUrl) {
    return (
      <img
        src={avatarUrl}
        alt=""
        referrerPolicy="no-referrer"
        className="rounded-full border border-[var(--theme-topbar-text-muted)]"
        style={{ width: size, height: size }}
      />
    );
  }
  return (
    <span
      className="flex items-center justify-center rounded-full bg-[var(--theme-topbar-accent)] font-black text-[var(--theme-topbar-accent-text)]"
      style={{ width: size, height: size, fontSize: size * 0.45 }}
    >
      {(name || '?').charAt(0).toUpperCase()}
    </span>
  );
}

function TopbarNavItem({
  link,
  active,
  mobile,
  onSelect,
}: {
  link: (typeof NAV_LINKS)[number];
  active?: TopbarActivePage;
  mobile?: boolean;
  onSelect?: () => void;
}) {
  const isActive = !!link.key && link.key === active;
  if (mobile) {
    const cls =
      'flex items-center rounded-[10px] px-4 py-[12px] text-[15px] font-extrabold no-underline transition-colors';
    if (!link.href) {
      return <span className={`${cls} cursor-default text-[var(--theme-topbar-text-muted)]`}>{link.label}</span>;
    }
    return (
      <a
        href={link.href}
        onClick={onSelect}
        className={`${cls} ${
          isActive ? 'text-[var(--theme-topbar-text)] bg-[color-mix(in_srgb,var(--theme-topbar-accent)_16%,transparent)]' : 'text-[var(--theme-topbar-text-muted)] hover:text-[var(--theme-topbar-text)]'
        }`}
      >
        {link.label}
      </a>
    );
  }
  // Десктоп: активный пункт — текст + underline 2px accent; высота полосы 46px,
  // underline рисуем border-b на всю высоту пункта, чтобы прижался к низу полосы.
  const base = 'flex h-[46px] items-center border-b-2 text-[13px] font-bold no-underline transition-colors';
  if (!link.href) {
    return (
      <span className={`${base} cursor-default border-transparent text-[var(--theme-topbar-text-muted)]`}>
        {link.label}
      </span>
    );
  }
  return (
    <a
      href={link.href}
      className={`${base} ${
        isActive
          ? 'border-[var(--theme-topbar-accent)] text-[var(--theme-topbar-text)]'
          : 'border-transparent text-[var(--theme-topbar-text-muted)] hover:text-[var(--theme-topbar-text)]'
      }`}
    >
      {link.label}
    </a>
  );
}

function AppTopbar({ active, user, onLogin, onLogout }: AppTopbarProps) {
  const auth = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const [loginOpen, setLoginOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);

  // Override-пропсы: user !== undefined → auth-состояние управляется снаружи.
  const overridden = user !== undefined;
  const authLoading = overridden ? false : auth.loading;
  const isAuthenticated = overridden ? user !== null : auth.isAuthenticated;
  const userName = overridden ? user?.name || 'User' : auth.displayName || auth.email || 'User';
  const avatarUrl = overridden ? user?.avatarUrl || null : auth.avatarUrl;

  // Клик мимо дропдауна закрывает его (эталон — home-header.tsx).
  useEffect(() => {
    if (!userMenuOpen) return;
    const onDown = (e: MouseEvent) => {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target as Node)) setUserMenuOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [userMenuOpen]);

  const handleLogin = () => {
    if (onLogin) return onLogin();
    setLoginOpen(true);
  };

  const logoutHref = `/auth/logout?returnUrl=${encodeURIComponent(window.location.href)}`;

  const signOutEverywhere = async () => {
    try {
      await fetch('/auth/logout-all', { method: 'POST', credentials: 'include' });
    } finally {
      setUserMenuOpen(false);
      setMenuOpen(false);
      auth.refresh();
    }
  };

  const dropdownItemClass =
    'block w-full rounded-[8px] px-3 py-[9px] text-left text-[13px] font-bold text-[var(--theme-topbar-text)] no-underline transition-colors hover:bg-[color-mix(in_srgb,var(--theme-topbar-accent)_16%,transparent)]';

  const authBlock = authLoading ? null : !isAuthenticated ? (
    <button
      type="button"
      className="rounded-[8px] bg-[var(--theme-topbar-accent)] px-3.5 py-[6px] text-[12px] font-extrabold text-[var(--theme-topbar-accent-text)] transition-opacity hover:opacity-90"
      onClick={handleLogin}
    >
      Login
    </button>
  ) : (
    <div className="relative" ref={userMenuRef}>
      <button
        type="button"
        className="flex items-center gap-2"
        aria-haspopup="menu"
        aria-expanded={userMenuOpen}
        onClick={() => setUserMenuOpen((open) => !open)}
      >
        <TopbarAvatar name={userName} avatarUrl={avatarUrl} size={30} />
        <span className="hidden max-w-[140px] truncate text-[12px] font-bold text-[var(--theme-topbar-text)] sm:block">
          {userName}
        </span>
        <span className="text-[9px] text-[var(--theme-topbar-accent)]">▾</span>
      </button>
      {userMenuOpen && (
        <div className="absolute right-0 top-[40px] w-[210px] rounded-[12px] border border-[color-mix(in_srgb,var(--theme-topbar-text)_18%,transparent)] bg-[var(--theme-topbar-bg)] p-1.5 shadow-[0_18px_44px_rgba(0,0,0,0.45)]">
          <a href={routes.myMedia()} className={dropdownItemClass}>
            My media
          </a>
          {onLogout ? (
            <button type="button" className={dropdownItemClass} onClick={() => { setUserMenuOpen(false); onLogout(); }}>
              Sign out
            </button>
          ) : (
            <a href={logoutHref} className={dropdownItemClass}>
              Sign out
            </a>
          )}
          {!onLogout && (
            <button type="button" className={dropdownItemClass} onClick={signOutEverywhere}>
              Sign out everywhere
            </button>
          )}
        </div>
      )}
    </div>
  );

  return (
    // Полоса остаётся LTR и на RTL-страницах (правило хендоффа).
    <header dir="ltr" className="sticky top-0 z-50 bg-[var(--theme-topbar-bg)]">
      <div className="flex h-[46px] items-center justify-between gap-4 px-4 lg:px-8">
        <TopbarLogo />

        <nav className="hidden items-center gap-6 md:flex" aria-label="Main">
          {NAV_LINKS.map((link) => (
            <TopbarNavItem key={link.label} link={link} active={active} />
          ))}
        </nav>

        <div className="flex items-center gap-2">
          {authBlock}
          {/* Бургер (мобильный): nav сворачивается, лого + auth остаются */}
          <button
            type="button"
            className="flex h-9 w-9 flex-col items-end justify-center gap-[4px] md:hidden"
            aria-label={menuOpen ? 'Close menu' : 'Open menu'}
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen((open) => !open)}
          >
            {menuOpen ? (
              <span className="w-full text-center text-[17px] font-bold leading-none text-[var(--theme-topbar-text)]">✕</span>
            ) : (
              <>
                <span className="h-[2px] w-[19px] rounded-[2px] bg-[var(--theme-topbar-text)]" />
                <span className="h-[2px] w-[13px] rounded-[2px] bg-[var(--theme-topbar-text)]" />
              </>
            )}
          </button>
        </div>
      </div>

      {menuOpen && (
        <div className="absolute left-2 right-2 top-[50px] rounded-[14px] border border-[color-mix(in_srgb,var(--theme-topbar-text)_18%,transparent)] bg-[var(--theme-topbar-bg)] p-2 shadow-[0_18px_44px_rgba(0,0,0,0.45)] md:hidden">
          {NAV_LINKS.map((link) => (
            <TopbarNavItem key={link.label} link={link} active={active} mobile onSelect={() => setMenuOpen(false)} />
          ))}
          {!authLoading && !isAuthenticated && (
            <div className="mt-2 border-t border-[color-mix(in_srgb,var(--theme-topbar-text)_18%,transparent)] pt-2">
              <button
                type="button"
                className="w-full rounded-[10px] bg-[var(--theme-topbar-accent)] px-4 py-[10px] text-[14px] font-extrabold text-[var(--theme-topbar-accent-text)]"
                onClick={() => { setMenuOpen(false); handleLogin(); }}
              >
                Login
              </button>
            </div>
          )}
          {!authLoading && isAuthenticated && (
            <div className="mt-2 border-t border-[color-mix(in_srgb,var(--theme-topbar-text)_18%,transparent)] pt-1">
              <div className="flex items-center gap-3 px-4 py-[8px]">
                <TopbarAvatar name={userName} avatarUrl={avatarUrl} size={28} />
                <span className="truncate text-[13px] font-extrabold text-[var(--theme-topbar-text)]">{userName}</span>
              </div>
              <a
                href={routes.myMedia()}
                className="block rounded-[10px] px-4 py-[10px] text-[14px] font-extrabold text-[var(--theme-topbar-text)] no-underline transition-colors hover:bg-[color-mix(in_srgb,var(--theme-topbar-accent)_16%,transparent)]"
              >
                My media
              </a>
              {onLogout ? (
                <button
                  type="button"
                  className="block w-full rounded-[10px] px-4 py-[10px] text-left text-[14px] font-extrabold text-[var(--theme-topbar-text)] transition-colors hover:bg-[color-mix(in_srgb,var(--theme-topbar-accent)_16%,transparent)]"
                  onClick={() => { setMenuOpen(false); onLogout(); }}
                >
                  Sign out
                </button>
              ) : (
                <>
                  <a
                    href={logoutHref}
                    className="block rounded-[10px] px-4 py-[10px] text-[14px] font-extrabold text-[var(--theme-topbar-text)] no-underline transition-colors hover:bg-[color-mix(in_srgb,var(--theme-topbar-accent)_16%,transparent)]"
                  >
                    Sign out
                  </a>
                  <button
                    type="button"
                    className="block w-full rounded-[10px] px-4 py-[10px] text-left text-[14px] font-extrabold text-[var(--theme-topbar-text)] transition-colors hover:bg-[color-mix(in_srgb,var(--theme-topbar-accent)_16%,transparent)]"
                    onClick={signOutEverywhere}
                  >
                    Sign out everywhere
                  </button>
                </>
              )}
            </div>
          )}
        </div>
      )}

      {/* Свой стейт модалки, как в HomeHeader — работает без LoginModalProvider */}
      {!overridden && <LoginModal open={loginOpen} onClose={() => setLoginOpen(false)} onLoggedIn={() => auth.refresh()} />}
    </header>
  );
}

export default AppTopbar;

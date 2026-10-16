import React, { useEffect, useRef, useState } from 'react';
import { useAuth } from '../../../hooks/useAuth';
import { useProfileSetup } from '../../../hooks/useProfileSetup';
import LoginModal from '../../components/login-modal/login-modal';
import UI_BrandMark from '../../components/mix/brand-mark/brand-mark';
import { routes } from '../../../utils/routes';

type ActivePage = 'home' | 'competitions' | 'groups';

export function SwimHubLogo() {
  return (
    <a href={routes.home()} className="flex items-center gap-3 no-underline">
      <UI_BrandMark className="h-[30px] w-[30px] lg:h-9 lg:w-9" />
      <span className="text-[12px] font-black tracking-[0.22em] text-[var(--t-text)] lg:text-[14px]">
        SWIM<span className="text-[var(--t-accent)]">HUB</span>
      </span>
    </a>
  );
}

const NAV_LINKS: { label: string; href?: string; key?: ActivePage }[] = [
  { label: 'Home', href: routes.home(), key: 'home' },
  { label: 'Competitions', href: routes.competitionsList(), key: 'competitions' },
  { label: 'Groups', href: routes.groupsList(), key: 'groups' },
  { label: 'Normatives' },
  { label: 'Records' },
  { label: 'About', href: routes.about() },
];

function MenuItem({
  href,
  label,
  trailing,
  onSelect,
}: {
  href?: string;
  label: string;
  trailing: React.ReactNode;
  onSelect: () => void;
}) {
  const className =
    'flex items-center justify-between rounded-[13px] px-4 py-[15px] text-[17px] font-extrabold text-[var(--t-text)] no-underline transition-colors hover:bg-[var(--t-accent-soft)] active:bg-[var(--t-accent-soft)]';
  if (!href) {
    return (
      <span className={className}>
        {label}
        {trailing}
      </span>
    );
  }
  return (
    <a href={href} className={className} onClick={onSelect}>
      {label}
      {trailing}
    </a>
  );
}

/** Аватар (картинка или инициал) — общий для десктоп-кнопки и мобильного меню. */
function UserAvatar({ name, avatarUrl, size }: { name: string; avatarUrl: string | null; size: number }) {
  if (avatarUrl) {
    return (
      <img
        src={avatarUrl}
        alt=""
        referrerPolicy="no-referrer"
        className="rounded-full border border-[var(--t-accent-border)]"
        style={{ width: size, height: size }}
      />
    );
  }
  return (
    <span
      className="flex items-center justify-center rounded-full bg-[image:var(--t-accent-grad)] font-black text-[var(--t-accent-ink)]"
      style={{ width: size, height: size, fontSize: size * 0.45 }}
    >
      {(name || '?').charAt(0).toUpperCase()}
    </span>
  );
}

/**
 * Точка «профиль не настроен» (хендофф set-up-profile 1a) — та же, что у аватара в AppTopbar,
 * на ролях темы deep. Кольцо — цвет панели, чтобы точка отделялась от градиента аватара.
 */
function SetupDot() {
  return (
    <span
      aria-hidden="true"
      className="absolute right-[-1px] top-[-1px] h-[9px] w-[9px] rounded-full bg-[var(--t-text)] shadow-[0_0_0_2px_var(--t-surface-strong)]"
    />
  );
}

/**
 * Пункт «Set up profile» с прогрессом (docs/plans/profile-setup-plan.md §4) — двойник пункта
 * AppTopbar на ролях `--t-*`: главная рисует свою шапку, а новичок после входа чаще всего здесь.
 * Фон — как hover соседних пунктов. `mobile` — отступы пунктов бургера.
 */
function SetupProfileItem({ done, total, mobile }: { done: number; total: number; mobile?: boolean }) {
  const pct = total > 0 ? Math.round((done / total) * 100) : 0;
  return (
    <a
      href={routes.setupProfile()}
      className={`mb-[2px] flex flex-col gap-[6px] bg-[var(--t-accent-soft)] no-underline ${
        mobile ? 'rounded-[13px] px-4 py-[12px]' : 'rounded-[10px] px-4 py-[10px]'
      }`}
    >
      <span className="flex items-center gap-[6px]">
        <span className={`flex-1 font-bold text-[var(--t-text)] ${mobile ? 'text-[15px] font-extrabold' : 'text-[13px]'}`}>
          Set up profile
        </span>
        <span className="text-[12px] font-bold text-[var(--t-accent)]">
          {done}/{total}
        </span>
      </span>
      <span
        className="block h-[3px] rounded-[2px] bg-[color-mix(in_srgb,var(--t-text)_14%,transparent)]"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={total}
        aria-valuenow={done}
        aria-label="Profile setup progress"
      >
        <span className="block h-full rounded-[2px] bg-[var(--t-accent)]" style={{ width: `${pct}%` }} />
      </span>
      <span className="text-[11px] leading-[1.35] text-[color-mix(in_srgb,var(--t-text)_78%,transparent)]">Add yourself, your club and group</span>
    </a>
  );
}

function HomeHeader({ active }: { active: ActivePage }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [loginOpen, setLoginOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);
  const auth = useAuth();
  const closeMenu = () => setMenuOpen(false);

  const userName = auth.displayName || auth.email || 'User';

  const { setup } = useProfileSetup();
  const showSetup = auth.isAuthenticated && !!setup?.visible;

  // Клик мимо дропдауна закрывает его.
  useEffect(() => {
    if (!userMenuOpen) return;
    const onDown = (e: MouseEvent) => {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target as Node)) setUserMenuOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [userMenuOpen]);

  const logoutHref = `/auth/logout?returnUrl=${encodeURIComponent(window.location.href)}`;

  const signOutEverywhere = async () => {
    try {
      await fetch('/auth/logout-all', { method: 'POST', credentials: 'include' });
    } finally {
      setUserMenuOpen(false);
      closeMenu();
      auth.refresh();
    }
  };

  const signInButtonClass =
    'rounded-[11px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] px-4 py-[7px] text-[13px] font-extrabold text-[var(--t-text)] transition-colors hover:border-[var(--t-accent)]';

  const dropdownItemClass =
    'block w-full rounded-[10px] px-4 py-[10px] text-left text-[13px] font-bold text-[var(--t-text)] no-underline transition-colors hover:bg-[var(--t-accent-soft)]';

  return (
    <header className="relative z-30 flex items-center justify-between px-5 py-[18px] lg:px-16 lg:py-[34px]">
      <SwimHubLogo />

      <div className="hidden items-center gap-[34px] lg:flex">
        <nav className="flex items-center gap-[34px]" aria-label="Main">
          {NAV_LINKS.map((link) =>
            link.href ? (
              <a
                key={link.label}
                href={link.href}
                className={`text-[14px] font-bold no-underline ${
                  link.key === active ? 'text-[var(--t-accent)]' : 'text-[var(--t-text)] hover:text-[var(--t-accent)]'
                }`}
              >
                {link.label}
              </a>
            ) : (
              <span key={link.label} className="cursor-default text-[14px] font-bold text-[var(--t-text-2)]">
                {link.label}
              </span>
            )
          )}
        </nav>

        {/* User-меню (desktop). Пока /auth/me грузится — ничего, чтобы не мигало Войти→аватар. */}
        {!auth.loading && !auth.isAuthenticated && (
          <button type="button" className={signInButtonClass} onClick={() => setLoginOpen(true)}>
            Sign in
          </button>
        )}
        {!auth.loading && auth.isAuthenticated && (
          <div className="relative" ref={userMenuRef}>
            <button
              type="button"
              className="flex items-center gap-2"
              aria-haspopup="menu"
              aria-expanded={userMenuOpen}
              onClick={() => setUserMenuOpen((open) => !open)}
            >
              <span className="relative flex">
                <UserAvatar name={userName} avatarUrl={auth.avatarUrl} size={30} />
                {showSetup && <SetupDot />}
              </span>
              <span className="max-w-[140px] truncate text-[13px] font-bold text-[var(--t-text)]">{userName}</span>
              <span className="text-[10px] text-[var(--t-accent)]">▾</span>
            </button>
            {userMenuOpen && (
              <div className="absolute right-0 top-[42px] z-40 w-[220px] rounded-[16px] border border-[var(--t-accent-border)] bg-[var(--t-surface-strong)] p-2 shadow-[var(--t-shadow)] backdrop-blur-[18px]">
                {showSetup && setup && <SetupProfileItem done={setup.done} total={setup.total} />}
                <a href={routes.myMedia()} className={dropdownItemClass}>
                  My media
                </a>
                <a href={logoutHref} className={dropdownItemClass}>
                  Sign out
                </a>
                <button type="button" className={dropdownItemClass} onClick={signOutEverywhere}>
                  Sign out everywhere
                </button>
              </div>
            )}
          </div>
        )}
      </div>

      {/* На телефоне аватара в шапке главной нет — точка «профиль не настроен» на бургере. */}
      <button
        type="button"
        className="relative flex h-11 w-11 flex-col items-end justify-center gap-[5px] lg:hidden"
        aria-label={menuOpen ? 'Close menu' : 'Open menu'}
        aria-expanded={menuOpen}
        onClick={() => setMenuOpen((open) => !open)}
      >
        {menuOpen ? (
          <span className="w-full text-center text-[20px] font-bold leading-none text-[var(--t-text-2)]">✕</span>
        ) : (
          <>
            <span className="h-[2.5px] w-[22px] rounded-[2px] bg-[var(--t-text-2)]" />
            <span className="h-[2.5px] w-[15px] rounded-[2px] bg-[var(--t-text-2)]" />
            {showSetup && (
              <span
                aria-hidden="true"
                className="absolute right-[0px] top-[9px] h-[9px] w-[9px] rounded-full bg-[var(--t-text)] shadow-[0_0_0_2px_var(--t-surface-strong)]"
              />
            )}
          </>
        )}
      </button>

      {menuOpen && (
        <div className="hp-menu-panel absolute left-3 right-3 top-[66px] z-40 rounded-[20px] border border-[var(--t-accent-border)] bg-[var(--t-surface-strong)] p-[10px] shadow-[var(--t-shadow)] backdrop-blur-[18px] lg:hidden">
          <MenuItem
            href={routes.competitionsList()}
            label="Competitions"
            onSelect={closeMenu}
            trailing={
              <span className="hp-mono text-[10px] font-extrabold text-[var(--t-live)]">● LIVE</span>
            }
          />
          <MenuItem
            label="Normatives"
            onSelect={closeMenu}
            trailing={<span className="text-[17px] font-bold text-[var(--t-accent)]">→</span>}
          />
          <MenuItem
            label="Records"
            onSelect={closeMenu}
            trailing={
              <span className="hp-mono text-[10px] font-extrabold text-[var(--t-warn)]">★ 3 NEW</span>
            }
          />
          <MenuItem
            href={routes.about()}
            label="About"
            onSelect={closeMenu}
            trailing={<span className="text-[17px] font-bold text-[var(--t-accent)]">→</span>}
          />

          {/* Auth-блок мобильного меню */}
          {!auth.loading && (
            <div className="mt-[10px] border-t border-[var(--t-border)] pt-[6px]">
              {!auth.isAuthenticated ? (
                <button
                  type="button"
                  className="flex w-full items-center justify-between rounded-[13px] px-4 py-[15px] text-[17px] font-extrabold text-[var(--t-text)] transition-colors hover:bg-[var(--t-accent-soft)]"
                  onClick={() => { closeMenu(); setLoginOpen(true); }}
                >
                  Sign in
                  <span className="text-[17px] font-bold text-[var(--t-accent)]">→</span>
                </button>
              ) : (
                <>
                  <div className="flex items-center gap-3 px-4 py-[10px]">
                    <UserAvatar name={userName} avatarUrl={auth.avatarUrl} size={28} />
                    <span className="truncate text-[14px] font-extrabold text-[var(--t-text)]">{userName}</span>
                  </div>
                  {showSetup && setup && <SetupProfileItem done={setup.done} total={setup.total} mobile />}
                  <a
                    href={routes.myMedia()}
                    className="block rounded-[13px] px-4 py-[12px] text-[15px] font-extrabold text-[var(--t-text)] no-underline transition-colors hover:bg-[var(--t-accent-soft)]"
                  >
                    My media
                  </a>
                  <a
                    href={logoutHref}
                    className="block rounded-[13px] px-4 py-[12px] text-[15px] font-extrabold text-[var(--t-text)] no-underline transition-colors hover:bg-[var(--t-accent-soft)]"
                  >
                    Sign out
                  </a>
                  <button
                    type="button"
                    className="block w-full rounded-[13px] px-4 py-[12px] text-left text-[15px] font-extrabold text-[var(--t-text)] transition-colors hover:bg-[var(--t-accent-soft)]"
                    onClick={signOutEverywhere}
                  >
                    Sign out everywhere
                  </button>
                </>
              )}
            </div>
          )}

          <div className="mt-[10px] flex items-center justify-between border-t border-[var(--t-border)] px-4 pb-[6px] pt-[14px]">
            <span className="text-[11px] font-bold text-[var(--t-text-2)]">Countries — coming 2026</span>
            <span className="hp-mono rounded-[7px] border border-[var(--t-border)] px-2 py-[3px] text-[11px] font-extrabold text-[var(--t-text-2)]">
              SOON
            </span>
          </div>
        </div>
      )}

      <LoginModal open={loginOpen} onClose={() => setLoginOpen(false)} onLoggedIn={() => auth.refresh()} />
    </header>
  );
}

export default HomeHeader;

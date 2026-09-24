import React, { useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import { useAuth } from '../../hooks/useAuth';
import { useFavorites, type FavoriteDto } from '../../hooks/useFavorites';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { routes } from '../../utils/routes';
import { sortByFavoriteRank } from '../../utils/helpers/favorites-order';

/**
 * Страница «My favorites» (docs/plans/family-favorites-plan.md, решения Влада 24.09.2026):
 * всё своё избранное в одном месте — пловцы и клубы, «это я» и пометка «семья».
 *
 * «Семья» (золотое сердечко) ставится ТОЛЬКО здесь: на остальных экранах новых кнопок нет,
 * там семья лишь поднимает пловца в списке (Me → семья → остальные). Прав пометка не даёт —
 * ни семья, ни «это я»: это удобство, а не подтверждённая связь (родителю прав не даём).
 */

const STAR = 'M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z';
const HEART = 'M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z';

// Цвета значков — те же, что у UI_FavoriteControls: звезда и семья в золоте, выключенное — контур.
const GOLD_FILL = '#f5b800';
const GOLD_STROKE = '#d99a00';
const OFF_STROKE = '#9aa3af';

function IconToggle({ on, path, title, onClick, busy }: {
  on: boolean; path: string; title: string; onClick: () => void; busy: boolean;
}) {
  return (
    <button
      type="button"
      title={title}
      aria-pressed={on}
      disabled={busy}
      onClick={onClick}
      className="flex h-8 w-8 items-center justify-center rounded-full border border-[var(--t-border)] bg-[var(--t-surface)] transition-transform hover:scale-110 disabled:opacity-40"
    >
      <svg width="16" height="16" viewBox="0 0 24 24" fill={on ? GOLD_FILL : 'none'} stroke={on ? GOLD_STROKE : OFF_STROKE} strokeWidth="1.9" strokeLinejoin="round">
        <path d={path} />
      </svg>
    </button>
  );
}

function RemoveButton({ onClick, busy, label }: { onClick: () => void; busy: boolean; label: string }) {
  return (
    <button
      type="button"
      title={`Remove ${label} from favorites`}
      disabled={busy}
      onClick={onClick}
      className="hp-mono flex h-8 items-center rounded-full border border-[var(--t-border)] px-3 text-[12px] font-extrabold text-[var(--t-text-2)] hover:border-[var(--t-danger)] hover:text-[var(--t-danger)] disabled:opacity-40"
    >
      Remove
    </button>
  );
}

function MyFavorites() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();
  const deep = useDeepThemeClass();
  const fav = useFavorites();
  // Одна операция за раз на строку: двойной клик по звезде иначе шлёт два запроса наперегонки.
  const [busyId, setBusyId] = useState<number | null>(null);

  const run = async (id: number, action: () => Promise<unknown>) => {
    setBusyId(id);
    try { await action(); } finally { setBusyId(null); }
  };

  const shell = (children: React.ReactNode) => (
    <div className={`${deep} relative min-h-screen overflow-x-clip pb-24 text-[var(--t-text)]`} style={{ background: 'var(--deep-hero-grad)' }}>
      <AppTopbar />
      {children}
      <UI_ModeToggle />
    </div>
  );

  if (auth.loading || (auth.isAuthenticated && fav.loading)) {
    return <div className="min-h-screen bg-[var(--deep-page-bg)]" />;
  }

  if (!auth.isAuthenticated) {
    return shell(
      <div className="flex min-h-[70vh] items-center justify-center px-4">
        <div className="deep-card w-full max-w-md text-center">
          <h1 className="deep-card-title mb-2">My favorites</h1>
          <p className="mb-4 text-sm text-[var(--t-text-2)]">Sign in to see and manage your favorites</p>
          <button
            type="button"
            onClick={openLoginModal}
            className="hp-mono rounded-[11px] bg-[var(--t-accent)] px-4 py-2 text-sm font-extrabold text-[var(--t-accent-ink)]"
          >
            Sign in
          </button>
        </div>
      </div>,
    );
  }

  const swimmers = sortByFavoriteRank(
    fav.favorites.filter((f) => f.target_type === 'swimmer' && f.swimmer_id != null),
    (f) => f.swimmer_id, fav.primarySwimmerId, fav.familySwimmerIds,
  );
  const clubs = fav.favorites.filter((f) => f.target_type === 'club' && f.club_id != null);

  const swimmerRow = (f: FavoriteDto) => {
    const busy = busyId === f.id;
    const isMe = f.is_primary;
    const isFamily = f.is_family;
    return (
      // На телефоне имя — отдельной строкой, кнопки под ним: три кнопки в строку оставляли
      // имени пару букв.
      <li key={f.id} className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-[var(--t-border)] py-3 last:border-b-0">
        <div className="min-w-0 basis-full sm:basis-0 sm:flex-1">
          <a
            href={routes.swimmer(f.swimmer_id!)}
            dir="auto"
            className="block truncate text-[15px] font-extrabold text-[var(--t-text)] no-underline hover:text-[var(--t-accent)]"
          >
            {f.swimmer_name || `#${f.swimmer_id}`}
          </a>
          <div className="text-[12px] font-bold text-[var(--t-text-3)]">
            {isMe ? 'This is me' : isFamily ? 'Family' : 'Favorite'}
          </div>
        </div>
        <IconToggle
          on={isMe}
          path={STAR}
          busy={busy}
          title={isMe ? 'This is me — click to unset' : 'Mark as me'}
          onClick={() => run(f.id, () => (isMe ? fav.unsetPrimary(f.id) : fav.setPrimary(f.id)))}
        />
        <IconToggle
          on={isFamily}
          path={HEART}
          busy={busy}
          title={isFamily ? 'Family — click to unmark' : 'Mark as family (shown first in start lists)'}
          onClick={() => run(f.id, () => fav.setFamily(f.id, !isFamily))}
        />
        <RemoveButton label={f.swimmer_name || 'swimmer'} busy={busy} onClick={() => run(f.id, () => fav.removeFavorite(f.id))} />
      </li>
    );
  };

  const clubRow = (f: FavoriteDto) => (
    <li key={f.id} className="flex items-center gap-3 border-b border-[var(--t-border)] py-3 last:border-b-0">
      <a
        href={routes.club(f.club_id!)}
        dir="auto"
        className="min-w-0 flex-1 truncate text-[15px] font-extrabold text-[var(--t-text)] no-underline hover:text-[var(--t-accent)]"
      >
        {f.club_name || `#${f.club_id}`}
      </a>
      <RemoveButton label={f.club_name || 'club'} busy={busyId === f.id} onClick={() => run(f.id, () => fav.removeFavorite(f.id))} />
    </li>
  );

  return shell(
    <main className="mx-auto w-full max-w-3xl px-4 pt-24">
      <p className="hp-mono text-[12px] font-extrabold uppercase tracking-wide text-[var(--t-text-3)]">
        My profile · {auth.displayName || auth.email}
      </p>
      <h1 className="mb-1 text-[28px] font-black text-[var(--t-text)]">My favorites</h1>
      <p className="mb-6 max-w-xl text-[13px] text-[var(--t-text-2)]">
        Swimmers you mark as <span className="font-extrabold text-[var(--deep-gold)]">family</span> come first —
        right after you — in start lists, head-to-head and your favorites card. It changes the order only; it
        gives no extra access.
      </p>

      <section className="deep-card mb-4">
        <div className="mb-2 flex items-baseline gap-2">
          <h2 className="deep-card-title">Swimmers</h2>
          <span className="deep-card-sub">{swimmers.length}</span>
        </div>
        {swimmers.length === 0 ? (
          <p className="py-3 text-[13px] text-[var(--t-text-2)]">
            No favorite swimmers yet — tap the heart next to a swimmer in results.
          </p>
        ) : (
          <ul className="m-0 list-none p-0">{swimmers.map(swimmerRow)}</ul>
        )}
      </section>

      <section className="deep-card">
        <div className="mb-2 flex items-baseline gap-2">
          <h2 className="deep-card-title">Clubs</h2>
          <span className="deep-card-sub">{clubs.length}</span>
        </div>
        {clubs.length === 0 ? (
          <p className="py-3 text-[13px] text-[var(--t-text-2)]">
            No followed clubs — use “Follow club” on a club page.
          </p>
        ) : (
          <ul className="m-0 list-none p-0">{clubs.map(clubRow)}</ul>
        )}
      </section>
    </main>,
  );
}

export default MyFavorites;

import React, { useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import { useAuth } from '../../hooks/useAuth';
import { useFavorites, favoriteLevelOf, type FavoriteDto, type FavoriteLevel } from '../../hooks/useFavorites';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import ConfirmDialog from '../components/confirm-dialog/confirm-dialog';
import { routes } from '../../utils/routes';
import { sortByFavoriteRank } from '../../utils/helpers/favorites-order';

/**
 * Страница «My favorites» — вариант 1b хендоффа `!design_handoff/design_handoff_my_favorites`
 * («один переключатель на пловца»): все пловцы одним плоским списком, у каждого — сегментный
 * переключатель уровня Me / Family / Favorite, иконка строки повторяет уровень.
 *
 * Уровни исключают друг друга (решение Влада 24.09.2026, вместе с этим хендоффом):
 * - **Me** (★, акцент) — один; новый «Me» опускает прежнего в Favorite, из семьи выходит;
 * - **Family** (золотое сердечко) — до `familyLimit.max` (4, FavoritesRules.MaxFamily), Me не
 *   в счёт; на пределе кнопка погашена с подсказкой;
 * - **Favorite** (красное сердечко) — все остальные.
 * Правило держит сервер (UserFavoriteRepository), клиент лишь меняет список сразу
 * (`useFavorites.setLevel`, откат — перечитать список). Прав уровни не дают — только порядок
 * (Me → семья → остальные) в стартовых протоколах, H2H и карточке избранного.
 *
 * Убрать из избранного — ТОЛЬКО через подтверждение (`ConfirmDialog`): имя пропадало из
 * списка сразу по клику, неожиданно и без отката (правка Влада 24.09.2026; хендофф это
 * допускает — «consider adding a confirmation»).
 */

/** Иконки уровней. Своих SVG нет в проекте в виде компонента — пути те же, что у UI_FavoriteControls. */
const STAR = 'M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z';
const HEART = 'M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z';

const LEVELS: Record<FavoriteLevel, { label: string; path: string; color: string; hint: string }> = {
  me: { label: 'Me', path: STAR, color: 'var(--t-level-me)', hint: 'This is me — only one; replaces the current one' },
  family: { label: 'Family', path: HEART, color: 'var(--t-level-family)', hint: 'Family — right after you in lists' },
  fav: { label: 'Favorite', path: HEART, color: 'var(--t-level-fav)', hint: 'Followed swimmer' },
};
const LEVEL_ORDER: FavoriteLevel[] = ['me', 'family', 'fav'];

/** Иконка уровня: залитая — у текущего уровня и в строке, контур — у неактивной кнопки. */
function LevelIcon({ level, filled, size }: { level: FavoriteLevel; filled: boolean; size: number }) {
  const { path, color } = LEVELS[level];
  return (
    <svg
      width={size} height={size} viewBox="0 0 24 24" aria-hidden="true"
      fill={filled ? color : 'none'} stroke={filled ? color : 'currentColor'}
      strokeWidth="1.9" strokeLinejoin="round" className="shrink-0"
    >
      <path d={path} />
    </svg>
  );
}

/**
 * Сегментный переключатель уровня. Телефон — сетка из трёх равных колонок во всю ширину
 * (кнопки ≥ 40px), с `sm` — компактная полоса справа в строке.
 * Погашенная Family — `aria-disabled`, а не `disabled`: у `disabled` title не всплывает.
 */
function LevelSwitch({ level, familyBlockedHint, onChange, label }: {
  level: FavoriteLevel;
  /** Семья заполнена — подсказка к погашенной кнопке; null — ставить можно. */
  familyBlockedHint: string | null;
  onChange: (next: FavoriteLevel) => void;
  /** Имя пловца — для подписи группы кнопок экранному чтецу. */
  label: string;
}) {
  return (
    <div
      role="radiogroup"
      aria-label={`Level for ${label}`}
      className="mt-1.5 grid grid-cols-3 gap-0.5 rounded-lg border border-[var(--t-border)] bg-[var(--t-surface-strong)] p-[3px] sm:mt-0 sm:flex sm:shrink-0"
    >
      {LEVEL_ORDER.map((k) => {
        const active = k === level;
        const blocked = k === 'family' && !active && familyBlockedHint != null;
        const color = LEVELS[k].color;
        return (
          <button
            key={k}
            type="button"
            role="radio"
            aria-checked={active}
            aria-disabled={blocked || undefined}
            title={blocked ? familyBlockedHint! : LEVELS[k].hint}
            onClick={() => { if (!active && !blocked) onChange(k); }}
            className={`flex min-h-10 items-center justify-center gap-[5px] rounded-md border px-2.5 text-[13px] transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--t-accent)] sm:min-h-0 sm:py-[5px] sm:text-[12px] ${
              active
                ? 'text-[var(--t-text)]'
                : blocked
                  ? 'cursor-not-allowed border-transparent text-[var(--t-text-3)] opacity-45'
                  : 'border-transparent text-[var(--t-text-3)] hover:bg-[var(--t-surface)] hover:text-[var(--t-text-2)]'
            }`}
            // Цвет уровня — переменной, поэтому style: Tailwind не соберёт класс из значения.
            style={active ? { borderColor: color, background: `color-mix(in oklch, ${color} 16%, transparent)` } : undefined}
          >
            <LevelIcon level={k} filled={active} size={14} />
            {LEVELS[k].label}
          </button>
        );
      })}
    </div>
  );
}

/** Кнопка «×» — убрать из избранного (через подтверждение). Цель касания 44px на телефоне. */
function RemoveButton({ onClick, label }: { onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      title="Remove from favorites"
      aria-label={`Remove ${label} from favorites`}
      onClick={onClick}
      className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg text-[var(--t-text-3)] transition-colors hover:bg-[var(--t-surface)] hover:text-[var(--t-danger)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--t-accent)] sm:h-8 sm:w-8"
    >
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
        <path d="M6 6l12 12M18 6L6 18" />
      </svg>
    </button>
  );
}

/** Эмблема клуба в круге 32px — на месте буквы-заглушки прототипа. */
function ClubAvatar({ name, id }: { name: string; id?: number | null }) {
  return (
    <span className="hidden h-8 w-8 shrink-0 items-center justify-center overflow-hidden rounded-full bg-[var(--t-surface-strong)] sm:flex">
      <UI_ClubIcon clubName={name} clubId={id} iconWidth="6" />
    </span>
  );
}

function MyFavorites() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();
  const deep = useDeepThemeClass();
  const fav = useFavorites();
  /** Кого убрать из избранного — открыт диалог подтверждения. */
  const [pendingRemove, setPendingRemove] = useState<FavoriteDto | null>(null);

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

  // Порядок Me → семья → остальные — общий хелпер сайта; после любой смены уровня список
  // пересортировывается сам, потому что уровень уже поменялся в состоянии хука.
  const swimmers = sortByFavoriteRank(
    fav.favorites.filter((f) => f.target_type === 'swimmer' && f.swimmer_id != null),
    (f) => f.swimmer_id, fav.primarySwimmerId, fav.familySwimmerIds,
  );
  const clubs = fav.favorites.filter((f) => f.target_type === 'club' && f.club_id != null);
  const familyMax = fav.familyLimit?.max;
  const familyBlockedHint = fav.familyFull
    ? fav.familyLimit?.fullHint ?? `Family is full (${familyMax}/${familyMax})`
    : null;
  const meCount = fav.primarySwimmerId != null ? 1 : 0;
  const counter = (
    <>
      Me {meCount}/1
      {familyMax != null && (
        <>
          {' · '}
          {/* Заполненная семья — золотом: подсказывает, почему Family погашена. */}
          <span style={fav.familyFull ? { color: 'var(--t-level-family)' } : undefined}>
            Family {fav.familySwimmerIds.size}/{familyMax}
          </span>
        </>
      )}
    </>
  );

  const swimmerRow = (f: FavoriteDto) => {
    const level = favoriteLevelOf(f);
    const name = f.swimmer_name || `#${f.swimmer_id}`;
    return (
      // Телефон: [иконка | имя/клуб | ×], под ними переключатель во всю ширину.
      // С `sm` — всё в один ряд, имя забирает остаток.
      <li key={f.id} className="border-b border-[var(--t-border)] pt-2.5 pb-3 last:border-b-0 sm:flex sm:items-center sm:gap-3 sm:py-2.5">
        <div className="flex min-w-0 items-center gap-2.5 sm:flex-1 sm:gap-3">
          <span className="flex w-5 shrink-0 justify-center" title={LEVELS[level].label}>
            <LevelIcon level={level} filled size={18} />
          </span>
          <ClubAvatar name={f.swimmer_club_name ?? ''} id={f.swimmer_club_id} />
          <div className="min-w-0 flex-1">
            <a
              href={routes.swimmer(f.swimmer_id!)}
              dir="auto"
              className="block truncate text-left text-[15px] font-medium text-[var(--t-text)] no-underline hover:text-[var(--t-accent)] sm:text-[14px]"
            >
              {name}
            </a>
            {f.swimmer_club_name && (
              <div dir="auto" className="truncate text-left text-[12px] text-[var(--t-text-3)]">
                {f.swimmer_club_name}
              </div>
            )}
          </div>
          {/* На телефоне × стоит в первой линии, с `sm` — после переключателя. */}
          <span className="sm:hidden"><RemoveButton label={name} onClick={() => setPendingRemove(f)} /></span>
        </div>
        <LevelSwitch
          level={level}
          label={name}
          familyBlockedHint={familyBlockedHint}
          onChange={(next) => { void fav.setLevel(f.id, next); }}
        />
        <span className="hidden sm:block"><RemoveButton label={name} onClick={() => setPendingRemove(f)} /></span>
      </li>
    );
  };

  const clubRow = (f: FavoriteDto) => {
    const name = f.club_name || `#${f.club_id}`;
    return (
      <li key={f.id} className="flex items-center gap-2.5 border-b border-[var(--t-border)] py-1.5 last:border-b-0 sm:gap-3 sm:py-2.5">
        {/* У клуба уровень один — Favorite. */}
        <span className="flex w-5 shrink-0 justify-center"><LevelIcon level="fav" filled size={18} /></span>
        <ClubAvatar name={f.club_name ?? ''} id={f.club_id} />
        <a
          href={routes.club(f.club_id!)}
          dir="auto"
          className="min-w-0 flex-1 truncate text-left text-[15px] font-medium text-[var(--t-text)] no-underline hover:text-[var(--t-accent)] sm:text-[14px]"
        >
          {name}
        </a>
        <RemoveButton label={name} onClick={() => setPendingRemove(f)} />
      </li>
    );
  };

  return shell(
    <main className="mx-auto w-full max-w-[808px] px-4 pt-24">
      <p className="flex items-center gap-1 text-[13px] text-[var(--t-text-3)]">
        <span aria-hidden="true">‹</span> My profile · <bdi>{auth.displayName || auth.email}</bdi>
      </p>
      <h1 className="mt-1.5 mb-1 text-[26px] font-medium text-[var(--t-text)]">My favorites</h1>
      {/* Счётчик уровней: на телефоне — под заголовком, с `sm` — в шапке карточки. */}
      <p className="mb-4 text-[12px] text-[var(--t-text-3)] sm:hidden">{counter}</p>

      {/* Телефон — без карточки, строки прямо по фону (как в прототипе); с `sm` — карточка. */}
      <section className="sm:rounded-[14px] sm:border sm:border-[var(--t-border)] sm:bg-[var(--t-card)] sm:p-7 sm:mt-5">
        <div className="mb-3 hidden items-baseline justify-between sm:flex">
          <h2 className="m-0 text-[18px] font-medium">
            Swimmers <span className="text-[14px] text-[var(--t-text-3)]">{swimmers.length}</span>
          </h2>
          <span className="text-[12px] text-[var(--t-text-3)]">{counter}</span>
        </div>
        {swimmers.length === 0 ? (
          <p className="py-3 text-[13px] text-[var(--t-text-2)]">
            No favorite swimmers yet — tap the heart next to a swimmer in{' '}
            <a href={routes.results()} className="text-[var(--t-accent)]">results</a>.
          </p>
        ) : (
          <ul className="m-0 list-none p-0">{swimmers.map(swimmerRow)}</ul>
        )}

        <h2 className="mt-7 mb-1 text-[17px] font-medium sm:mt-9 sm:mb-2 sm:text-[18px]">
          Clubs <span className="text-[13px] text-[var(--t-text-3)] sm:text-[14px]">{clubs.length}</span>
        </h2>
        {clubs.length === 0 ? (
          <p className="py-3 text-[13px] text-[var(--t-text-2)]">
            No followed clubs — use “Follow club” on a club page.
          </p>
        ) : (
          <ul className="m-0 list-none p-0">{clubs.map(clubRow)}</ul>
        )}
      </section>

      {pendingRemove && (
        <ConfirmDialog
          // Имя — в <bdi>: иврит внутри английской фразы иначе разворачивает кавычки.
          title={<>Remove “<bdi>{removeName(pendingRemove)}</bdi>” from favorites?</>}
          confirmLabel="Remove"
          busyLabel="Removing…"
          onConfirm={async () => {
            const ok = await fav.removeFavorite(pendingRemove.id);
            if (ok) setPendingRemove(null);
            return { success: ok, error: 'Could not remove — please try again.' };
          }}
          onClose={() => setPendingRemove(null)}
        >
          <p className="mt-3 text-[13px] text-[var(--t-text-2)]">
            {favoriteLevelOf(pendingRemove) === 'family' && (
              <>They are marked as <span style={{ color: 'var(--t-level-family)' }}>family</span> — the mark goes too. </>
            )}
            {pendingRemove.is_primary && <>This is marked as you — that mark goes too. </>}
            {pendingRemove.target_type === 'club'
              ? 'You can follow the club again from its page.'
              : 'You can add them back any time with the heart next to their name in results.'}
          </p>
        </ConfirmDialog>
      )}
    </main>,
  );
}

/** Имя для заголовка диалога: пловец или клуб, без имени — номер. */
function removeName(f: FavoriteDto): string {
  return f.target_type === 'club'
    ? f.club_name || `#${f.club_id}`
    : f.swimmer_name || `#${f.swimmer_id}`;
}

export default MyFavorites;

import React, { useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import { useAuth } from '../../hooks/useAuth';
import { useFavorites, type FavoriteDto } from '../../hooks/useFavorites';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import UI_FavoriteControls from '../components/mix/favorite-controls/favorite-controls';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import ConfirmDialog from '../components/confirm-dialog/confirm-dialog';
import { routes } from '../../utils/routes';
import { sortByFavoriteRank } from '../../utils/helpers/favorites-order';

/**
 * Страница «My favorites» (docs/plans/family-favorites-plan.md, решения Влада 24.09.2026):
 * всё своё избранное в одном месте — пловцы и клубы, «это я» и пометка «семья».
 *
 * Строка устроена как на остальном сайте: слева сердечко `UI_FavoriteControls` — красное, у
 * семьи золотое. Справа — звезда «это я», кнопка Family (делает сердечко золотым) и Remove.
 * Семья — не больше `familyLimit.max` (4, FavoritesRules.MaxFamily на сервере).
 *
 * Убрать из избранного — ТОЛЬКО через подтверждение (`ConfirmDialog`), и с сердечка, и с
 * Remove: имя пропадало из списка сразу по клику, неожиданно и без отката (правка Влада
 * 24.09.2026).
 *
 * «Семья» ставится ТОЛЬКО здесь: на остальных экранах кнопки нет, там семья лишь поднимает
 * пловца в списке (Me → семья → остальные). Прав пометка не даёт — ни семья, ни «это я».
 */

/**
 * Сетка строки пловца — одна на строки и на строку-заголовок над ними: колонки кнопок одной
 * ширины во всех строках, поэтому счётчик «Family N/4» стоит ровно над колонкой Family
 * (правка Влада 24.09.2026). Телефон: имя — отдельной строкой во всю ширину, под ним
 * [звезда | Family | Remove] плотнее (влезает с 320px); с `sm` — всё в ряд, имя забирает остаток.
 */
// ⚠ Все колонки кнопок — ФИКСИРОВАННОЙ ширины: в строке-заголовке они пустые, и колонка
// «по содержимому» там схлопнулась бы — сетка заголовка разъехалась бы со строками.
const SWIMMER_GRID = 'grid grid-cols-[32px_96px_88px] gap-x-2 sm:grid-cols-[minmax(0,1fr)_32px_104px_88px] sm:gap-x-3 items-center';

const STAR = 'M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z';

// Звезда — те же цвета, что у UI_FavoriteControls: включённая в золоте, выключенная — контур.
const GOLD_FILL = '#f5b800';
const GOLD_STROKE = '#d99a00';
const OFF_STROKE = '#9aa3af';

function MeToggle({ on, onClick, busy }: { on: boolean; onClick: () => void; busy: boolean }) {
  return (
    <button
      type="button"
      title={on ? 'This is me — click to unset' : 'Mark as me'}
      aria-pressed={on}
      disabled={busy}
      onClick={onClick}
      className="flex h-8 w-8 items-center justify-center rounded-full border border-[var(--t-border)] bg-[var(--t-surface)] transition-transform hover:scale-110 disabled:opacity-40"
    >
      <svg width="16" height="16" viewBox="0 0 24 24" fill={on ? GOLD_FILL : 'none'} stroke={on ? GOLD_STROKE : OFF_STROKE} strokeWidth="1.9" strokeLinejoin="round">
        <path d={STAR} />
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
      className="hp-mono flex h-8 w-full items-center justify-center rounded-full border border-[var(--t-border)] px-3 text-[12px] font-extrabold text-[var(--t-text-2)] hover:border-[var(--t-danger)] hover:text-[var(--t-danger)] disabled:opacity-40"
    >
      Remove
    </button>
  );
}

/**
 * Кнопка «Family»: включённая — золотая (как сердечко семьи), выключенная — контурная.
 * Семья заполнена — выключенная погашена `aria-disabled` с подсказкой в title (у `disabled`
 * title не всплывает). Снять пометку можно всегда.
 */
function FamilyToggle({ on, onClick, busy, blockedHint }: {
  on: boolean; onClick: () => void; busy: boolean; blockedHint: string | null;
}) {
  const blocked = !on && blockedHint != null;
  return (
    <button
      type="button"
      title={blocked ? blockedHint! : on ? 'Family — click to unmark' : 'Make family: gold heart, shown first'}
      aria-pressed={on}
      aria-disabled={blocked || undefined}
      disabled={busy}
      onClick={() => { if (!blocked) onClick(); }}
      className={`hp-mono flex h-8 w-full items-center justify-center gap-1.5 rounded-full border px-3 text-[12px] font-extrabold transition-colors disabled:opacity-40 ${
        on
          ? 'border-[var(--deep-gold-border)] bg-[var(--deep-gold-chip)] text-[var(--deep-gold)]'
          : blocked
            ? 'cursor-not-allowed border-[var(--t-border)] text-[var(--t-text-3)] opacity-50'
            : 'border-[var(--t-border)] text-[var(--t-text-2)] hover:border-[var(--deep-gold-border)] hover:text-[var(--deep-gold)]'
      }`}
    >
      {on ? '✓ Family' : 'Family'}
    </button>
  );
}

function MyFavorites() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();
  const deep = useDeepThemeClass();
  const fav = useFavorites();
  // Одна операция за раз на строку: двойной клик иначе шлёт два запроса наперегонки.
  const [busyId, setBusyId] = useState<number | null>(null);
  /** Кого убрать из избранного — открыт диалог подтверждения. */
  const [pendingRemove, setPendingRemove] = useState<FavoriteDto | null>(null);

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
  const familyMax = fav.familyLimit?.max;
  const familyBlockedHint = fav.familyFull ? fav.familyLimit?.fullHint ?? null : null;

  const swimmerRow = (f: FavoriteDto) => {
    const busy = busyId === f.id;
    const isMe = f.is_primary;
    const isFamily = f.is_family;
    return (
      // На телефоне кнопки уходят под имя: в одну строку имени оставалась пара букв.
      <li key={f.id} className={`${SWIMMER_GRID} gap-y-2 border-b border-[var(--t-border)] py-3 last:border-b-0`}>
        <div className="col-span-3 flex min-w-0 items-center gap-3 sm:col-span-1">
          {/* Сердечко как на всём сайте: красное / золотое у семьи. Клик — не удаление, а
              тот же диалог подтверждения, что у Remove. */}
          <UI_FavoriteControls
            swimmerId={f.swimmer_id}
            isFavorite
            isFamily={isFamily}
            showPrimary={false}
            onToggleFavorite={() => setPendingRemove(f)}
          />
          <div className="min-w-0">
            <a
              href={routes.swimmer(f.swimmer_id!)}
              dir="auto"
              className="block truncate text-[15px] font-extrabold text-[var(--t-text)] no-underline hover:text-[var(--t-accent)]"
            >
              {f.swimmer_name || `#${f.swimmer_id}`}
            </a>
            {/* Клуб пловца — эмблема (ведёт на клуб) и название. Подписи «Family/Favorite»
                здесь больше нет: её и так говорят сердечко и кнопка Family. */}
            {f.swimmer_club_name && (
              <div className="mt-0.5 flex min-w-0 items-center gap-1.5">
                <UI_ClubIcon clubName={f.swimmer_club_name} clubId={f.swimmer_club_id} iconWidth="6" className="shrink-0" />
                <span dir="auto" className="truncate text-[12px] font-bold text-[var(--t-text-3)]">
                  {f.swimmer_club_name}
                </span>
              </div>
            )}
            {isMe && <div className="text-[12px] font-bold text-[var(--deep-gold)]">This is me</div>}
          </div>
        </div>
        <MeToggle
          on={isMe}
          busy={busy}
          onClick={() => run(f.id, () => (isMe ? fav.unsetPrimary(f.id) : fav.setPrimary(f.id)))}
        />
        <FamilyToggle
          on={isFamily}
          busy={busy}
          blockedHint={familyBlockedHint}
          onClick={() => run(f.id, () => fav.setFamily(f.id, !isFamily))}
        />
        <RemoveButton label={f.swimmer_name || 'swimmer'} busy={busy} onClick={() => setPendingRemove(f)} />
      </li>
    );
  };

  const clubRow = (f: FavoriteDto) => (
    // Как у пловцов: на телефоне сердечко, эмблема и название — первой строкой во всю ширину,
    // Remove под ними; в один ряд названию оставалась одна буква.
    <li key={f.id} className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-[var(--t-border)] py-3 last:border-b-0">
      <div className="flex min-w-0 basis-full items-center gap-3 sm:basis-0 sm:flex-1">
        <UI_FavoriteControls
          swimmerId={f.club_id}
          isFavorite
          showPrimary={false}
          onToggleFavorite={() => setPendingRemove(f)}
        />
        <UI_ClubIcon clubName={f.club_name ?? ''} clubId={f.club_id} iconWidth="8" className="shrink-0" />
        <a
          href={routes.club(f.club_id!)}
          dir="auto"
          className="min-w-0 flex-1 truncate text-[15px] font-extrabold text-[var(--t-text)] no-underline hover:text-[var(--t-accent)]"
        >
          {f.club_name || `#${f.club_id}`}
        </a>
      </div>
      {/* Та же ширина, что у колонки Remove в строках пловцов (кнопка — w-full). */}
      <div className="w-[88px] shrink-0">
        <RemoveButton label={f.club_name || 'club'} busy={busyId === f.id} onClick={() => setPendingRemove(f)} />
      </div>
    </li>
  );

  return shell(
    <main className="mx-auto w-full max-w-3xl px-4 pt-24">
      <p className="hp-mono text-[12px] font-extrabold uppercase tracking-wide text-[var(--t-text-3)]">
        My profile · {auth.displayName || auth.email}
      </p>
      <h1 className="mb-1 text-[28px] font-black text-[var(--t-text)]">My favorites</h1>
      <p className="mb-6 max-w-xl text-[13px] leading-relaxed text-[var(--t-text-2)]">
        Tap <span className="font-extrabold text-[var(--deep-gold)]">Family</span> to turn the heart gold: family
        comes first — right after you — in start lists, head-to-head and your favorites card.
        {familyMax != null && <> Up to <span className="font-extrabold">{familyMax}</span> family members.</>}
        {' '}It changes the order only; it gives no extra access.
      </p>

      <section className="deep-card mb-4">
        <div className="mb-2 flex items-baseline gap-2">
          <h2 className="deep-card-title">Swimmers</h2>
          <span className="deep-card-sub">{swimmers.length}</span>
        </div>
        {familyMax != null && swimmers.length > 0 && (
          // Строка-заголовок колонок: та же сетка, что у строк, счётчик — в колонке Family.
          <div className={SWIMMER_GRID}>
            {/* Цвет заполненной семьи — style, а не класс: `.deep-card-sub` задаёт свой цвет
                и перебивает утилиту той же специфичности. */}
            <span
              className="deep-card-sub col-start-2 text-center sm:col-start-3"
              style={fav.familyFull ? { color: 'var(--deep-gold)' } : undefined}
              title={fav.familyFull ? familyBlockedHint ?? undefined : undefined}
            >
              Family {fav.familySwimmerIds.size}/{familyMax}
            </span>
          </div>
        )}
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
            {pendingRemove.is_family && (
              <>They are marked as <span className="font-extrabold text-[var(--deep-gold)]">family</span> — the mark goes too. </>
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

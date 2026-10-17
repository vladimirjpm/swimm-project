import React, { useMemo, useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import '../components/mix/h2h/h2h.css';
import { useAuth } from '../../hooks/useAuth';
import { useFavorites } from '../../hooks/useFavorites';
import {
  useProfileSetup,
  SURVEY_ROLES,
  STEP_TITLES,
  ROLE_LABELS,
  type ProfileSetupStep,
  type ProfileSetupStepKey,
  type SurveyRole,
} from '../../hooks/useProfileSetup';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import UI_H2HPickerModal from '../components/mix/h2h/h2h-picker-modal';
import UI_H2HRivalPicker from '../components/mix/h2h/h2h-rival-picker';
import { useSwimmerSearch } from '../swimmer-project/use-swimmer-page';
import { routes } from '../../utils/routes';

/**
 * Страница «Set up profile» — хендофф `!design_handoff/set-up-profile` вариант 1a,
 * план `docs/plans/profile-setup-plan.md` §5.
 *
 * Решения Влада 16.10.2026:
 * - шагов ровно три и всегда одни и те же: «это я», клуб, группа;
 * - опрос «Who are you?» ТОЛЬКО сохраняется — на шаги, счётчик и права не влияет;
 *   «Just watching» исключающий (правило — `toggleSurveyRole`);
 * - заявка в группу (pending) засчитывает шаг;
 * - Dismiss — ссылкой внизу, пункт меню после него не показывается;
 * - (17.10.2026) от шага можно отказаться: «Not a swimmer», «No club», «No group» — отказ
 *   засчитывает шаг и снимается кнопкой Undo. Тренер отмечает только клуб, родитель — без «это я».
 * - (17.10.2026, хендофф `set-up-profile-ex3`) «I'm a parent» добавляет шаг «My family»: найти
 *   детей поиском и отметить семьёй (золотое сердечко, до `familyLimit.max`). Других шагов по
 *   ролям нет. Сняли роль — шаг пропал, семья осталась.
 *
 * Тема — deep, как у соседних личных страниц (/my-favorites, /my-media): токены хендоффа
 * `--theme-mode-*` переложены на роли `--t-*` (client/CLAUDE.md «Роли --t-*»).
 * «Это я» прав не даёт — недоверенная пометка (правило «primary favorite недоверенный»).
 */

// ── Иконки (Phosphor в проекте не подключён — свои SVG, контур 1.8) ─────────────

const STAR = 'M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z';
const HEART = 'M12 20.5s-7.5-4.6-9.3-9.1C1.4 8 3.6 4.5 7 4.5c2.1 0 3.5 1.1 5 2.9 1.5-1.8 2.9-2.9 5-2.9 3.4 0 5.6 3.5 4.3 6.9-1.8 4.5-9.3 9.1-9.3 9.1z';

function Svg({ size, children, fill }: { size: number; children: React.ReactNode; fill?: string }) {
  return (
    <svg
      width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" className="shrink-0"
      fill={fill ?? 'none'} stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"
    >
      {children}
    </svg>
  );
}

const ROLE_META: Record<SurveyRole, { label: string; icon: React.ReactNode }> = {
  swimmer: {
    label: 'I swim',
    icon: (
      <Svg size={16}>
        <circle cx="16" cy="5.5" r="2" />
        <path d="M4 11l5-2.5 3.5 3L15 10M2 17c1.7 0 1.7-1.2 3.3-1.2S7 17 8.7 17s1.7-1.2 3.3-1.2S13.7 17 15.3 17s1.7-1.2 3.4-1.2S20.3 17 22 17" />
      </Svg>
    ),
  },
  parent: {
    label: "I'm a parent",
    icon: (
      <Svg size={16}>
        <circle cx="8" cy="6" r="2.6" />
        <circle cx="17" cy="9.5" r="1.9" />
        <path d="M3.5 20v-2.5A4.5 4.5 0 0 1 12.5 17.5V20M13.8 20v-1.6a3.2 3.2 0 0 1 6.4 0V20" />
      </Svg>
    ),
  },
  coach: {
    label: "I'm a coach",
    icon: (
      <Svg size={16}>
        <rect x="5" y="4" width="14" height="17" rx="2" />
        <path d="M9 4V3h6v1M8.5 10h7M8.5 14h7M8.5 18h4" />
      </Svg>
    ),
  },
  watcher: {
    label: 'Just watching',
    icon: (
      <Svg size={16}>
        <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" />
        <circle cx="12" cy="12" r="3" />
      </Svg>
    ),
  },
};

// `skip` — подпись кнопки отказа от шага (решение Влада 17.10.2026): отказ засчитывает шаг.
const STEP_META: Record<ProfileSetupStepKey, { title: string; hint: string; cta: string; skip: string; icon: React.ReactNode }> = {
  me: {
    title: STEP_TITLES.me,
    hint: 'Find your swimmer record',
    cta: 'Find me',
    skip: 'Not a swimmer',
    // Золотая звезда «это я» — та же, что на /my-favorites (решение 24.09.2026).
    icon: <span style={{ color: 'var(--t-level-me)' }}><Svg size={18} fill="currentColor"><path d={STAR} /></Svg></span>,
  },
  family: {
    title: STEP_TITLES.family,
    // Число — из лимита сервера (`familyHint` ниже); здесь запасной текст, пока лимит не приехал.
    hint: 'Mark your children · Family favorite',
    cta: 'Add children',
    skip: 'Skip',
    // Золотое сердечко «семья» — то же, что на /my-favorites.
    icon: <span style={{ color: 'var(--t-level-family)' }}><Svg size={18} fill="currentColor"><path d={HEART} /></Svg></span>,
  },
  club: {
    title: STEP_TITLES.club,
    hint: 'Club you or your child swim for',
    cta: 'Choose club',
    skip: 'No club',
    icon: (
      <Svg size={18}>
        <path d="M12 3l7 3v5.5c0 4.4-3 7.9-7 9.5-4-1.6-7-5.1-7-9.5V6l7-3z" />
      </Svg>
    ),
  },
  group: {
    title: STEP_TITLES.group,
    hint: 'Training group · coach approves',
    cta: 'Join group',
    skip: 'No group',
    icon: (
      <Svg size={18}>
        <circle cx="9" cy="8" r="3" />
        <path d="M3.5 19.5v-1A4.5 4.5 0 0 1 8 14h2a4.5 4.5 0 0 1 4.5 4.5v1M16 5.2a3 3 0 0 1 0 5.6M18 14.2a4.5 4.5 0 0 1 2.5 4.3v1" />
      </Svg>
    ),
  },
};

/** Итог (2a): подпись строки и ссылка на выбранное. */
const RECAP: Record<ProfileSetupStepKey, { label: string; open: string }> = {
  me: { label: 'Me', open: 'My swims →' },
  family: { label: 'Family', open: 'Edit family →' },
  club: { label: 'Club', open: 'Open club →' },
  group: { label: 'Group', open: 'Open group →' },
};

/** Порядок строк итога: семья — после клуба, перед группой (хендофф ex3), в чек-листе — второй. */
const RECAP_ORDER: ProfileSetupStepKey[] = ['me', 'club', 'family', 'group'];

const CHECK = (
  <Svg size={16}>
    <path d="M5 12.5l4.5 4.5L19 7.5" />
  </Svg>
);

// ── Окно выбора клуба ────────────────────────────────────────────────────────

interface ClubOption { id: number; name: string }

/**
 * Клубы приезжают ВСЕ одним списком (`/api/me/hub-groups/clubs`, около полутора сотен) и
 * фильтруются на месте — как страны в `RcCountryPicker`; классы те же, что у пикера пловцов.
 */
function ClubPicker({ onPick }: { onPick: (id: number) => void }) {
  const [clubs, setClubs] = useState<ClubOption[] | null>(null);
  const [error, setError] = useState(false);
  const [query, setQuery] = useState('');

  React.useEffect(() => {
    let alive = true;
    fetch('/api/me/hub-groups/clubs', { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
      .then((list: ClubOption[]) => { if (alive) setClubs(list.filter((c) => c.name)); })
      .catch(() => { if (alive) setError(true); });
    return () => { alive = false; };
  }, []);

  const q = query.trim().toLowerCase();
  const hits = clubs?.filter((c) => !q || c.name.toLowerCase().includes(q)) ?? null;

  return (
    <div className="h2h-picker">
      <input
        className="h2h-search"
        type="search"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search a club..."
        aria-label="Search a club"
      />
      <div className="h2h-hits">
        {error ? (
          <div className="h2h-hint">Could not load clubs right now.</div>
        ) : !hits ? (
          <div className="h2h-hint">Loading…</div>
        ) : hits.length === 0 ? (
          <div className="h2h-hint">No club found.</div>
        ) : (
          hits.map((c) => (
            <button key={c.id} type="button" className="h2h-hit" onClick={() => onPick(c.id)}>
              <span className="h2h-hit__name" dir="auto">{c.name}</span>
            </button>
          ))
        )}
      </div>
    </div>
  );
}

// ── Страница ─────────────────────────────────────────────────────────────────

type Picker = 'me' | 'family' | 'club' | null;

const SAVE_FAILED = 'Could not save — please try again.';

function SetupProfile() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();
  const deep = useDeepThemeClass();
  const { setup, refresh, toggleRole, setStepSkipped, dismiss } = useProfileSetup();
  const fav = useFavorites();

  const [picker, setPicker] = useState<Picker>(null);
  const [search, setSearch] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const found = useSwimmerSearch(picker === 'me' || picker === 'family' ? search : '');

  const favoriteSwimmers = useMemo(
    () => fav.favorites
      .filter((f) => f.target_type === 'swimmer' && f.swimmer_id != null)
      .map((f) => ({ id: f.swimmer_id as number, name: f.swimmer_name || `#${f.swimmer_id}` })),
    [fav.favorites],
  );

  // Семья (шаг «My family»): уже отмеченные — плашками сверху окна; в полосе и выдаче — все,
  // кроме них и «это я» (семья сняла бы звезду «Me»: уровни исключают друг друга).
  const familyMembers = useMemo(
    () => fav.favorites.filter((f) => f.target_type === 'swimmer' && f.is_family && f.swimmer_id != null),
    [fav.favorites],
  );
  const familyCandidates = favoriteSwimmers.filter(
    (f) => f.id !== fav.primarySwimmerId && !fav.familySwimmerIds.has(f.id));
  const familyHits = found.data?.filter(
    (h) => h.id !== fav.primarySwimmerId && !fav.familySwimmerIds.has(h.id)) ?? null;

  const shell = (children: React.ReactNode) => (
    <div className={`${deep} relative min-h-screen overflow-x-clip pb-24 text-[var(--t-text)]`} style={{ background: 'var(--deep-hero-grad)' }}>
      <AppTopbar />
      {children}
      <UI_ModeToggle />
    </div>
  );

  if (auth.loading || (auth.isAuthenticated && !setup)) {
    return <div className="min-h-screen bg-[var(--deep-page-bg)]" />;
  }

  if (!auth.isAuthenticated) {
    return shell(
      <div className="flex min-h-[70vh] items-center justify-center px-4">
        <div className="deep-card w-full max-w-md text-center">
          <h1 className="deep-card-title mb-2">Set up your profile</h1>
          <p className="mb-4 text-sm text-[var(--t-text-2)]">Sign in to add yourself, your club and group</p>
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

  const s = setup!;

  /** Действие шага: ошибка — строкой под шагами, успех — перечитать статус (меню тоже). */
  const run = async (action: () => Promise<string | null>) => {
    setBusy(true);
    setError(null);
    try {
      const err = await action();
      if (err) setError(err);
      await refresh();
    } finally {
      setBusy(false);
    }
  };

  const pickMe = (swimmerId: number) => {
    setPicker(null);
    setSearch('');
    void run(async () => {
      const existing = fav.favorites.find((f) => f.target_type === 'swimmer' && f.swimmer_id === swimmerId);
      // setMeBySwimmer — переключатель: по уже отмеченному «это я» он бы СНЯЛ звезду.
      if (existing?.is_primary) return null;
      if (!existing && fav.isFull('swimmer')) return fav.fullHint('swimmer');
      await fav.setMeBySwimmer(swimmerId);
      return null;
    });
  };

  const pickClub = (clubId: number) => {
    setPicker(null);
    void run(async () => {
      // toggleFavoriteClub — переключатель: уже избранный клуб он бы убрал.
      if (fav.favoriteClubIds.has(clubId)) return null;
      if (fav.isFull('club')) return fav.fullHint('club');
      await fav.toggleFavoriteClub(clubId);
      return null;
    });
  };

  /**
   * Отметить ребёнка семьёй: не в избранном — сначала добавить (семья — пометка на избранном),
   * потом поставить сердечко. Лимит семьи и общий лимит избранного — текстом из ответа.
   */
  const markFamily = (swimmerId: number) => {
    setSearch('');
    void run(async () => {
      let target = fav.favorites.find((f) => f.target_type === 'swimmer' && f.swimmer_id === swimmerId);
      if (target?.is_family) return null;
      if (fav.familyFull) return fav.familyLimit?.fullHint || SAVE_FAILED;
      if (!target) {
        if (fav.isFull('swimmer')) return fav.fullHint('swimmer');
        const added = await fav.addFavoriteSwimmer(swimmerId);
        if (!added) return SAVE_FAILED;
        target = added;
      }
      return (await fav.setFamily(target.id, true)) ? null : fav.familyLimit?.fullHint || SAVE_FAILED;
    });
  };

  /** Снять сердечко семьи: пловец остаётся в обычном избранном. */
  const unmarkFamily = (favoriteId: number) => {
    void run(async () => ((await fav.setFamily(favoriteId, false)) ? null : SAVE_FAILED));
  };

  const closeFamily = () => {
    setPicker(null);
    setSearch('');
    setError(null);
  };

  /** Подсказка шага: у семьи — число из лимита сервера; клуб тренера — «Club you coach at» (ex3). */
  const hintFor = (step: ProfileSetupStep): string => {
    if (step.key === 'family' && fav.familyLimit) {
      return `Mark your children · Family favorite, up to ${fav.familyLimit.max}`;
    }
    if (step.key === 'club' && s.survey.coach && !s.survey.swimmer && !s.survey.parent) return 'Club you coach at';
    return STEP_META[step.key].hint;
  };

  const stepAction = (step: ProfileSetupStep) => {
    if (step.key === 'me') setPicker('me');
    else if (step.key === 'family') setPicker('family');
    else if (step.key === 'club') setPicker('club');
    // Публичный список групп не несёт id, а вступают на странице группы — туда и ведём.
    else window.location.href = routes.groupsList();
  };

  const targetHref = (step: ProfileSetupStep): string | null => {
    // Семья — несколько пловцов: ссылки нет, её строка открывает окно семьи.
    if (step.targetId == null || step.key === 'family') return null;
    if (step.key === 'me') return routes.swimmer(step.targetId);
    if (step.key === 'club') return routes.club(step.targetId);
    return step.targetSlug ? routes.group(step.targetSlug) : null;
  };

  /** Отказ от шага или его отмена; статус (и счётчик в меню) перечитывает хук. */
  const skipStep = (step: ProfileSetupStep, skipped: boolean) => {
    setBusy(true);
    setError(null);
    void setStepSkipped(step.key, skipped)
      .then((ok) => { if (!ok) setError('Could not save — please try again.'); })
      .finally(() => setBusy(false));
  };

  const stepRow = (step: ProfileSetupStep) => {
    const meta = STEP_META[step.key];
    const href = targetHref(step);
    return (
      <li key={step.key} className="flex items-center gap-[14px] border-t border-[var(--t-border)] py-3">
        <span
          className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full border ${
            step.done && !step.skipped ? 'border-solid border-[var(--t-accent)]' : 'border-dashed border-[var(--t-border)]'
          } text-[var(--t-text-2)]`}
        >
          {meta.icon}
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5 text-[14px] font-bold">
            {meta.title}
            {/* Шаг, которого человек ещё не видел (хендофф ex2, 3b): его и привёл пункт «New: …». */}
            {s.newSteps.includes(step.key) && (
              <span className="hp-mono rounded-[6px] bg-[color-mix(in_srgb,var(--t-warn)_18%,transparent)] px-1.5 py-px text-[10px] font-extrabold text-[var(--t-text)]">
                NEW
              </span>
            )}
          </div>
          {step.done && step.targetName ? (
            <div className="flex min-w-0 items-center gap-1.5 text-[12px] text-[var(--t-text-2)]">
              {href ? (
                <a href={href} dir="auto" className="truncate text-[var(--t-text-2)] no-underline hover:text-[var(--t-accent)]">
                  {step.targetName}
                </a>
              ) : step.key === 'family' ? (
                <button
                  type="button"
                  dir="auto"
                  onClick={() => setPicker('family')}
                  title="Edit family"
                  className="truncate text-left text-[var(--t-text-2)] hover:text-[var(--t-accent)]"
                >
                  {step.targetName}
                </button>
              ) : (
                <span dir="auto" className="truncate">{step.targetName}</span>
              )}
              {step.status === 'pending' && (
                <span className="hp-mono shrink-0 rounded-[6px] border border-[var(--t-border)] px-1.5 py-px text-[10px] font-extrabold text-[var(--t-text-2)]">
                  PENDING
                </span>
              )}
            </div>
          ) : step.skipped ? (
            // Отказ: шаг засчитан, вернуть можно в один клик.
            <div className="flex items-center gap-1.5 text-[12px] text-[var(--t-text-2)]">
              <span>{meta.skip}</span>
              <span aria-hidden="true">·</span>
              <button
                type="button"
                disabled={busy}
                onClick={() => skipStep(step, false)}
                className="font-bold text-[var(--t-accent)] hover:underline disabled:opacity-50"
              >
                Undo
              </button>
            </div>
          ) : (
            <div className="flex flex-wrap items-center gap-x-1.5 text-[12px] text-[var(--t-text-2)]">
              <span>{hintFor(step)}</span>
              <span aria-hidden="true">·</span>
              <button
                type="button"
                disabled={busy}
                onClick={() => skipStep(step, true)}
                title="Skip this step — it counts as done"
                className="font-bold text-[var(--t-text-2)] underline-offset-2 hover:text-[var(--t-accent)] hover:underline disabled:opacity-50"
              >
                {meta.skip}
              </button>
            </div>
          )}
        </div>
        {step.skipped ? (
          <span className="shrink-0 text-[13px] font-bold text-[var(--t-text-2)]">Skipped</span>
        ) : step.done ? (
          <span className="flex shrink-0 items-center gap-1 text-[13px] font-bold text-[var(--t-accent)]">
            {CHECK}
            <span className="hidden sm:inline">Done</span>
          </span>
        ) : (
          <button
            type="button"
            disabled={busy}
            onClick={() => stepAction(step)}
            className="h-[34px] shrink-0 whitespace-nowrap rounded-[8px] bg-[var(--t-accent)] px-[14px] text-[13px] font-bold text-[var(--t-accent-ink)] transition-opacity hover:opacity-90 disabled:opacity-50"
          >
            {meta.cta}
          </button>
        )}
      </li>
    );
  };

  // Окна выбора — общие для чек-листа и итога (у пропущенного шага в итоге та же кнопка).
  const pickers = (
    <>
      <UI_H2HPickerModal open={picker === 'me'} title="Find your swimmer record" onClose={() => { setPicker(null); setSearch(''); }}>
        <UI_H2HRivalPicker
          favorites={favoriteSwimmers}
          query={search}
          onQuery={setSearch}
          hits={found.data ?? null}
          loading={found.loading}
          error={!!found.error}
          onPick={pickMe}
          placeholder="Search your name..."
          emptyText="Nobody found — no results under this name yet."
        />
      </UI_H2HPickerModal>

      <UI_H2HPickerModal open={picker === 'club'} title="Choose your club" onClose={() => setPicker(null)}>
        <ClubPicker onPick={pickClub} />
      </UI_H2HPickerModal>

      {/* Семья: отмечать можно нескольких подряд, окно закрывает «Done». */}
      <UI_H2HPickerModal open={picker === 'family'} title="Mark your children" onClose={closeFamily}>
        <div className="mb-3 flex flex-wrap items-center gap-1.5">
          <span className="mr-1 text-[12px] font-bold text-[var(--t-text-2)]">
            Family{fav.familyLimit ? ` ${familyMembers.length}/${fav.familyLimit.max}` : ''}
          </span>
          {familyMembers.length === 0 && (
            <span className="text-[12px] text-[var(--t-text-3)]">Nobody yet — search below.</span>
          )}
          {familyMembers.map((f) => {
            const name = f.swimmer_name || `#${f.swimmer_id}`;
            return (
              <span
                key={f.id}
                className="inline-flex items-center gap-1.5 rounded-full border border-[var(--t-border)] py-0.5 pl-2.5 pr-1 text-[12px] font-bold"
              >
                <span aria-hidden="true" style={{ color: 'var(--t-level-family)' }}>♥</span>
                <span dir="auto">{name}</span>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => unmarkFamily(f.id)}
                  aria-label={`Remove ${name} from family`}
                  className="flex h-5 w-5 items-center justify-center rounded-full text-[var(--t-text-2)] hover:bg-[var(--t-accent-soft)] hover:text-[var(--t-text)] disabled:opacity-50"
                >
                  ×
                </button>
              </span>
            );
          })}
        </div>
        <UI_H2HRivalPicker
          favorites={familyCandidates}
          query={search}
          onQuery={setSearch}
          hits={familyHits}
          loading={found.loading}
          error={!!found.error}
          onPick={markFamily}
          placeholder="Search your child's name..."
          emptyText="Nobody else found under this name."
        />
        {error && <p className="m-0 mt-3 text-[13px] text-[var(--t-danger)]">{error}</p>}
        <div className="mt-4 flex justify-end">
          <button
            type="button"
            onClick={closeFamily}
            className="h-9 rounded-[8px] bg-[var(--t-accent)] px-[18px] text-[13px] font-bold text-[var(--t-accent-ink)] hover:opacity-90"
          >
            Done
          </button>
        </div>
      </UI_H2HPickerModal>
    </>
  );

  /**
   * Строка итога (2a): иконка, подпись, что выбрано и ссылка на это. Пропущенный шаг — «Not added»
   * и та же кнопка, что в чек-листе: сделанное делом потом сильнее отказа (ProfileSetupRules).
   */
  const recapRow = (step: ProfileSetupStep) => {
    const meta = STEP_META[step.key];
    const recap = RECAP[step.key];
    const href = targetHref(step);
    return (
      <li key={step.key} className="flex items-center gap-3 border-t border-[var(--t-border)] py-2.5">
        <span className={`flex w-5 shrink-0 justify-center ${step.skipped ? 'text-[var(--t-text-3)]' : 'text-[var(--t-accent)]'}`}>
          {meta.icon}
        </span>
        <span className="w-12 shrink-0 text-[13px] text-[var(--t-text-2)] sm:w-[90px]">{recap.label}</span>
        {step.skipped || !step.targetName ? (
          <span className="min-w-0 flex-1 text-[14px] text-[var(--t-text-3)]">Not added</span>
        ) : (
          <span className="flex min-w-0 flex-1 items-center gap-1.5">
            <span dir="auto" className="truncate text-[14px] font-bold">{step.targetName}</span>
            {step.status === 'pending' && (
              <span className="hp-mono shrink-0 rounded-[6px] border border-[var(--t-border)] px-1.5 py-px text-[10px] font-extrabold text-[var(--t-text-2)]">
                PENDING
              </span>
            )}
          </span>
        )}
        {step.skipped ? (
          <button
            type="button"
            disabled={busy}
            onClick={() => stepAction(step)}
            className="shrink-0 whitespace-nowrap text-[13px] font-semibold text-[var(--t-accent)] hover:underline disabled:opacity-50"
          >
            {meta.cta}
          </button>
        ) : href ? (
          <a href={href} className="shrink-0 whitespace-nowrap text-[13px] font-semibold text-[var(--t-accent)] no-underline hover:underline">
            {recap.open}
          </a>
        ) : step.key === 'family' ? (
          <button
            type="button"
            onClick={() => setPicker('family')}
            className="shrink-0 whitespace-nowrap text-[13px] font-semibold text-[var(--t-accent)] hover:underline"
          >
            {recap.open}
          </button>
        ) : null}
      </li>
    );
  };

  // ── Готово (хендофф set-up-profile-ex1, вариант 2a): все три шага сделаны или пропущены ──
  if (s.done === s.total) {
    const me = s.steps.find((x) => x.key === 'me');
    const myHref = me && !me.skipped && me.targetId != null ? routes.swimmer(me.targetId) : null;
    const pickedRoles = SURVEY_ROLES.filter((r) => s.survey[r]);
    return shell(
      <main className="mx-auto w-full max-w-[640px] px-4 pt-24">
        <section className="rounded-[12px] border border-[var(--t-border)] bg-[var(--t-card)] p-6 shadow-[var(--t-shadow)]">
          <div className="mb-[18px] flex flex-wrap items-center gap-[14px]">
            <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-[color-mix(in_srgb,var(--t-good)_14%,transparent)] text-[var(--t-good)]">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M5 12.5l4.5 4.5L19 7.5" />
              </svg>
            </span>
            <div className="min-w-0 flex-1">
              <h1 className="m-0 text-[20px] font-extrabold">You&apos;re all set</h1>
              <p className="m-0 mt-0.5 text-[13px] text-[var(--t-text-2)]">SwimHub now shows your results, club and group first.</p>
            </div>
            {/* Ответ опроса — только плашками, ни на что не влияет (Р1). */}
            {pickedRoles.length > 0 && (
              <span className="flex basis-full flex-wrap gap-1.5 sm:basis-auto">
                {pickedRoles.map((r) => (
                  <span key={r} className="whitespace-nowrap rounded-[12px] bg-[var(--t-accent-soft)] px-2.5 py-1 text-[12px] font-semibold text-[var(--t-accent)]">
                    {ROLE_LABELS[r]}
                  </span>
                ))}
              </span>
            )}
          </div>

          <ul className="m-0 list-none p-0">
            {RECAP_ORDER.flatMap((k) => s.steps.filter((x) => x.key === k)).map(recapRow)}
          </ul>

          {error && <p className="m-0 mt-3 text-[13px] text-[var(--t-danger)]">{error}</p>}

          <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-[var(--t-border)] pt-4">
            <span className="text-[12px] text-[var(--t-text-3)]">
              Change any of this later in <a href={routes.myFavorites()} className="text-[var(--t-accent)]">My favorites</a>.
            </span>
            {/* «Это я» есть — к своим заплывам; пропущен — к результатам: своих заплывов не найти. */}
            <a
              href={myHref ?? routes.results()}
              className="flex h-9 items-center whitespace-nowrap rounded-[8px] bg-[var(--t-accent)] px-[18px] text-[13px] font-bold text-[var(--t-accent-ink)] no-underline hover:opacity-90"
            >
              {myHref ? 'Go to my swims' : 'Browse results'}
            </a>
          </div>
        </section>
        {pickers}
      </main>,
    );
  }

  return shell(
    <main className="mx-auto w-full max-w-[640px] px-4 pt-24">
      <section className="rounded-[12px] border border-[var(--t-border)] bg-[var(--t-card)] p-6 shadow-[var(--t-shadow)]">
        <div className="flex items-baseline justify-between gap-3">
          <h1 className="m-0 mb-1 text-[20px] font-extrabold">Set up your profile</h1>
          <span className="text-[12px] font-bold text-[var(--t-accent)]">
            {s.done}/{s.total}
          </span>
        </div>
        <p className="m-0 mb-5 text-[13px] text-[var(--t-text-2)]">Add yourself, your club and group</p>

        {/* Опрос: прав не даёт (Р1); «I'm a parent» добавляет шаг «My family» (ex3). */}
        <div className="mb-2.5 flex items-baseline gap-2">
          <span className="shrink-0 whitespace-nowrap text-[14px] font-bold">Who are you?</span>
          <span className="text-[12px] text-[var(--t-text-3)]">Pick all that apply</span>
        </div>
        <div className="mb-5 flex flex-wrap gap-2" role="group" aria-label="Who are you?">
          {SURVEY_ROLES.map((role) => {
            const on = s.survey[role];
            return (
              <button
                key={role}
                type="button"
                aria-pressed={on}
                onClick={() => { void toggleRole(role); }}
                className={`flex h-9 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-[18px] border px-[14px] text-[13px] font-semibold transition-colors ${
                  on
                    ? 'border-[var(--t-accent)] bg-[var(--t-accent-soft)] text-[var(--t-accent)]'
                    : 'border-[var(--t-border)] bg-transparent text-[var(--t-text)] hover:border-[var(--t-accent-border)]'
                }`}
              >
                {ROLE_META[role].icon}
                {ROLE_META[role].label}
              </button>
            );
          })}
        </div>

        <ul className="m-0 list-none p-0">{s.steps.map(stepRow)}</ul>

        {error && <p className="m-0 mt-3 text-[13px] text-[var(--t-danger)]">{error}</p>}
      </section>

      <div className="mt-4 flex items-center justify-between px-1 text-[12px] text-[var(--t-text-3)]">
        <span>Change any of this later in <a href={routes.myFavorites()} className="text-[var(--t-accent)]">My favorites</a>.</span>
        {/* Всё сделано — пункт меню и так скрыт, закрывать нечего. */}
        {!s.dismissed && s.done < s.total && (
          <button
            type="button"
            className="text-[12px] font-bold text-[var(--t-text-2)] underline-offset-2 hover:underline"
            onClick={() => { void dismiss().then((ok) => { if (ok) window.location.href = routes.home(); }); }}
          >
            Dismiss
          </button>
        )}
      </div>

      {pickers}
    </main>,
  );
}

export default SetupProfile;

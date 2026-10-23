import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import { useAuth } from '../../hooks/useAuth';
import { useFavorites } from '../../hooks/useFavorites';
import {
  useProfileSetup,
  SURVEY_ROLES,
  STEP_TITLES,
  ROLE_LABELS,
  type ProfileSetupStep,
  type ProfileSetupStepKey,
  type ProfileSetupTarget,
  type SurveyRole,
} from '../../hooks/useProfileSetup';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import ProfileEditPopup, { type EditKind, type EditRequest } from './components/profile-edit-popup';
import DeleteAccountDialog from './components/delete-account-dialog';
import * as api from './profile-api';
import { routes } from '../../utils/routes';

/**
 * Страница «Set up profile» — хендофф `!design_handoff/set-up-profile` вариант 1a,
 * план `docs/plans/profile-setup-plan.md` §5.
 *
 * Решения Влада 16.10.2026:
 * - три базовых шага у всех: «это я», клуб, группа;
 * - опрос «Who are you?» прав не даёт; «Just watching» исключающий (правило — `toggleSurveyRole`);
 * - заявка в группу (pending) засчитывает шаг;
 * - Dismiss — ссылкой внизу, пункт меню после него не показывается;
 * - (17.10.2026) от шага можно отказаться: «Not a swimmer», «No club», «No group» — отказ
 *   засчитывает шаг и снимается кнопкой Undo. Тренер отмечает только клуб, родитель — без «это я».
 * - (17.10.2026, хендофф `set-up-profile-ex3`) «I'm a parent» добавляет шаг «My family»: найти
 *   детей поиском и отметить семьёй (золотое сердечко, до `familyLimit.max`). Других шагов по
 *   ролям нет. Сняли роль — шаг пропал, семья осталась.
 * - (18.10.2026, хендофф `set-up-profile-ex4`) все правки — одним попапом (5d), невыбранные роли
 *   подсказками «Also» внизу итога (4b); решения Р7–Р10 — в плане, «Правка итога».
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

const SWIM_ICON = (
  <Svg size={16}>
    <circle cx="16" cy="5.5" r="2" />
    <path d="M4 11l5-2.5 3.5 3L15 10M2 17c1.7 0 1.7-1.2 3.3-1.2S7 17 8.7 17s1.7-1.2 3.3-1.2S13.7 17 15.3 17s1.7-1.2 3.4-1.2S20.3 17 22 17" />
  </Svg>
);
// Учитель у доски — `ph-chalkboard-teacher` из хендоффа ex4 (раньше был планшет с листом).
const COACH_ICON = (
  <Svg size={16}>
    <rect x="9" y="3.5" width="12" height="9" rx="1" />
    <circle cx="6" cy="9.5" r="2.2" />
    <path d="M2.5 20.5v-1.8a3.5 3.5 0 0 1 7 0v1.8M8.5 14l4.5-2.5" />
  </Svg>
);
const EYE_ICON = (
  <Svg size={16}>
    <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" />
    <circle cx="12" cy="12" r="3" />
  </Svg>
);

const ROLE_META: Record<SurveyRole, { label: string; icon: React.ReactNode }> = {
  swimmer: { label: 'I swim', icon: SWIM_ICON },
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
  coach: { label: "I'm a coach", icon: COACH_ICON },
  watcher: { label: 'Just watching', icon: EYE_ICON },
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
    // Число — из лимита сервера (`hintFor` ниже); здесь запасной текст, пока лимит не приехал.
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
  family: { label: 'Family', open: '' },
  club: { label: 'Club', open: 'Open club →' },
  group: { label: 'Group', open: 'Open group →' },
};

/** Порядок строк итога: семья — после клуба, перед группой (хендофф ex3), в чек-листе — второй. */
const RECAP_ORDER: ProfileSetupStepKey[] = ['me', 'club', 'family', 'group'];

/**
 * Подсказки «Also» (хендофф ex4, 4b): что даёт роль и куда ведёт кнопка. Порядок — как в
 * хендоффе. Р7: показываем все четыре; у coach/watcher своих шагов нет — кнопка ставит роль и
 * ведёт в каталог групп / к результатам.
 */
const ALSO: { role: SurveyRole; icon: React.ReactNode; question: string; gives: string; cta: string }[] = [
  {
    role: 'parent',
    icon: <Svg size={16} fill="currentColor"><path d={HEART} /></Svg>,
    question: 'Are you a parent?',
    gives: 'Mark your children · Family favorite',
    cta: 'Add children',
  },
  { role: 'coach', icon: COACH_ICON, question: 'Are you a coach?', gives: 'Get access to the groups you coach', cta: 'Request access' },
  { role: 'swimmer', icon: SWIM_ICON, question: 'Do you swim?', gives: 'Find your swimmer record', cta: 'Find me' },
  { role: 'watcher', icon: EYE_ICON, question: 'Just watching?', gives: 'Follow swimmers and clubs', cta: 'Browse' },
];
const ALSO_VISIBLE = 2;

const CHECK = (
  <Svg size={16}>
    <path d="M5 12.5l4.5 4.5L19 7.5" />
  </Svg>
);

const PENCIL = (
  <Svg size={14}>
    <path d="M14.5 5.5l4 4M4 20l1-4.5L15.8 4.7a1.4 1.4 0 0 1 2 0l1.5 1.5a1.4 1.4 0 0 1 0 2L8.5 19 4 20z" />
  </Svg>
);

/** Сколько живёт тост и сколько ждёт отложенный уход из группы (Р9). */
const TOAST_MS = 5000;

/**
 * Тост после Save/Remove. `undo` — обратная операция; `commit` — отложенное действие (уход из
 * группы): уходит на сервер, когда тост погас, и не уходит вовсе, если нажали Undo.
 */
interface Toast {
  text: string;
  undo: () => Promise<void>;
  commit?: () => Promise<void>;
}

// ── Страница ─────────────────────────────────────────────────────────────────

function SetupProfile() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();
  const deep = useDeepThemeClass();
  const { setup, refresh, toggleRole, setStepSkipped, dismiss } = useProfileSetup();
  // Только лимит семьи: правки идут через profile-api, показ — статусом с сервера.
  const { familyLimit } = useFavorites();

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [edit, setEdit] = useState<EditRequest | null>(null);
  const [showAllAlso, setShowAllAlso] = useState(false);
  const [toast, setToast] = useState<Toast | null>(null);
  // Группы, уход из которых ждёт конца тоста (Р9) — их строк в итоге уже не видно.
  const [leaving, setLeaving] = useState<Set<number>>(new Set());
  // «Delete account» (docs/plans/account-deletion-plan.md). `?delete=1` — возврат после повторного
  // входа через Google: окно открывается само, а параметр уходит из адреса.
  const [deleteOpen, setDeleteOpen] = useState(() => new URLSearchParams(window.location.search).get('delete') === '1');
  const [deleted, setDeleted] = useState(false);
  useEffect(() => {
    const url = new URL(window.location.href);
    if (!url.searchParams.has('delete')) return;
    url.searchParams.delete('delete');
    window.history.replaceState(null, '', url.pathname + url.search + url.hash);
  }, []);

  const toastRef = useRef<Toast | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  /** Тост погас (или его вытеснил следующий): отложенное — на сервер. */
  const settle = useCallback(async (t: Toast | null) => {
    if (!t?.commit) return;
    await t.commit();
    await refresh();
  }, [refresh]);

  const showToast = useCallback((t: Toast) => {
    if (timerRef.current) clearTimeout(timerRef.current);
    const prev = toastRef.current;
    toastRef.current = t;
    setToast(t);
    void settle(prev);
    timerRef.current = setTimeout(() => {
      if (toastRef.current !== t) return;
      toastRef.current = null;
      setToast(null);
      void settle(t);
    }, TOAST_MS);
  }, [settle]);

  const undoToast = async () => {
    const t = toastRef.current;
    if (!t) return;
    if (timerRef.current) clearTimeout(timerRef.current);
    toastRef.current = null;
    setToast(null);
    setBusy(true);
    try {
      await t.undo();
      await refresh();
    } finally {
      setBusy(false);
    }
  };

  // Закрыли вкладку, пока тост висел: отложенный уход всё равно отправляем (keepalive).
  useEffect(() => {
    const flush = () => { void toastRef.current?.commit?.(); toastRef.current = null; };
    window.addEventListener('pagehide', flush);
    return () => window.removeEventListener('pagehide', flush);
  }, []);

  // Шаг семьи, клубы и группы — из статуса; группы под отложенным уходом скрыты.
  const stepOf = useCallback(
    (key: ProfileSetupStepKey) => setup?.steps.find((x) => x.key === key) ?? null, [setup]);
  const takenClubIds = useMemo(() => new Set((stepOf('club')?.targets ?? []).map((t) => t.id)), [stepOf]);
  const takenGroupIds = useMemo(() => new Set((stepOf('group')?.targets ?? []).map((t) => t.id)), [stepOf]);

  const shell = (children: React.ReactNode) => (
    <div className={`${deep} relative min-h-screen overflow-x-clip pb-24 text-[var(--t-text)]`} style={{ background: 'var(--deep-hero-grad)' }}>
      <AppTopbar />
      {children}
      <UI_ModeToggle />
    </div>
  );

  // Аккаунт удалён — сессии уже нет, и useAuth о ней ещё не знает: показываем итог, а не гостя.
  if (deleted) {
    return shell(
      <div className="flex min-h-[70vh] items-center justify-center px-4">
        <div className="deep-card w-full max-w-md text-center">
          <h1 className="deep-card-title mb-2">Your account was deleted</h1>
          <p className="mb-4 text-sm text-[var(--t-text-2)]">You&apos;re signed out. Thanks for swimming with us.</p>
          <a
            href={routes.home()}
            className="hp-mono inline-block rounded-[11px] bg-[var(--t-accent)] px-4 py-2 text-sm font-extrabold text-[var(--t-accent-ink)] no-underline"
          >
            Go to home page
          </a>
        </div>
      </div>,
    );
  }

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
  const familyTargets = stepOf('family')?.targets ?? [];
  const meTarget = stepOf('me')?.targets[0] ?? null;

  // ── Правки из попапа (5d) ──────────────────────────────────────────────────
  // Каждая возвращает текст ошибки или null; успех — тост с Undo и перечитанный статус.

  const done = async (text: string, undo: () => Promise<void>, commit?: () => Promise<void>) => {
    await refresh();
    showToast({ text, undo, commit });
    return null;
  };

  const saveEdit = async (req: EditRequest, ids: number[]): Promise<string | null> => {
    const prev = req.current ?? null;
    const id = ids[0];

    if (req.kind === 'me') {
      const fav = await api.ensureFavorite('swimmer', id);
      if ('error' in fav) return fav.error;
      const err = await api.setPrimary(fav.id, true);
      if (err) return err;
      return done(prev ? 'Me changed' : 'Me added', async () => {
        if (prev?.favoriteId) await api.setPrimary(prev.favoriteId, true);
        else await api.setPrimary(fav.id, false);
        if (fav.created) await api.removeFavorite(fav.id);
      });
    }

    if (req.kind === 'family') {
      const before = familyTargets;
      const removed = before.filter((t) => !ids.includes(t.id));
      const addedIds = ids.filter((x) => !before.some((t) => t.id === x));
      // Сначала снимаем — освобождаем места под лимит семьи.
      for (const t of removed) {
        const err = t.favoriteId ? await api.setFamily(t.favoriteId, false) : null;
        if (err) { await refresh(); return err; }
      }
      const added: { favId: number; created: boolean }[] = [];
      for (const swimmerId of addedIds) {
        const fav = await api.ensureFavorite('swimmer', swimmerId);
        if ('error' in fav) { await refresh(); return fav.error; }
        const err = await api.setFamily(fav.id, true);
        if (err) { await refresh(); return err; }
        added.push({ favId: fav.id, created: fav.created });
      }
      return done('Family updated', async () => {
        for (const a of added) {
          await api.setFamily(a.favId, false);
          if (a.created) await api.removeFavorite(a.favId);
        }
        for (const t of removed) if (t.favoriteId) await api.setFamily(t.favoriteId, true);
      });
    }

    if (req.kind === 'club') {
      // Р8: смена клуба группу не трогает. Снимаем прежний первым — общий лимит клубов.
      if (prev?.favoriteId) {
        const err = await api.removeFavorite(prev.favoriteId);
        if (err) return err;
      }
      const fav = await api.ensureFavorite('club', id);
      if ('error' in fav) {
        if (prev) await api.ensureFavorite('club', prev.id);
        await refresh();
        return fav.error;
      }
      return done(prev ? 'Club changed' : 'Club added', async () => {
        if (fav.created) await api.removeFavorite(fav.id);
        if (prev) await api.ensureFavorite('club', prev.id);
      });
    }

    // Группа: вступаем сразу, уход из прежней — отложен под Undo (Р9).
    const err = await api.joinGroup(id);
    if (err) return err;
    if (prev) setLeaving((l) => new Set(l).add(prev.id));
    return done(prev ? 'Group changed' : 'Group added', async () => {
      if (prev) setLeaving((l) => { const n = new Set(l); n.delete(prev.id); return n; });
      await api.leaveGroup(id);
    }, prev ? async () => {
      await api.leaveGroup(prev.id, true);
      setLeaving((l) => { const n = new Set(l); n.delete(prev.id); return n; });
    } : undefined);
  };

  const removeEdit = async (req: EditRequest): Promise<string | null> => {
    const prev = req.current ?? null;

    if (req.kind === 'me' && prev?.favoriteId) {
      // Снимается только звезда «это я»; пловец остаётся в избранном, результаты не трогаются.
      const err = await api.setPrimary(prev.favoriteId, false);
      if (err) return err;
      return done('Me removed', async () => { await api.setPrimary(prev.favoriteId!, true); });
    }

    if (req.kind === 'family') {
      const before = familyTargets;
      for (const t of before) {
        const err = t.favoriteId ? await api.setFamily(t.favoriteId, false) : null;
        if (err) { await refresh(); return err; }
      }
      return done('Family removed', async () => {
        for (const t of before) if (t.favoriteId) await api.setFamily(t.favoriteId, true);
      });
    }

    if (req.kind === 'club' && prev?.favoriteId) {
      const err = await api.removeFavorite(prev.favoriteId);
      if (err) return err;
      return done('Club removed', async () => { await api.ensureFavorite('club', prev.id); });
    }

    if (req.kind === 'group' && prev) {
      setLeaving((l) => new Set(l).add(prev.id));
      return done('Left the group', async () => {
        setLeaving((l) => { const n = new Set(l); n.delete(prev.id); return n; });
      }, async () => {
        await api.leaveGroup(prev.id, true);
        setLeaving((l) => { const n = new Set(l); n.delete(prev.id); return n; });
      });
    }
    return null;
  };

  const openEdit = (kind: EditKind, current?: ProfileSetupTarget | null) =>
    setEdit({ kind, mode: kind === 'family' ? (familyTargets.length > 0 ? 'change' : 'add') : current ? 'change' : 'add', current });

  /** Подсказка шага: у семьи — число из лимита сервера; клуб тренера — «Club you coach at» (ex3). */
  const hintFor = (step: ProfileSetupStep): string => {
    if (step.key === 'family' && familyLimit) {
      return `Mark your children · Family favorite, up to ${familyLimit.max}`;
    }
    if (step.key === 'club' && s.survey.coach && !s.survey.swimmer && !s.survey.parent) return 'Club you coach at';
    return STEP_META[step.key].hint;
  };

  const targetHref = (key: ProfileSetupStepKey, t: ProfileSetupTarget): string | null => {
    if (key === 'me') return routes.swimmer(t.id);
    if (key === 'club') return routes.club(t.id);
    if (key === 'group') return t.slug ? routes.group(t.slug) : null;
    return null; // семья — несколько пловцов: строка открывает окно семьи
  };

  /** Отказ от шага или его отмена; статус (и счётчик в меню) перечитывает хук. */
  const skipStep = (step: ProfileSetupStep, skipped: boolean) => {
    setBusy(true);
    setError(null);
    void setStepSkipped(step.key, skipped)
      .then((ok) => { if (!ok) setError('Could not save — please try again.'); })
      .finally(() => setBusy(false));
  };

  const pendingChip = (
    <span className="hp-mono shrink-0 rounded-[6px] border border-[var(--t-border)] px-1.5 py-px text-[10px] font-extrabold text-[var(--t-text-2)]">
      PENDING
    </span>
  );

  const stepRow = (step: ProfileSetupStep) => {
    const meta = STEP_META[step.key];
    const first = step.targets.find((t) => !(step.key === 'group' && leaving.has(t.id))) ?? null;
    const href = first ? targetHref(step.key, first) : null;
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
              ) : (
                <button
                  type="button"
                  dir="auto"
                  onClick={() => openEdit(step.key, first)}
                  className="truncate text-left text-[var(--t-text-2)] hover:text-[var(--t-accent)]"
                >
                  {step.targetName}
                </button>
              )}
              {step.status === 'pending' && pendingChip}
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
            onClick={() => openEdit(step.key)}
            className="h-[34px] shrink-0 whitespace-nowrap rounded-[8px] bg-[var(--t-accent)] px-[14px] text-[13px] font-bold text-[var(--t-accent-ink)] transition-opacity hover:opacity-90 disabled:opacity-50"
          >
            {meta.cta}
          </button>
        )}
      </li>
    );
  };

  // Опасное действие — тихой ссылкой под карточкой, отдельно от дела страницы (Р1 плана удаления).
  const deleteAccountLink = (
    <div className="mt-10 flex justify-center">
      <button
        type="button"
        onClick={() => setDeleteOpen(true)}
        className="rounded-[8px] border border-[var(--t-danger-border)] bg-transparent px-3 py-1.5 text-[12px] font-bold text-[var(--t-danger)] hover:bg-[var(--t-danger-soft)]"
      >
        Delete account
      </button>
    </div>
  );

  // Попап и тост — общие для чек-листа и итога.
  const overlays = (
    <>
      {deleteOpen && (
        <DeleteAccountDialog
          email={auth.email ?? ''}
          // refresh — чтобы топбар перестал показывать имя: сессии уже нет.
          onDeleted={() => { setDeleteOpen(false); setDeleted(true); void auth.refresh(); }}
          onClose={() => setDeleteOpen(false)}
        />
      )}
      <ProfileEditPopup
        request={edit}
        takenClubIds={takenClubIds}
        takenGroupIds={takenGroupIds}
        family={familyTargets}
        meSwimmerId={meTarget?.id ?? null}
        familyMax={familyLimit?.max ?? null}
        icon={edit ? STEP_META[edit.kind].icon : null}
        onClose={() => setEdit(null)}
        onSave={(ids) => saveEdit(edit!, ids)}
        onRemove={() => removeEdit(edit!)}
      />
      {toast && (
        <div
          role="status"
          className={`${deep} fixed bottom-6 left-1/2 z-[110] flex -translate-x-1/2 items-center gap-3 rounded-[10px] border border-[var(--t-border)] bg-[var(--t-card)] px-4 py-2.5 text-[13px] text-[var(--t-text)] shadow-[var(--t-shadow)]`}
        >
          <span>{toast.text}</span>
          <button type="button" disabled={busy} onClick={() => { void undoToast(); }} className="font-bold text-[var(--t-accent)] hover:underline disabled:opacity-50">
            Undo
          </button>
        </div>
      )}
    </>
  );

  /**
   * Строка итога (2a, 5d): вся строка открывает попап (карандаш справа), ссылка-переход —
   * отдельная цель. Пустая или пропущенная строка — «Not added» и попап в режиме Add.
   */
  const recapRow = (key: ProfileSetupStepKey, step: ProfileSetupStep, t: ProfileSetupTarget | null, rowKey: string) => {
    const meta = STEP_META[key];
    const empty = t == null || step.skipped;
    const href = !empty && t ? targetHref(key, t) : null;
    const value = key === 'family'
      ? familyTargets.map((f) => f.name).join(', ')
      : t?.name ?? '';
    const open = () => openEdit(key, empty ? null : t);
    return (
      <li key={rowKey} className="border-t border-[var(--t-border)]">
        <div
          role="button"
          tabIndex={0}
          onClick={open}
          onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); } }}
          className="group -mx-2 flex cursor-pointer items-center gap-3 rounded-[8px] px-2 py-2.5 transition-colors hover:bg-[var(--t-accent-soft)]"
        >
          <span className={`flex w-5 shrink-0 justify-center ${empty ? 'text-[var(--t-text-3)]' : 'text-[var(--t-accent)]'}`}>
            {meta.icon}
          </span>
          <span className="w-12 shrink-0 text-[13px] text-[var(--t-text-2)] sm:w-[90px]">{RECAP[key].label}</span>
          {empty ? (
            <span className="min-w-0 flex-1 text-[14px] text-[var(--t-text-3)]">Not added</span>
          ) : (
            <span className="flex min-w-0 flex-1 items-center gap-1.5">
              <span dir="auto" className="truncate text-[14px] font-bold">{value}</span>
              {t?.status === 'pending' && pendingChip}
            </span>
          )}
          {empty ? (
            <span className="shrink-0 whitespace-nowrap text-[13px] font-semibold text-[var(--t-accent)]">{meta.cta}</span>
          ) : href ? (
            <a
              href={href}
              onClick={(e) => e.stopPropagation()}
              className="shrink-0 whitespace-nowrap text-[13px] font-semibold text-[var(--t-accent)] no-underline hover:underline"
            >
              {RECAP[key].open}
            </a>
          ) : null}
          <span className="shrink-0 text-[var(--t-text-3)]">{PENCIL}</span>
        </div>
      </li>
    );
  };

  const recapRows = RECAP_ORDER.flatMap((key) => {
    const step = stepOf(key);
    if (!step) return [];
    if (key === 'family') return [recapRow(key, step, familyTargets[0] ?? null, key)];
    const targets = key === 'group' ? step.targets.filter((t) => !leaving.has(t.id)) : step.targets;
    // Р10: строка на каждый клуб и группу; пусто — одна строка «Not added».
    if (targets.length === 0 || step.skipped) return [recapRow(key, step, null, key)];
    return targets.map((t) => recapRow(key, step, t, `${key}-${t.id}`));
  });

  // Опрос «Who are you?» — в чек-листе. Прав не даёт (Р1); «I'm a parent» добавляет шаг «My family».
  const surveyBlock = (
    <>
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
    </>
  );

  // ── Готово (хендофф ex1, 2a + ex4, 4b/5d): все шаги сделаны или пропущены ──
  if (s.done === s.total) {
    const me = stepOf('me');
    const myHref = me && !me.skipped && meTarget ? routes.swimmer(meTarget.id) : null;
    const pickedRoles = SURVEY_ROLES.filter((r) => s.survey[r]);
    const anyRole = pickedRoles.length > 0;

    // 4b: невыбранные роли. «Just watching» исключает остальные (Р1а) — предлагаем его, только
    // пока не выбрано ничего: иначе кнопка молча сняла бы уже выбранные роли и их шаги.
    const also = ALSO.filter((a) => !s.survey[a.role] && (a.role !== 'watcher' || !anyRole));
    const alsoShown = showAllAlso ? also : also.slice(0, ALSO_VISIBLE);
    const alsoHidden = also.length - alsoShown.length;

    /** Кнопка подсказки: ставит роль и сразу ведёт к делу (не к выбору роли). */
    const alsoAction = async (role: SurveyRole) => {
      setBusy(true);
      try {
        if (!(await toggleRole(role))) { setError('Could not save — please try again.'); return; }
        if (role === 'parent') openEdit('family');
        else if (role === 'swimmer') openEdit('me', meTarget);
        else if (role === 'coach') window.location.href = routes.groupsList();
        else window.location.href = routes.results();
      } finally {
        setBusy(false);
      }
    };

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
              <p className="m-0 mt-0.5 text-[13px] text-[var(--t-text-2)]">Click any row to change it.</p>
            </div>
            {anyRole && (
              <span className="flex basis-full flex-wrap gap-1.5 sm:basis-auto">
                {pickedRoles.map((r) => (
                  <span key={r} className="whitespace-nowrap rounded-[12px] bg-[var(--t-accent-soft)] px-2.5 py-1 text-[12px] font-semibold text-[var(--t-accent)]">
                    {ROLE_LABELS[r]}
                  </span>
                ))}
              </span>
            )}
          </div>

          <ul className="m-0 list-none p-0">{recapRows}</ul>

          {/* 5d: ещё клуб или группа — тот же попап в режиме Add. */}
          <div className="mt-2 text-[13px] text-[var(--t-text-2)]">
            + Add another{' '}
            <button type="button" onClick={() => openEdit('club')} className="font-semibold text-[var(--t-accent)] hover:underline">club</button>
            {' or '}
            <button type="button" onClick={() => openEdit('group')} className="font-semibold text-[var(--t-accent)] hover:underline">group</button>
          </div>

          {/* 4b «Also»: невыбранные роли — вопрос, что даёт, кнопка сразу к делу. */}
          {also.length > 0 && (
            <div className="mt-4 flex flex-col gap-2 border-t border-[var(--t-border)] pt-3.5">
              <div className="text-[12px] font-semibold uppercase tracking-[0.06em] text-[var(--t-text-3)]">Also</div>
              {alsoShown.map((a) => (
                <div key={a.role} className="flex items-center gap-3 rounded-[8px] border border-dashed border-[var(--t-border)] px-3 py-2.5">
                  <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-[var(--t-accent-soft)] text-[var(--t-accent)]">{a.icon}</span>
                  <span className="min-w-0 flex-1">
                    <span className="block text-[14px] font-bold">{a.question}</span>
                    <span className="block text-[12px] text-[var(--t-text-2)]">{a.gives}</span>
                  </span>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => { void alsoAction(a.role); }}
                    className="h-8 shrink-0 whitespace-nowrap rounded-[8px] border border-[var(--t-accent)] px-3 text-[13px] font-semibold text-[var(--t-accent)] hover:bg-[var(--t-accent-soft)] disabled:opacity-50"
                  >
                    {a.cta}
                  </button>
                </div>
              ))}
              {alsoHidden > 0 && (
                <button type="button" onClick={() => setShowAllAlso(true)} className="self-start text-[12px] font-semibold text-[var(--t-accent)] hover:underline">
                  {alsoHidden} more role{alsoHidden > 1 ? 's' : ''}
                </button>
              )}
            </div>
          )}

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
        {deleteAccountLink}
        {overlays}
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

        {surveyBlock}

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

      {deleteAccountLink}
      {overlays}
    </main>,
  );
}

export default SetupProfile;

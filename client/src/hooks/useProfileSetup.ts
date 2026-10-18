import { useCallback, useEffect, useSyncExternalStore } from 'react';
import { useAuth } from './useAuth';
import { fetchAntiforgeryToken, invalidateTokenCache } from '../utils/antiforgery';

// «Set up profile» (docs/plans/profile-setup-plan.md): пункт меню с прогрессом и страница
// /setup-profile. Шаги и счётчик считает сервер (ProfileSetupRules) — здесь только чтение.

// `family` — только у родителя (хендофф set-up-profile-ex3, решение Влада 17.10.2026).
export type ProfileSetupStepKey = 'me' | 'family' | 'club' | 'group';

export interface ProfileSetupStep {
  key: ProfileSetupStepKey;
  done: boolean;
  /** Засчитан отказом («Not a swimmer», «No club», «No group», «Skip»); сделанное делом — false. */
  skipped: boolean;
  /** Id пловца / клуба / группы, которым шаг закрыт; у семьи — null. */
  targetId: number | null;
  /** Имя пловца или клуба (иврит по умолчанию), название группы; у семьи — имена через запятую. */
  targetName: string | null;
  /** Только у группы. */
  targetSlug: string | null;
  /** Только у группы: active | pending (заявка тоже засчитывает шаг). */
  status: 'active' | 'pending' | null;
  /**
   * ВСЕ цели шага (хендофф ex4, Р10): каждый клуб, группа, ребёнок семьи, «это я». Итог рисует
   * строку на каждую; поля выше — первая цель.
   */
  targets: ProfileSetupTarget[];
}

/** Одна цель шага: пловец, клуб или группа. */
export interface ProfileSetupTarget {
  id: number;
  /** Id записи избранного — у пловца и клуба; у группы null. */
  favoriteId: number | null;
  name: string;
  slug: string | null;
  status: 'active' | 'pending' | null;
}

/**
 * Ответ опроса «Who are you?» — прав не даёт (решение Влада 16.10.2026); на шаги влияет только
 * `parent` — добавляет шаг «My family» (хендофф ex3, 17.10.2026).
 */
export interface ProfileSurvey {
  swimmer: boolean;
  parent: boolean;
  coach: boolean;
  watcher: boolean;
  answeredAt: string | null;
}

export interface ProfileSetup {
  survey: ProfileSurvey;
  steps: ProfileSetupStep[];
  done: number;
  total: number;
  /** Показывать «Set up profile»: не все шаги сделаны и не закрыт Dismiss (или есть новый шаг). */
  visible: boolean;
  /** Все шаги сделаны или пропущены — итог «You're all set», в меню тихий «My profile». */
  complete: boolean;
  /** Несделанные шаги, которых пользователь ещё не видел — «New: …» и жёлтая точка (хендофф ex2, 3b). */
  newSteps: ProfileSetupStepKey[];
  dismissed: boolean;
}

// ── Модульный кэш на страницу (как useAuth) ──────────────────────────────────
// Один GET на загрузку страницы, сколько бы компонентов ни звало хук (меню + страница
// /setup-profile). refresh() перечитывает и обновляет всех подписчиков. Кэш помнит, ЧЕЙ он:
// сменился пользователь (вход, выход, dev-переключатель персонажа) — читаем заново.

let cached: ProfileSetup | null = null;
let cachedFor: number | null = null;
let inflight: Promise<ProfileSetup | null> | null = null;
const listeners = new Set<() => void>();

function notify() {
  listeners.forEach((l) => l());
}

async function fetchSetup(): Promise<ProfileSetup | null> {
  try {
    const r = await fetch('/api/me/profile-setup', { credentials: 'include' });
    // Ошибка — не повод ломать шапку: пункта просто нет.
    if (!r.ok) return null;
    return (await r.json()) as ProfileSetup;
  } catch {
    return null;
  }
}

function load(userId: number, force = false): Promise<ProfileSetup | null> {
  if (!force && cachedFor === userId && (cached || inflight)) return inflight ?? Promise.resolve(cached);
  cachedFor = userId;
  const p = fetchSetup().then((s) => {
    // Пока шёл запрос, пользователь мог смениться — тогда ответ чужой, выбрасываем.
    if (cachedFor === userId) {
      cached = s;
      inflight = null;
      notify();
    }
    return s;
  });
  inflight = p;
  return p;
}

/** Подменить снимок без запроса — оптимистичная запись опроса и её откат. */
function setCached(next: ProfileSetup | null) {
  cached = next;
  notify();
}

export type SurveyRole = 'swimmer' | 'parent' | 'coach' | 'watcher';
export const SURVEY_ROLES: SurveyRole[] = ['swimmer', 'parent', 'coach', 'watcher'];

/** Названия шагов — общие для страницы и строки «New: …» в меню. */
export const STEP_TITLES: Record<ProfileSetupStepKey, string> = {
  me: "It's me",
  family: 'My family',
  club: 'My club',
  group: 'My group',
};

/** Подписи ответа опроса — плашки итога и подстрока «My profile» в меню. */
export const ROLE_LABELS: Record<SurveyRole, string> = {
  swimmer: 'Swimmer',
  parent: 'Parent',
  coach: 'Coach',
  watcher: 'Just watching',
};

/** Подстрока тихого пункта «My profile»: роли через « · »; без ответа — сколько сделано. */
export function profileSubtitle(s: ProfileSetup): string {
  const roles = SURVEY_ROLES.filter((r) => s.survey[r]).map((r) => ROLE_LABELS[r]);
  if (roles.length > 0) return roles.join(' · ');
  return s.complete ? 'All set' : `${s.done}/${s.total} done`;
}

/**
 * Ответ после клика по чипу. «Just watching» исключающий (решение Влада 16.10.2026): включил
 * его — остальные гаснут; включил другую роль — гаснет он. Сервер такую пару всё равно
 * отклонит (400 `watcher_exclusive`), здесь правило лишь не даёт её отправить.
 */
export function toggleSurveyRole(s: ProfileSurvey, role: SurveyRole): Record<SurveyRole, boolean> {
  const next = { swimmer: s.swimmer, parent: s.parent, coach: s.coach, watcher: s.watcher };
  const on = !next[role];
  if (role === 'watcher') {
    if (on) { next.swimmer = false; next.parent = false; next.coach = false; }
  } else if (on) {
    next.watcher = false;
  }
  next[role] = on;
  return next;
}

async function send(method: 'PUT' | 'POST' | 'DELETE', path: string, body?: unknown): Promise<boolean> {
  const token = await fetchAntiforgeryToken();
  const init: RequestInit = {
    method,
    credentials: 'include',
    headers: {
      ...(token ? { 'X-XSRF-TOKEN': token } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  };
  let r = await fetch(path, init);
  // Протухший токен (сессия сменилась) — один повтор со свежим.
  if (r.status === 400 || r.status === 403) {
    invalidateTokenCache();
    const fresh = await fetchAntiforgeryToken();
    if (fresh && fresh !== token) {
      r = await fetch(path, { ...init, headers: { ...init.headers, 'X-XSRF-TOKEN': fresh } });
    }
  }
  return r.ok;
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function getSnapshot(): ProfileSetup | null {
  return cached;
}

/**
 * Состояние «Set up profile» текущего пользователя. У гостя — null (запроса нет).
 * `refresh()` — после любого действия, меняющего шаги (Me, клуб, группа, Dismiss).
 */
export function useProfileSetup() {
  const auth = useAuth();
  const snapshot = useSyncExternalStore(subscribe, getSnapshot);
  const userId = auth.isAuthenticated ? auth.userId : null;

  useEffect(() => {
    if (userId != null) load(userId);
  }, [userId]);

  const refresh = useCallback(() => (userId != null ? load(userId, true) : Promise.resolve(null)), [userId]);

  /**
   * Клик по чипу опроса: снимок меняется сразу, ответ уходит на сервер; отказ — откат.
   * «I'm a parent» добавляет или убирает шаг «My family» (хендофф ex3) — тогда статус
   * перечитывается: шаги и счётчик считает сервер.
   */
  const toggleRole = useCallback(async (role: SurveyRole): Promise<boolean> => {
    const before = cached;
    if (userId == null || !before || cachedFor !== userId) return false;
    const answer = toggleSurveyRole(before.survey, role);
    setCached({ ...before, survey: { ...answer, answeredAt: new Date().toISOString() } });
    const ok = await send('PUT', '/api/me/profile-setup/roles', answer);
    if (!ok && cachedFor === userId) setCached(before);
    else if (ok && answer.parent !== before.survey.parent) await load(userId, true);
    return ok;
  }, [userId]);

  /**
   * Отказ от шага или его отмена (решение Влада 17.10.2026): отказ засчитывает шаг — тренер
   * отмечает только клуб, родитель обходится без «это я». После записи статус перечитывается,
   * чтобы счётчик в меню совпал со страницей.
   */
  const setStepSkipped = useCallback(async (key: ProfileSetupStepKey, skipped: boolean): Promise<boolean> => {
    if (userId == null) return false;
    const ok = await send(skipped ? 'PUT' : 'DELETE', `/api/me/profile-setup/steps/${key}/skip`);
    if (ok) await load(userId, true);
    return ok;
  }, [userId]);

  /** Dismiss: пункт «Set up profile» больше не показывается в меню. */
  const dismiss = useCallback(async (): Promise<boolean> => {
    if (userId == null) return false;
    const ok = await send('POST', '/api/me/profile-setup/dismiss');
    if (ok) await load(userId, true);
    return ok;
  }, [userId]);

  // Снимок другого пользователя (или гостя) не показываем.
  const setup = userId != null && cachedFor === userId ? snapshot : null;
  return { setup, refresh, toggleRole, setStepSkipped, dismiss };
}

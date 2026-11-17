import type {
  BreakInput, HubGroupBreaks, HubGroupLevels, LaneChange, LanePlan, LanePlanInput, LanePlanPublishCheck, LanePlanSummary,
  Workout,
} from '../types';

/**
 * Клиент плана дорожек группы (docs/plans/lane-plans-plan.md). Ручки —
 * `/api/hub-groups/{id}/lane-plans` (snake_case); уровни для редактора —
 * `/api/me/hub-groups/{id}/levels` (camelCase, только управляющим).
 *
 * `key` плана — ключ ЗАНЯТИЯ `yyyy-MM-dd-HHmm` (CF5 потока тренера; `lane-plan-key.ts`), голая дата —
 * только чтение плана дня без занятия и старые ссылки.
 */

/** Итог запроса: `data` при успехе, `error` — текст сервера, `status` — HTTP-код (0 — сети нет). */
export interface ApiResult<T> {
  ok: boolean;
  status: number;
  data?: T;
  error?: string;
}

/** Токен antiforgery: свой кэш на модуль — как у остальных мутирующих клиентов проекта. */
let cachedToken: string | null = null;

async function antiforgery(): Promise<string | null> {
  if (cachedToken) return cachedToken;
  try {
    const r = await fetch('/api/antiforgery/token', { credentials: 'include' });
    cachedToken = r.ok ? (await r.json()).token ?? null : null;
  } catch {
    cachedToken = null;
  }
  return cachedToken;
}

/** Запрос с antiforgery и разбором ошибки — общий для клиентов таба Trainings (CF4: `account-link-api.ts`). */
export async function request<T>(method: string, url: string, body?: unknown): Promise<ApiResult<T>> {
  const headers: Record<string, string> = {};
  if (method !== 'GET') {
    const token = await antiforgery();
    if (!token) return { ok: false, status: 0 };
    headers['X-XSRF-TOKEN'] = token;
  }
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  try {
    const r = await fetch(url, {
      method,
      credentials: 'include',
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (r.ok) {
      const data = r.status === 204 ? undefined : await r.json().catch(() => undefined);
      return { ok: true, status: r.status, data: data as T | undefined };
    }
    if (method !== 'GET') cachedToken = null;
    const err = await r.json().catch(() => ({}));
    return { ok: false, status: r.status, error: (err as { error?: string }).error };
  } catch {
    if (method !== 'GET') cachedToken = null;
    return { ok: false, status: 0 };
  }
}

const base = (groupId: number) => `/api/hub-groups/${groupId}/lane-plans`;

export const lanePlansApi = {
  list: (groupId: number) => request<LanePlanSummary[]>('GET', base(groupId)),
  get: (groupId: number, key: string) => request<LanePlan>('GET', `${base(groupId)}/${key}`),
  /** Кто в день плана «не в воде» (On break / Doesn't train / «Can't come») — в «Not today» нового плана. Только управляющему. */
  away: (groupId: number, key: string) => request<{ swimmer_ids: number[] }>('GET', `${base(groupId)}/${key}/away`),
  save: (groupId: number, key: string, input: LanePlanInput) =>
    request<LanePlan>('PUT', `${base(groupId)}/${key}`, input),
  /**
   * Workout занятия (CF6): документ целиком, null — стереть. Отдельно от PUT плана — редакторы
   * дорожек и workout сохраняются сами и не перетирают друг друга. 404 — у занятия плана нет.
   */
  saveWorkout: (groupId: number, key: string, workout: Workout | null) =>
    request<void>('PUT', `${base(groupId)}/${key}/workout`, { workout }),
  /**
   * «Auto-arrange»: сервер сам делит дорожки между уровнями и раскладывает людей. Ничего не
   * сохраняет. `reserved_lanes` — сколько последних дорожек оставить пустыми про запас (всегда). `pinned` — закреплённые (CF2): остаются на своих дорожках, в ответе — с `pinned: true`.
   */
  autoLanes: (
    groupId: number,
    input: {
      lane_count: number; max_per_lane: number | null; reserved_lanes: number; swimmer_ids: number[] | null;
      pinned?: { swimmer_id: number; lane_no: number }[];
    },
  ) =>
    request<{ lanes: { lane_no: number; level_id: number | null }[]; swimmers: LanePlanInput['swimmers'] }>(
      'POST', `${base(groupId)}/auto-lanes`, input),
  /**
   * Что мешает опубликовать (решение 10 потока тренера): идущие пловцы без уровня не на дорожке.
   * Считает сервер — «идущий» это и ответ аккаунта, привязанного к пловцу.
   */
  publishCheck: (groupId: number, key: string) =>
    request<LanePlanPublishCheck>('GET', `${base(groupId)}/${key}/publish-check`),
  /** `notify` — push участникам «дорожки готовы». Отказ 400 с текстом — решение 10. */
  publish: (groupId: number, key: string, notify = false) =>
    request<void>('POST', `${base(groupId)}/${key}/publish`, { notify }),
  /**
   * Поставить идущего пловца на дорожку его уровня (CF4): тренер задал уровень в карточке человека.
   * `lane_change` null — двигать не пришлось; 404 — у занятия плана нет.
   */
  place: (groupId: number, key: string, swimmerId: number) =>
    request<{ lane_change: LaneChange | null }>('POST', `${base(groupId)}/${key}/swimmers/${swimmerId}/place`),
  unpublish: (groupId: number, key: string) => request<void>('POST', `${base(groupId)}/${key}/unpublish`),
  remove: (groupId: number, key: string) => request<void>('DELETE', `${base(groupId)}/${key}`),
  /** Зритель открыл вид Lanes (Т1б): гаснет «тебя перенесли». GET плана метку не гасит. */
  markSeen: (groupId: number) => request<void>('PUT', `${base(groupId)}/seen`),
  levels: (groupId: number) => request<HubGroupLevels>('GET', `/api/me/hub-groups/${groupId}/levels`),
  /** Уровень и подуровень пловца: null — «без уровня». Тело camelCase, как у всей ручки уровней. */
  setSwimmerLevel: (groupId: number, swimmerId: number, levelId: number | null, subLevel: number | null = null) =>
    request<void>('PUT', `/api/me/hub-groups/${groupId}/swimmer-levels/${swimmerId}`, { levelId, subLevel }),
  /** Уровень аккаунта-участника без пловца (Ш3.1); null — «без уровня». */
  setAccountLevel: (groupId: number, userId: number, levelId: number | null) =>
    request<void>('PUT', `/api/me/hub-groups/${groupId}/account-levels/${userId}`, { levelId }),
  /** «On break» (Ш3.1): свой перерыв; управляющему — все и недавние возвращения. */
  breaks: (groupId: number) => request<HubGroupBreaks>('GET', `/api/hub-groups/${groupId}/breaks`),
  /**
   * Статус человека (перерыв / «не тренируется» / в строю, Р74): без субъекта — себе; `until`
   * yyyy-MM-dd, null — бессрочно (тренер).
   */
  setBreak: (groupId: number, input: BreakInput) =>
    request<HubGroupBreaks>('PUT', `/api/hub-groups/${groupId}/breaks`, input),
};

/** Сегодня по Израилю, yyyy-MM-dd: план — календарный день бассейна, не UTC. */
export function todayInIsrael(): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Jerusalem', year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(new Date());
}

/** «Sun 27» — день недели и число: кнопка копии и её подтверждение. */
export function shortDay(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const weekday = new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-US', { weekday: 'short', timeZone: 'UTC' });
  return `${weekday} ${d}`;
}

/** «Sun, Sep 27» — короткая подпись даты плана (en-US: у en-GB месяц бывает «Sept»). */
export function formatPlanDate(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-US', {
    weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC',
  });
}

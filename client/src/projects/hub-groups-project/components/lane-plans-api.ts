import type { HubGroupLevels, LanePlan, LanePlanInput, LanePlanSummary } from '../types';

/**
 * Клиент плана дорожек группы (docs/plans/lane-plans-plan.md). Ручки —
 * `/api/hub-groups/{id}/lane-plans` (snake_case); уровни для редактора —
 * `/api/me/hub-groups/{id}/levels` (camelCase, только управляющим).
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

async function request<T>(method: string, url: string, body?: unknown): Promise<ApiResult<T>> {
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
  get: (groupId: number, date: string) => request<LanePlan>('GET', `${base(groupId)}/${date}`),
  save: (groupId: number, date: string, input: LanePlanInput) =>
    request<LanePlan>('PUT', `${base(groupId)}/${date}`, input),
  distribute: (
    groupId: number,
    input: { lane_count: number; lanes: LanePlanInput['lanes']; swimmer_ids: number[] | null },
  ) => request<{ swimmers: LanePlanInput['swimmers'] }>('POST', `${base(groupId)}/distribute`, input),
  publish: (groupId: number, date: string) => request<void>('POST', `${base(groupId)}/${date}/publish`),
  unpublish: (groupId: number, date: string) => request<void>('POST', `${base(groupId)}/${date}/unpublish`),
  remove: (groupId: number, date: string) => request<void>('DELETE', `${base(groupId)}/${date}`),
  levels: (groupId: number) => request<HubGroupLevels>('GET', `/api/me/hub-groups/${groupId}/levels`),
};

/** Сегодня по Израилю, yyyy-MM-dd: план — календарный день бассейна, не UTC. */
export function todayInIsrael(): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Jerusalem', year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(new Date());
}

/** «Sun, Sep 27» — короткая подпись даты плана (en-US: у en-GB месяц бывает «Sept»). */
export function formatPlanDate(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-US', {
    weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC',
  });
}

/**
 * Какой план открыть по умолчанию: ближайший с даты «сегодня» и позже, иначе последний
 * прошедший; планов нет — сегодня. Список приходит новыми сверху.
 */
export function pickDefaultDate(plans: LanePlanSummary[], today: string): string {
  const upcoming = plans.filter((p) => p.date >= today);
  if (upcoming.length > 0) return upcoming[upcoming.length - 1].date;
  return plans[0]?.date ?? today;
}

import type { LanePlanSummary } from '../types';
import { formatPlanDate, shortDay } from './lane-plans-api';

/**
 * Ключ плана дорожек (docs/plans/trainings-coach-flow-plan.md, CF5, решение 1): план принадлежит
 * ЗАНЯТИЮ — ключ `yyyy-MM-dd-HHmm`, тот же, что у ответов (`NextTraining.id`, `TrainingRsvp.session_id`).
 * Голая дата `yyyy-MM-dd` — план дня без занятия (архив после миграции, только чтение) и старые
 * ссылки на Lanes: сервер по ней отдаёт план дня или первый план этой даты.
 *
 * Ключи сравниваются строкой: `2026-11-15` < `2026-11-15-0700` < `2026-11-15-2000` < `2026-11-16`.
 */

const SESSION_KEY = /^\d{4}-\d{2}-\d{2}-\d{4}$/;

/** Ключ занятия (можно править и заводить план); `false` — голая дата. */
export function isSessionKey(key: string): boolean {
  return SESSION_KEY.test(key);
}

/** Дата ключа, yyyy-MM-dd. */
export function keyDate(key: string): string {
  return key.slice(0, 10);
}

/** «20:00» из ключа занятия; у голой даты — `null`. */
export function keyStart(key: string): string | null {
  return isSessionKey(key) ? `${key.slice(11, 13)}:${key.slice(13, 15)}` : null;
}

/** Ключ плана из ответа API: занятие, у плана дня без занятия — дата. */
export function planKey(plan: Pick<LanePlanSummary, 'date' | 'session_id'>): string {
  return plan.session_id ?? plan.date;
}

/** «Sun, Sep 27 · 20:00» — подпись плана; у плана дня — только дата. */
export function formatPlanKey(key: string): string {
  const start = keyStart(key);
  const date = formatPlanDate(keyDate(key));
  return start ? `${date} · ${start}` : date;
}

/** «Sun 27 · 20:00» — короткая подпись («Copy from …» и её подтверждение). */
export function shortPlanKey(key: string): string {
  const start = keyStart(key);
  const day = shortDay(keyDate(key));
  return start ? `${day} · ${start}` : day;
}

/**
 * Какой план открыть по умолчанию: ближайший с даты «сегодня» и позже, иначе последний
 * прошедший; планов нет — сегодня голой датой (покажется «No lane plan for …»). Список приходит
 * новыми сверху.
 */
export function pickDefaultKey(plans: LanePlanSummary[], today: string): string {
  const upcoming = plans.filter((p) => p.date >= today);
  if (upcoming.length > 0) return planKey(upcoming[upcoming.length - 1]);
  return plans[0] ? planKey(plans[0]) : today;
}

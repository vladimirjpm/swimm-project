import type { LanePlanSummary, PastTraining } from '../../../types';

/**
 * Какие прошедшие занятия можно открыть «посмотреть план» (27.11.2026): список «Past» в Next и кнопка
 * «View plan» на карточке ленты Trainings решают это ОДНИМ правилом — здесь.
 *
 * Открыть можно только занятие из `past-trainings` (`openDesk` ищет его там). Состав списка —
 * от роли:
 * - участник — сервер уже отдал только занятия с опубликованным планом;
 * - «👁 View» тренера — сервер отдал тренерский список, оставляем то, что увидел бы участник:
 *   опубликованный план;
 * - тренер — занятие, у которого есть план дорожек (черновик тоже: стол тренера его покажет).
 */
export type PlanViewer = 'member' | 'preview' | 'manager';

export function planViewer(manages: boolean, lanePreview: boolean): PlanViewer {
  if (manages) return 'manager';
  return lanePreview ? 'preview' : 'member';
}

/** Прошедшие с планом, который этот зритель может открыть. `null` — список ещё не пришёл. */
export function viewablePast(
  list: PastTraining[] | null,
  lanePlans: LanePlanSummary[] | null,
  viewer: PlanViewer,
): PastTraining[] | null {
  if (!list) return null;
  if (viewer === 'member') return list;
  const ok = (id: string | null | undefined) => lanePlans?.some((p) => p.session_id === id
    && (viewer === 'manager' || p.status === 'published')) ?? false;
  return list.filter((p) => ok(p.session.id));
}

/** Ключ занятия `yyyy-MM-dd-HHmm` тренировки ленты; без времени начала — `null`. */
export function feedSessionKey(s: { date: string; start?: string | null }): string | null {
  const m = /^(\d{1,2}):(\d{2})/.exec(s.start ?? '');
  if (!m) return null;
  return `${s.date.slice(0, 10)}-${m[1].padStart(2, '0')}${m[2]}`;
}

/**
 * План для тренировки ленты: точно по ключу занятия; у тренировки без времени (времена залиты
 * импортом — у них есть только дата) — по дате, если план в этот день ровно один. Два занятия в
 * день и тренировка без времени — какое из них, не угадать: кнопки нет.
 */
export function feedPlanKey(s: { date: string; start?: string | null }, keys: ReadonlySet<string>): string | null {
  const exact = feedSessionKey(s);
  if (exact) return keys.has(exact) ? exact : null;
  const day = s.date.slice(0, 10);
  const sameDay = [...keys].filter((k) => k.slice(0, 10) === day);
  return sameDay.length === 1 ? sameDay[0] : null;
}

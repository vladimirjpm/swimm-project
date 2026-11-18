import type { LanePlanEvent } from '../../../types';

/**
 * Лента «Since publishing» рабочего стола тренера (docs/plans/trainings-coach-flow-plan.md, CF8;
 * макет F7-mobile) — чистые функции: текст строки и время. Строки считает сервер
 * (`GET …/lane-plans/{sessionId}/events`); место — `null` не в плане, `0` Unassigned, N дорожка.
 *
 * Текст — три куска: до имени, имя (его вызывающий оборачивает в `<bdi>` — имена на иврите), после.
 */

export interface EventText {
  before: string;
  name: string;
  after: string;
}

/** «lane 3» / «Unassigned» / «off the plan». */
function placeLabel(place: number | null): string {
  if (place == null) return 'off the plan';
  return place === 0 ? 'Unassigned' : `lane ${place}`;
}

/** «You» / имя аккаунта / «Someone» (аккаунт удалён). */
function who(e: LanePlanEvent): string {
  return e.by_me ? 'You' : e.actor_name || 'Someone';
}

export function eventText(e: LanePlanEvent): EventText {
  const name = e.name || e.name_en;
  switch (e.kind) {
    case 'published':
      return { before: `${who(e)} published`, name: '', after: e.on_lanes != null ? ` · ${e.on_lanes} on lanes` : '' };
    case 'going': {
      const to = e.to_lane ? `lane ${e.to_lane}` : 'not on a lane yet';
      return e.by_coach
        ? { before: `${who(e)} marked `, name, after: ` going → ${to}` }
        : { before: '', name, after: ` answered Going → ${to}` };
    }
    case 'cant_come': {
      const from = e.from_lane ? ` → off lane ${e.from_lane}` : ' → off the plan';
      return e.by_coach
        ? { before: `${who(e)} marked `, name, after: ` can't come${from}` }
        : { before: '', name, after: ` can't come${from}` };
    }
    case 'moved':
    default:
      if (e.to_lane == null) return { before: `${who(e)} took `, name, after: ' off the plan' };
      if (e.from_lane == null) return { before: `${who(e)} put `, name, after: ` ${e.to_lane === 0 ? 'in Unassigned' : `on lane ${e.to_lane}`}` };
      return { before: `${who(e)} moved `, name, after: ` ${placeLabel(e.from_lane)} → ${placeLabel(e.to_lane)}` };
  }
}

/** Изменений — всё, кроме строки «опубликован». */
export function changesCount(events: readonly LanePlanEvent[]): number {
  return events.filter((e) => e.kind !== 'published').length;
}

/** «2 changes» / «1 change» / «No changes». */
export function changesLabel(count: number): string {
  if (count === 0) return 'No changes';
  return `${count} ${count === 1 ? 'change' : 'changes'}`;
}

const TZ = 'Asia/Jerusalem';

const dayKey = (d: Date) =>
  new Intl.DateTimeFormat('en-CA', { timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit' }).format(d);

/**
 * Время строки по Израилю: «21:50»; не сегодня — «Sun 21:50». `now` — параметром (тесты).
 */
export function eventTime(at: string, now: Date = new Date()): string {
  const d = new Date(at);
  const time = new Intl.DateTimeFormat('en-GB', { timeZone: TZ, hour: '2-digit', minute: '2-digit', hour12: false }).format(d);
  if (dayKey(d) === dayKey(now)) return time;
  const weekday = new Intl.DateTimeFormat('en-US', { timeZone: TZ, weekday: 'short' }).format(d);
  return `${weekday} ${time}`;
}

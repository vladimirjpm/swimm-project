import { todayInIsrael } from '../../lane-plans-api';
import type { HubGroupDetails, NextTraining } from '../../../types';
import { addDays, formatSheetDate } from '../sheets/link-helpers';

/**
 * Чистые функции вида Next участника (Т5, docs/tasks/trainings-tab-t5-sonnet.md): дата строки,
 * «Tonight», отсчёт «IN 2 H», «Back on». Даты — строки `yyyy-MM-dd` по Израилю: день недели и
 * сдвиги считаем в UTC (`Date.UTC`), «сейчас» — через `Intl.DateTimeFormat` с поясом Израиля,
 * а не часами машины зрителя.
 */

const WEEKDAY = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** Дней от `from` до `to` (обе `yyyy-MM-dd`), может быть отрицательным. */
function daysBetween(from: string, to: string): number {
  const [fy, fm, fd] = from.split('-').map(Number);
  const [ty, tm, td] = to.split('-').map(Number);
  return Math.round((Date.UTC(ty, tm - 1, td) - Date.UTC(fy, fm - 1, fd)) / 86400000);
}

/** Сейчас по Израилю: дата `yyyy-MM-dd` и минуты от полуночи. */
export function nowInIsrael(): { date: string; minutes: number } {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Jerusalem', hourCycle: 'h23', hour: '2-digit', minute: '2-digit',
  }).formatToParts(new Date());
  const hour = Number(parts.find((p) => p.type === 'hour')?.value ?? 0);
  const minute = Number(parts.find((p) => p.type === 'minute')?.value ?? 0);
  return { date: todayInIsrael(), minutes: hour * 60 + minute };
}

/** «20:00» → минуты от полуночи; не время → null. */
function toMinutes(time: string | null | undefined): number | null {
  const m = /^(\d{1,2}):(\d{2})/.exec(time ?? '');
  return m ? Number(m[1]) * 60 + Number(m[2]) : null;
}

/** Сегодня / завтра по Израилю; иначе null. */
export function relativeDay(date: string): 'today' | 'tomorrow' | null {
  const d = daysBetween(todayInIsrael(), date);
  return d === 0 ? 'today' : d === 1 ? 'tomorrow' : null;
}

/** «Wed 30 Sep · 20:00» — дата строки ответа. */
export function rowDateLabel(date: string, start: string): string {
  return `${formatSheetDate(date)} · ${start}`;
}

/** «Wed 30» — день недели и число (шапка занятия на телефоне). */
export function dayNumLabel(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  return `${WEEKDAY[new Date(Date.UTC(y, m - 1, d)).getUTCDay()]} ${d}`;
}

/** «20:00–21:00» / «20:00». */
export function timeRangeLabel(start: string, end?: string | null): string {
  return end ? `${start}–${end}` : start;
}

/** Префикс подписи ответа: «Tonight · » / «Tomorrow · » (по Израилю) либо пусто. */
export function relativePrefix(date: string): string {
  const rel = relativeDay(date);
  return rel === 'today' ? 'Tonight · ' : rel === 'tomorrow' ? 'Tomorrow · ' : '';
}

/**
 * Отсчёт до занятия: сегодня — «IN 2 H» / «IN 40 MIN» (меньше часа) / «NOW» (уже началось, до
 * конца); завтра — «TOMORROW»; дальше — «IN N DAYS». Занятие в прошлом — пусто.
 */
export function untilLabel(date: string, start: string, end?: string | null): string {
  const now = nowInIsrael();
  const days = daysBetween(now.date, date);
  if (days < 0) return '';
  if (days === 1) return 'TOMORROW';
  if (days > 1) return `IN ${days} DAYS`;
  const startMin = toMinutes(start);
  if (startMin == null) return 'TODAY';
  const diff = startMin - now.minutes;
  if (diff <= 0) {
    const endMin = toMinutes(end);
    return endMin != null && now.minutes >= endMin ? 'TODAY' : 'NOW';
  }
  if (diff < 60) return `IN ${diff} MIN`;
  return `IN ${Math.floor(diff / 60)} H`;
}

/** Кикер шапки на десктопе: «Next · tonight · in 2 h» (части — только те, что есть). */
export function desktopKicker(date: string, start: string, end?: string | null): string {
  const parts = ['Next'];
  const rel = relativeDay(date);
  if (rel === 'today') parts.push('tonight');
  else if (rel === 'tomorrow') parts.push('tomorrow');
  const until = untilLabel(date, start, end);
  // «TOMORROW» уже названо отдельной частью — не повторяем.
  if (until && !(rel === 'tomorrow' && until === 'TOMORROW')) parts.push(until.toLowerCase());
  return parts.join(' · ');
}

/**
 * День возвращения с перерыва: `until` — ПОСЛЕДНИЙ день перерыва, возвращение — на следующий
 * («Thu 15 Oct»).
 */
export function backOnLabel(until: string): string {
  return formatSheetDate(addDays(until, 1));
}

/** Задание дорожки одной строкой: переводы строк → « · »; пусто — null. */
export function workoutOneLine(workout: string | null | undefined): string | null {
  const text = (workout ?? '').split(/\r?\n/).map((l) => l.trim()).filter(Boolean).join(' · ');
  return text || null;
}

/**
 * Сколько занятий-строк вмещает вид Next: три не отменённых + отменённые между ними (Р9). Сервер
 * отдаёт именно столько (`GET …/next-trainings`); потолок клиента — на случай, когда отмен много.
 */
export const NEXT_ENTRIES_MAX = 6;

/**
 * Ближайшие занятия группы по порядку: не отменённые (до трёх) и отменённые между ними — те же
 * строки, что в `group.next_trainings`. Первое НЕ отменённое — `group.next_training`. Нет поля —
 * одно, нет занятия — пусто.
 */
export function nextSessions(group: HubGroupDetails): NextTraining[] {
  const list = group.next_trainings ?? (group.next_training ? [group.next_training] : []);
  return list.slice(0, NEXT_ENTRIES_MAX);
}

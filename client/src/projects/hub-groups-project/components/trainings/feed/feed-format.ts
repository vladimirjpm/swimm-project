import type { TrainingSession, TrainingSet } from '../../../types';
import { DEFAULT_TRAINING_TYPE, trainingTypeOf } from '../training-types';

/**
 * Чистые функции подписей ленты тренировок (docs/tasks/trainings-tab-t4-sonnet.md): мета
 * карточки, километры, короткий стиль, сводка сета, колонка даты. Без React и без обращений
 * к данным — считаем только из пришедшего.
 *
 * Даты — строки `yyyy-MM-dd` (Израиль): день недели и месяц считаем через `Date.UTC` +
 * `getUTC*`, иначе часовой пояс машины сдвинет день.
 */

const WEEKDAY = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTH_FULL = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

function parts(date: string): [number, number, number] {
  const [y, m, d] = date.split('-').map(Number);
  return [y, m, d];
}

/** «Tue» — день недели записи. */
export function weekdayShort(date: string): string {
  const [y, m, d] = parts(date);
  return WEEKDAY[new Date(Date.UTC(y, m - 1, d)).getUTCDay()];
}

/** «29» — число месяца без нуля. */
export function dayOfMonth(date: string): number {
  return parts(date)[2];
}

/** «SEPTEMBER 2026» — разделитель месяца над записью. */
export function monthLabel(date: string): string {
  const [y, m, d] = parts(date);
  const month = new Date(Date.UTC(y, m - 1, d)).getUTCMonth();
  return `${MONTH_FULL[month].toUpperCase()} ${y}`;
}

/** Месяц записи как ключ сравнения «тот же / другой». */
export function monthKey(date: string): string {
  return date.slice(0, 7);
}

/** «2.4» — километры из метров (объём одного пловца). */
export function formatKm(meters: number): string {
  return (meters / 1000).toFixed(1);
}

/** Мета под названием: «20:00 · ⏱ times», «19:30 · no times · 3 links» (тренер), «nothing recorded». */
export function trainingMeta(session: TrainingSession, manages: boolean): string {
  const out: string[] = [];
  // Тип — первым, кроме обычной тренировки в воде: значок на каждой карточке был бы шумом.
  const type = trainingTypeOf(session);
  if (type.id !== DEFAULT_TRAINING_TYPE) out.push(`${type.glyph} ${type.label.toLowerCase()}`);
  if (session.start) out.push(session.start);
  const hasMedia = session.media.length > 0;
  if (session.has_results) out.push('⏱ times');
  else if (hasMedia) out.push('no times');
  else out.push('nothing recorded');
  if (manages) {
    if (session.has_results && session.stats) out.push(`${session.stats.swimmers} swimmers`);
    if (hasMedia) out.push(session.media.length === 1 ? '1 link' : `${session.media.length} links`);
  }
  return out.join(' · ');
}

/** `freestyle → free`, `backstroke → back`, `breaststroke → breast`, `butterfly → fly`, медли → `IM`. */
export function shortStyle(style: string): string {
  const s = style.trim().toLowerCase();
  switch (s) {
    case 'freestyle': return 'free';
    case 'backstroke': return 'back';
    case 'breaststroke': return 'breast';
    case 'butterfly': return 'fly';
    case 'medley':
    case 'individual medley':
    case 'im': return 'IM';
    default: return s;
  }
}

/** « · 4×100 free» — если у всех строк сета одна дистанция и стиль; иначе пустая строка. */
export function setSummary(set: Pick<TrainingSet, 'rows'>): string {
  const rows = set.rows;
  if (rows.length === 0) return '';
  const { distance, style } = rows[0];
  if (!rows.every((r) => r.distance === distance && r.style === style)) return '';
  const reps = new Set(rows.map((r) => r.order_no)).size;
  return ` · ${reps}×${distance} ${shortStyle(style)}`;
}

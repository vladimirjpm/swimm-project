import { seasonStartYear } from '../../../utils/helpers/season-helper';
import type { CalendarMeet, CalendarMonth, MeetType } from './calendar-types';

/*
 * Чистые функции календаря: даты, месяцы, фильтры, счётчики. Без React и без обращений к
 * браузеру, кроме Intl в `israelToday`. Даты ответа ('yyyy-MM-dd') сравниваем СТРОКАМИ или
 * считаем через UTC — `new Date('2026-10-09')` с локальным `getDate()` в поясе западнее UTC
 * даёт 8-е.
 */

const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

const pad2 = (n: number) => String(n).padStart(2, '0');

/** «Сегодня» по Asia/Jerusalem в виде yyyy-MM-dd (en-CA печатает именно так). */
export function israelToday(): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Jerusalem',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
}

/** Месяц → ключ запроса/сравнения: {2026,10} → '2026-10'. */
export function monthKey(m: CalendarMonth): string {
  return `${m.year}-${pad2(m.month)}`;
}

/** Ключ 'YYYY-MM' → месяц; мусор и месяц вне 1–12 → null. */
export function parseMonthKey(s: string | null | undefined): CalendarMonth | null {
  const match = /^(\d{4})-(\d{2})$/.exec(s ?? '');
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  if (month < 1 || month > 12) return null;
  return { year, month };
}

/** Месяц ± n (n любого знака), с переносом через границу года. */
export function addMonths(m: CalendarMonth, n: number): CalendarMonth {
  const index = m.year * 12 + (m.month - 1) + n;
  return { year: Math.floor(index / 12), month: ((index % 12) + 12) % 12 + 1 };
}

/** Год начала сезона, которому принадлежит месяц (по его 1-му числу). */
export function seasonOfMonth(m: CalendarMonth): number {
  return seasonStartYear(new Date(m.year, m.month - 1, 1));
}

/** 12 месяцев сезона по порядку: сентябрь года начала … август следующего. */
export function seasonMonths(season: number): CalendarMonth[] {
  return Array.from({ length: 12 }, (_, i) => addMonths({ year: season, month: 9 }, i));
}

/** «October 2026». */
export function monthLabel(m: CalendarMonth): string {
  return `${MONTH_NAMES[m.month - 1]} ${m.year}`;
}

/** Короткое имя месяца: «Oct». */
export function monthShort(m: CalendarMonth): string {
  return MONTH_NAMES[m.month - 1].slice(0, 3);
}

/** «Season 2026/27». */
export function seasonTitle(season: number): string {
  return `Season ${season}/${pad2((season + 1) % 100)}`;
}

/** Дата yyyy-MM-dd лежит в месяце. */
export function isDayInMonth(iso: string, m: CalendarMonth): boolean {
  return iso.startsWith(`${monthKey(m)}-`);
}

/**
 * Meet в месяце — если хоть один его день (`days[].date`) в этом месяце: старт, идущий через
 * границу месяцев, считается в обоих. Одно правило для счётчиков и всех видов.
 */
export function meetInMonth(meet: CalendarMeet, m: CalendarMonth): boolean {
  return meet.days.some((d) => isDayInMonth(d.date, m));
}

/** Meet в дне — дата есть в `days`. */
export function meetOnDay(meet: CalendarMeet, iso: string): boolean {
  return meet.days.some((d) => d.date === iso);
}

/** Фильтр по типу; пустой фильтр = All. */
export function applyTypeFilter(meets: CalendarMeet[], filter: MeetType[]): CalendarMeet[] {
  if (filter.length === 0) return meets;
  return meets.filter((meet) => filter.includes(meet.type));
}

/**
 * Счётчики чипов: сколько meets каждого типа в переданной области (месяц или сезон) —
 * вызывающий передаёт meets области БЕЗ учёта фильтра по типу. `all` — всего.
 */
export function typeCounts(meets: CalendarMeet[]): Record<MeetType | 'all', number> {
  const counts: Record<MeetType | 'all', number> = { all: meets.length, pool: 0, open_water: 0, artistic: 0 };
  for (const meet of meets) counts[meet.type] += 1;
  return counts;
}

/**
 * Выбранный день месяца по умолчанию. В месяце «сегодня» — ближайший день с meets начиная с
 * сегодня, иначе последний день с meets до сегодня; в другом месяце — первый день с meets;
 * meets нет — null. `meets` — уже отфильтрованные по типу.
 */
export function defaultSelectedDay(meets: CalendarMeet[], m: CalendarMonth, today: string): string | null {
  const days = new Set<string>();
  for (const meet of meets) {
    for (const d of meet.days) if (isDayInMonth(d.date, m)) days.add(d.date);
  }
  if (days.size === 0) return null;

  const sorted = Array.from(days).sort();
  if (isDayInMonth(today, m)) {
    const upcoming = sorted.find((d) => d >= today);
    return upcoming ?? sorted[sorted.length - 1];
  }
  return sorted[0];
}

/**
 * Границы листания: от сентября `min(seasons)` до августа `max(seasons)`.
 * Пустой список сезонов → null (данные ещё не пришли).
 */
export function navBounds(seasons: number[]): { min: CalendarMonth; max: CalendarMonth } | null {
  if (seasons.length === 0) return null;
  return {
    min: { year: Math.min(...seasons), month: 9 },
    max: { year: Math.max(...seasons) + 1, month: 8 },
  };
}

/** Индекс месяца для сравнения: больше = позже. */
export function monthIndex(m: CalendarMonth): number {
  return m.year * 12 + m.month;
}

/** «24 Oct» из yyyy-MM-dd (без Date, чтобы не зависеть от пояса). */
export function dayLabel(iso: string): string {
  const [, mm, dd] = iso.split('-');
  return `${dd} ${MONTH_NAMES[Number(mm) - 1].slice(0, 3)}`;
}

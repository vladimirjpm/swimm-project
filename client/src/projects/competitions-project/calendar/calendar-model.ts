import { seasonStartYear } from '../../../utils/helpers/season-helper';
import { routes } from '../../../utils/routes';
import type { CalendarGroup, CalendarMeet, CalendarMonth, MeetType } from './calendar-types';

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

/** Месяц «сегодня» (today — yyyy-MM-dd из ответа). */
export function isCurrentMonth(m: CalendarMonth, today: string): boolean {
  return isDayInMonth(today, m);
}

/** Месяц целиком раньше месяца «сегодня». */
export function isPastMonth(m: CalendarMonth, today: string): boolean {
  return monthKey(m) < today.slice(0, 7);
}

/**
 * Чем помечаем чемпионат: бейджем группы (только чемпионат БАССЕЙНА с группой) или текстом
 * («Open water championship», «Artistic championship», «Championship» — бассейн без группы).
 * Вызывать для meets с `is_championship`. Одно правило для Year, Month и кубов (Б3–Б6).
 */
export type ChampMark =
  | { kind: 'badge'; group: CalendarGroup }
  | { kind: 'text'; text: string };

export function champMark(meet: CalendarMeet): ChampMark {
  if (meet.type === 'pool') {
    return meet.group ? { kind: 'badge', group: meet.group } : { kind: 'text', text: 'Championship' };
  }
  return { kind: 'text', text: meet.type === 'open_water' ? 'Open water championship' : 'Artistic championship' };
}

const WEEKDAYS_SHORT = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const WEEKDAYS_LONG = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** Разбор yyyy-MM-dd → [год, месяц 1–12, день]; день недели считаем по UTC, не по локали. */
function parseIso(iso: string): [number, number, number] {
  const [y, m, d] = iso.split('-').map(Number);
  return [y, m, d];
}

/** День недели 0–6 (0 = воскресенье) для yyyy-MM-dd, по UTC. */
function weekdayOf(iso: string): number {
  const [y, m, d] = parseIso(iso);
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay();
}

/**
 * Сетка месяца: недели (с воскресенья) × 7; ISO-даты, а клетки до 1-го и после последнего
 * числа — null.
 */
export function monthGrid(m: CalendarMonth): (string | null)[][] {
  const offset = new Date(Date.UTC(m.year, m.month - 1, 1)).getUTCDay();
  const days = new Date(Date.UTC(m.year, m.month, 0)).getUTCDate();
  const cells: (string | null)[] = Array(offset).fill(null);
  for (let d = 1; d <= days; d++) cells.push(`${monthKey(m)}-${pad2(d)}`);
  while (cells.length % 7 !== 0) cells.push(null);
  const weeks: (string | null)[][] = [];
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7));
  return weeks;
}

/** Предыдущая календарная дата (yyyy-MM-dd), по UTC. */
function shiftIso(iso: string, delta: number): string {
  const [y, m, d] = parseIso(iso);
  const t = new Date(Date.UTC(y, m - 1, d + delta));
  return `${t.getUTCFullYear()}-${pad2(t.getUTCMonth() + 1)}-${pad2(t.getUTCDate())}`;
}

/**
 * Форма плашки meet в ячейке дня. `first` — день первый в `meet.days` (подпись = имя, иначе
 * «↳ Day N»); `dayNo` — порядковый номер даты в `meet.days` (1-based, дни бывают не подряд, так
 * что это НЕ разница дат); `joinLeft/joinRight` — соседняя календарная дата тоже в `days`
 * (плашка тянется на соседнюю ячейку и там плоская).
 */
export function chipShape(
  meet: CalendarMeet,
  iso: string,
): { first: boolean; joinLeft: boolean; joinRight: boolean; dayNo: number } {
  const index = Math.max(0, meet.days.findIndex((d) => d.date === iso));
  return {
    first: index === 0,
    joinLeft: meetOnDay(meet, shiftIso(iso, -1)),
    joinRight: meetOnDay(meet, shiftIso(iso, 1)),
    dayNo: index + 1,
  };
}

/** Meets, у которых в этот день есть запись в `days`, в исходном порядке. */
export function meetsOnDay(meets: CalendarMeet[], iso: string): CalendarMeet[] {
  return meets.filter((meet) => meetOnDay(meet, iso));
}

/** Подпись ячейки для скринридера: «Saturday, 24 October — 4 meets» / «— no meets». */
export function dayAriaLabel(iso: string, n: number): string {
  const [, m, d] = parseIso(iso);
  const tail = n === 0 ? 'no meets' : `${n} ${n === 1 ? 'meet' : 'meets'}`;
  return `${WEEKDAYS_LONG[weekdayOf(iso)]}, ${d} ${MONTH_NAMES[m - 1]} — ${tail}`;
}

/**
 * Куда ведёт куб — одно место правды для ссылки, подписи действия и разметки.
 *  • cancelled — никуда (null);
 *  • postponed (призрак на прежних датах) — страница федерации `source_url` (внешняя), без неё — null;
 *  • upcoming из входящих: с заявками — наша страница старта, без заявок — страница федерации
 *    (`source_url`, внешняя); если и её нет — наша страница старта как запасной вариант;
 *  • event → результаты события, competition → соревнование.
 */
export function meetLink(meet: CalendarMeet): { href: string; external: boolean } | null {
  if (meet.status === 'cancelled') return null;
  if (meet.status === 'postponed') return meet.source_url ? { href: meet.source_url, external: true } : null;
  if (meet.kind === 'event') return { href: `${routes.results()}?eventId=${meet.id}`, external: false };
  if (meet.kind === 'competition') return { href: routes.competition(meet.id), external: false };
  if (!meet.has_start_list && meet.source_url) return { href: meet.source_url, external: true };
  return { href: routes.competitionUpcoming(meet.id), external: false };
}

/** Адрес куба (без признака «внешняя»); null — куб не ссылка. */
export function meetHref(meet: CalendarMeet): string | null {
  return meetLink(meet)?.href ?? null;
}

/**
 * Плитка даты куба: день недели (`SAT` / `SAT–SUN`), число (`24` / `24–25`), месяц (`Oct` /
 * `Oct–Nov` при переходе через границу месяцев). По `start`/`end`, не по `days`.
 */
export function dateTile(meet: CalendarMeet): { wd: string; day: string; mon: string } {
  const [, sm, sd] = parseIso(meet.start);
  const [, em, ed] = parseIso(meet.end);
  const wdS = WEEKDAYS_SHORT[weekdayOf(meet.start)].toUpperCase();
  const wdE = WEEKDAYS_SHORT[weekdayOf(meet.end)].toUpperCase();
  const monS = MONTH_NAMES[sm - 1].slice(0, 3);
  const monE = MONTH_NAMES[em - 1].slice(0, 3);
  if (meet.start === meet.end) return { wd: wdS, day: String(sd), mon: monS };
  if (sm === em) return { wd: `${wdS}–${wdE}`, day: `${sd}–${ed}`, mon: monS };
  // Через границу месяцев «23–20 / May–Jul» читается как бессмыслица — крупно день начала,
  // конец целиком строкой месяца: «23 / May – 20 Jul».
  return { wd: `${wdS}–${wdE}`, day: String(sd), mon: `${monS} – ${ed} ${monE}` };
}

/** Подпись даты куба для aria-label: «Sat–Sun 24–25 Oct». */
export function dateRangeLabel(meet: CalendarMeet): string {
  const t = dateTile(meet);
  const wd = t.wd
    .split('–')
    .map((w) => w[0] + w.slice(1).toLowerCase())
    .join('–');
  return `${wd} ${t.day} ${t.mon}`;
}

/** «21 Nov» из yyyy-MM-dd — без нуля впереди (тег Postponed, подписи дней). */
export function shortDate(iso: string): string {
  const [, m, d] = parseIso(iso);
  return `${d} ${MONTH_NAMES[m - 1].slice(0, 3)}`;
}

export type StatusTone = 'live' | 'startlist' | 'progress' | 'complete' | 'cancelled' | 'postponed' | 'noresults';

/** Тег статуса куба; null — без тега (`done` и прочее). */
export function statusTag(meet: CalendarMeet): { text: string; tone: StatusTone } | null {
  if (meet.status === 'live') return { text: 'Live', tone: 'live' };
  if (meet.has_start_list && meet.status === 'upcoming') return { text: 'Startlist', tone: 'startlist' };
  if (meet.status === 'in_progress') return { text: 'In progress', tone: 'progress' };
  if (meet.status === 'complete') return { text: 'Complete', tone: 'complete' };
  if (meet.status === 'cancelled') return { text: 'Cancelled', tone: 'cancelled' };
  // Прошедший старт из входящих, который мы не затянули: честно говорим, что результатов у нас нет.
  if (meet.kind === 'upcoming' && meet.status === 'done' && meet.days_loaded === 0) {
    return { text: 'No results', tone: 'noresults' };
  }
  if (meet.status === 'postponed') {
    return { text: meet.postponed_to ? `Postponed → ${shortDate(meet.postponed_to)}` : 'Postponed', tone: 'postponed' };
  }
  return null;
}

/** Подпись действия в низу куба (сам куб — ссылка целиком); null — действия нет. */
export function actionLabel(meet: CalendarMeet): string | null {
  const link = meetLink(meet);
  if (!link) return null;
  if (link.external) return 'Details ↗';
  if (meet.status === 'postponed') return 'Details →';
  if (meet.kind === 'upcoming') return meet.has_start_list ? 'Startlist →' : 'Details →';
  if (meet.status === 'in_progress') return `Day ${meet.days_loaded} results →`;
  if (meet.status === 'complete') return 'Full results →';
  if (meet.status === 'live' || meet.status === 'done') return 'Results →';
  return 'Details →';
}

/** Многодневка в процессе (частично загружена) — куб на 2 колонки с полосой дней. */
export function isWideCube(meet: CalendarMeet): boolean {
  return meet.status === 'in_progress';
}

/** Индекс недели сетки месяца (`monthGrid`), в которой лежит дата; -1 — нет. */
export function weekIndexOf(grid: (string | null)[][], iso: string | null): number {
  if (!iso) return -1;
  return grid.findIndex((week) => week.includes(iso));
}

/**
 * Подпись недели сетки месяца: «18–24 Oct»; через границу месяца — «28 Sep – 4 Oct».
 * Неделя сетки на краях месяца содержит null — реальные границы недели достраиваем от
 * первой/последней непустой клетки.
 */
export function weekLabel(week: (string | null)[]): string {
  const first = week.findIndex((d) => d !== null);
  const last = week.length - 1 - [...week].reverse().findIndex((d) => d !== null);
  if (first < 0) return '';
  const start = shiftIso(week[first]!, -first);
  const end = shiftIso(week[last]!, week.length - 1 - last);
  const [, sm, sd] = parseIso(start);
  const [, em, ed] = parseIso(end);
  const monE = MONTH_NAMES[em - 1].slice(0, 3);
  return sm === em ? `${sd}–${ed} ${monE}` : `${sd} ${MONTH_NAMES[sm - 1].slice(0, 3)} – ${ed} ${monE}`;
}

/** Короткое имя дня недели для даты: «Sat» (по UTC). */
export function weekdayShortOf(iso: string): string {
  return WEEKDAYS_SHORT[weekdayOf(iso)];
}

/** Все даты месяца, в которые есть хотя бы один из meets, по возрастанию. */
export function daysWithMeets(meets: CalendarMeet[], m: CalendarMonth): string[] {
  const days = new Set<string>();
  for (const meet of meets) {
    for (const d of meet.days) if (isDayInMonth(d.date, m)) days.add(d.date);
  }
  return Array.from(days).sort();
}

/**
 * Название без даты: «ליגה 1 שוהם - 16/10/2026» → «ליגה 1 שוהם» (хендофф competitions_home-2). Дата
 * и так стоит в ячейке и в кубе, а в плашке 12px съедала само имя. ⚠ В данных дата — в КОНЦЕ
 * строки (бывает и без тире: «…וייסגל 30/10»); в RTL конец рисуется слева, поэтому в хендоффе она
 * описана как префикс. Чистим оба края; без даты название не трогаем.
 */
const DATE_PREFIX_RX = /^\d{1,2}\/\d{1,2}(\/\d{2,4})?\s*[-–]\s*/;
const DATE_SUFFIX_RX = /\s*[-–]?\s*\d{1,2}\/\d{1,2}(\/\d{2,4})?\s*$/;

export function meetDisplayName(name: string): string {
  const cleaned = name.replace(DATE_PREFIX_RX, '').replace(DATE_SUFFIX_RX, '').trim();
  return cleaned.length > 0 ? cleaned : name;
}

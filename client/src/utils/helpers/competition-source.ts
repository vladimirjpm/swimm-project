// Общие хелперы для элементов /api/competitions (события/соревнования), вынесены из
// filter-data-source-ddl.tsx — используются и селектором, и секцией Meets на competitions.tsx.

// Элемент /api/competitions: событие (свёрнуто в одну запись) или однодневное соревнование.
export type CompetitionSource = {
  kind: 'event' | 'competition';
  id: number;
  name: string;
  date: string; // dd/MM/yyyy
  date_end?: string | null;
  pool_type: string;
  /** Канонический таб; null — только «All» + кастомные табы по categories. */
  category: 'young8_11' | 'junior' | 'adults' | 'masters' | null;
  /** Полное членство — сырые Category.Key из БД (включая кастомные, напр. result-maccabiah). */
  categories?: string[];
  status: 'live' | 'upcoming' | 'done';
  day_count: number;
  result_count: number;
  /** даты дней (dd/MM/yyyy, по возрастанию) — опции фильтра по дню в paged-режиме */
  day_dates: string[];
};

const MONTHS_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const MONTHS_FULL = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

export const parseDate = (d: string): Date | null => {
  const [day, month, year] = d.split('/').map(Number);
  if (!day || !month || !year) return null;
  return new Date(year, month - 1, day);
};

// «16 Jun» / «1–3 Jul» / «28 Jun – 2 Jul» / для upcoming — «starts 12 Jul»
export const dateLabel = (src: CompetitionSource): string => {
  const start = parseDate(src.date);
  if (!start) return src.date;
  const startLabel = `${start.getDate()} ${MONTHS_SHORT[start.getMonth()]}`;
  if (src.status === 'upcoming') return `starts ${startLabel}`;
  const end = src.date_end ? parseDate(src.date_end) : null;
  if (end && end.getTime() !== start.getTime()) {
    return end.getMonth() === start.getMonth()
      ? `${start.getDate()}–${end.getDate()} ${MONTHS_SHORT[end.getMonth()]}`
      : `${startLabel} – ${end.getDate()} ${MONTHS_SHORT[end.getMonth()]}`;
  }
  return startLabel;
};

export const monthLabel = (src: CompetitionSource): string => {
  const d = parseDate(src.date);
  return d ? `${MONTHS_FULL[d.getMonth()]} ${d.getFullYear()}` : '';
};

export const sourceUrlParam = (src: CompetitionSource): [string, string] =>
  src.kind === 'event' ? ['eventId', String(src.id)] : ['competitionId', String(src.id)];

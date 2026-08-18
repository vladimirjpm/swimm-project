// Общие хелперы для элементов /api/competitions (события/соревнования), вынесены из
// filter-data-source-ddl.tsx — используются и селектором, и секцией Meets на competitions.tsx.

import CategoryHelper from './category-helper';

// Элемент /api/competitions: событие (свёрнуто в одну запись) или однодневное соревнование.
export type CompetitionSource = {
  kind: 'event' | 'competition';
  id: number;
  name: string;
  date: string; // dd/MM/yyyy
  date_end?: string | null;
  pool_type: string;
  /** Канонический таб; null — только «All» + кастомные табы по categories. */
  category: 'kids8_11' | 'young11_14' | 'juniors' | 'adults' | 'masters' | null;
  /** Полное членство — сырые Category.Key из БД (включая кастомные, напр. result-maccabiah). */
  categories?: string[];
  status: 'live' | 'upcoming' | 'done';
  day_count: number;
  result_count: number;
  /** даты дней (dd/MM/yyyy, по возрастанию) — опции фильтра по дню в paged-режиме */
  day_dates: string[];
  /** Чемпионат Израиля — ручной флаг из админки (Competition.IsChampionship). */
  is_championship?: boolean;
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

// --- Плитка соревнования (design_handoff_competition_overview3, вариант 11c) ---
// Данных для плитки в /api/competitions нет — выводим эвристикой из категории, даты и
// названия. Слот, который не удалось определить, НЕ рендерится (см. COMPETITION-TILE.md).

export type CompetitionTileData = {
  /**
   * Буква-бейдж категории: 'K' Kids, 'Y' Youth, 'J' Juniors, 'M' Masters; '' — категория
   * не определена. Источник — `Category.Badge` из БД через `CategoryHelper`, НЕ хардкод:
   * бейджи правятся в /Admin/Categories, и до 2026-07-30 здесь стояло `category ? 'K' : 'M'`,
   * из-за чего Youth и Juniors тоже показывались как «K».
   */
  letter: string;
  /** Возрастная лента: '8-11' | '11-14'; null — ленты нет. */
  ageGroup: string | null;
  /** '❄️' зима / '☀️' лето; null — сезон не определён. */
  season: '❄️' | '☀️' | null;
  /** Тип соревнования = чемпионат → кубок в полосе. */
  isChampionship: boolean;
};

/** Возрастная лента по канонической категории: только детские ступени. */
const AGE_GROUP_BY_CATEGORY: Record<string, string> = {
  kids8_11: '8-11',
  young11_14: '11-14',
};

// Слово-маркер важнее даты: «Winter Championship» в апреле — всё-таки зимний чемпионат.
const WINTER_RX = /winter|חורף/i;
const SUMMER_RX = /summer|קיץ/i;
/**
 * Чемпионат = чемпионат ИЗРАИЛЯ: в названии есть и «אליפות», и «ישראל» (либо английские
 * championship + israel). Слова проверяются по отдельности, а не подстрокой «אליפות ישראל»:
 * между ними бывает вставлен спонсор — «אליפות "ארנה" ישראל», «אליפות arena ישראל».
 * Клубные/городские «אליפות מכבי», «אליפות חדרה» — НЕ чемпионаты.
 * Зеркало серверного `CompetitionAdminRepository.IsChampionship` (фильтр в /Admin/Competitions):
 * меняешь здесь — меняй и там.
 */
/**
 * Чемпионат ли соревнование: источник истины — ручной флаг из админки (`is_championship`).
 * Эвристика по названию остаётся фоллбеком для источников, где флага нет (старый кэш
 * /api/competitions, объекты, собранные из результатов) — см. [[isChampionshipName]].
 */
export const isChampionshipSource = (src: CompetitionSource | undefined | null): boolean =>
  src?.is_championship ?? isChampionshipName(src?.name);

export const isChampionshipName = (name: string | null | undefined): boolean => {
  if (!name) return false;
  const lower = name.toLowerCase();
  return (
    (name.includes('אליפות') && name.includes('ישראל'))
    || (lower.includes('championship') && lower.includes('israel'))
  );
};

export function competitionTileData(src: CompetitionSource | undefined | null): CompetitionTileData {
  const name = src?.name ?? '';
  const category = src?.category ?? null;

  let season: CompetitionTileData['season'] = null;
  if (WINTER_RX.test(name)) season = '❄️';
  else if (SUMMER_RX.test(name)) season = '☀️';
  else {
    const d = src ? parseDate(src.date) : null;
    if (d) {
      const m = d.getMonth() + 1; // 1..12
      season = m >= 11 || m <= 3 ? '❄️' : '☀️';
    }
  }

  return {
    letter: category ? CategoryHelper.getBadgeByCanonicalKeySync(category) ?? '' : '',
    ageGroup: category ? AGE_GROUP_BY_CATEGORY[category] ?? null : null,
    season,
    isChampionship: isChampionshipSource(src),
  };
}

export const sourceUrlParam = (src: CompetitionSource): [string, string] =>
  src.kind === 'event' ? ['eventId', String(src.id)] : ['competitionId', String(src.id)];

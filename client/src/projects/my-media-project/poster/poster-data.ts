// Данные плаката: ЧТО на нём напечатано. Чистые функции без React и без обращений к
// приложению (импорты — только типы), поэтому проверяются юнит-тестом `poster-data.test.mjs`.
// Откуда берутся входы (медаль, метка, значок, плитка) — `poster-source.ts`; какие заплывы
// отмечены — `poster-selection.ts`; как это нарисовано — `layouts/` и `parts/`.

import type { CompetitionTileData } from '../../../utils/helpers/competition-source';
import type { PrintEmblem, PrintMark, PrintPlace, PrintRecordKind } from '../print/print-data';

export type PosterLayout = 'A' | 'B' | 'C';

/** Заплыв пловца на соревновании — вход плаката. */
export interface PosterSwimSource {
  id: number;
  /** День заплыва, yyyy-MM-dd. */
  date: string;
  place: number | null;
  /** Медаль вручали (`isMedal`) — место ≠ медаль. */
  medal: boolean;
  styleName: string;
  /** Дистанция, готовая к печати (`50m`, `4X50`). */
  distanceLabel: string;
  isRelay: boolean;
  /** Время как на сайте: `00:49.61`. */
  time: string;
  /** Строка без времени (DNS / DQ / DNF) — на плакат не идёт. */
  failed: boolean;
  /** Одна метка заплыва — `topMark` (у `Under review` её нет, Р76). */
  mark: PrintMark | null;
}

/** Метка строки: коротко (раскладки A и C) и словами (таблица B). */
export interface PosterBadge {
  kind: PrintMark['kind'];
  short: string;
  long: string;
}

/** Строка плаката. */
export interface PosterRow {
  id: number;
  /** null — места в протоколе нет. */
  place: PrintPlace | null;
  styleName: string;
  distanceLabel: string;
  isRelay: boolean;
  timeText: string;
  badge: PosterBadge | null;
}

/** Счёт медалей и итог соревнования (раскладка B). */
export interface PosterSummary {
  gold: number;
  silver: number;
  bronze: number;
  /** `6 swims`. */
  swimsLabel: string;
  /** `1 age record`; рекордов нет — null. */
  recordsLabel: string | null;
}

/** Всё, что плакат печатает. */
export interface PosterData {
  competitionName: string;
  tile: CompetitionTileData;
  emblem: PrintEmblem;
  year: string;
  /** `חורף 2026`; сезон неизвестен — только год. */
  seasonLabel: string;
  /** `גילאי 8-11`; у категории без возрастной группы — null. */
  ageLabel: string | null;
  swimmerName: string;
  clubName: string | null;
  rows: PosterRow[];
  summary: PosterSummary;
}

/** Входы плаката: соревнование, пловец и все его заплывы. */
export interface PosterSource {
  competitionName: string;
  tile: CompetitionTileData;
  emblem: PrintEmblem;
  /** Первый день соревнования, yyyy-MM-dd — год плаката и строки сезона. */
  firstDay: string;
  swimmerName: string;
  clubName: string | null;
  swims: PosterSwimSource[];
}

/**
 * Время на плакате — без ведущих нулей (решение Влада 12.11.2026): `00:49.61` → `49.61`,
 * `01:37.05` → `1:37.05`. На сайте время печатает `UI_SwimTime` как есть; плакат — картинка.
 */
export function posterTime(time: string): string {
  return time.replace(/^(?:0+:)+/, '').replace(/^0(?=\d)/, '');
}

const RECORD_LONG: Record<PrintRecordKind, string> = {
  world: 'World record',
  national: 'National record',
  age: 'Age record',
  masters: 'Masters record',
};

export function posterBadge(mark: PrintMark | null): PosterBadge | null {
  if (!mark) return null;
  if (mark.kind === 'record') return { kind: 'record', short: 'REC', long: RECORD_LONG[mark.record] };
  return mark.kind === 'sb' ? { kind: 'sb', short: 'SB', long: 'SB' } : { kind: 'pb', short: 'PB', long: 'PB' };
}

/** Сезон плитки соревнования → слово строки сезона (иврит, как в макете). */
const SEASON_WORD: Record<string, string> = { winter: 'חורף', summer: 'קיץ' };

/** `חורף 2026`: сезон по тому же правилу, что значок плитки, и год соревнования. */
export function posterSeasonLabel(season: string | null, year: string): string {
  const word = season ? SEASON_WORD[season] : undefined;
  return word ? `${word} ${year}` : year;
}

/** `גילאי 8-11`; возрастной группы у категории нет (взрослые, мастерс) — строки нет. */
export function posterAgeLabel(ageGroup: string | null): string | null {
  return ageGroup ? `גילאי ${ageGroup}` : null;
}

/**
 * Заплывы, которые могут попасть на плакат: с временем (DNS / DQ / DNF не идут), по порядку
 * дней. Порядок внутри дня — как пришёл (сортировка устойчивая).
 */
export function posterSwims(swims: PosterSwimSource[]): PosterSwimSource[] {
  return swims.filter((s) => !s.failed).sort((a, b) => a.date.localeCompare(b.date));
}

export function posterRow(swim: PosterSwimSource): PosterRow {
  return {
    id: swim.id,
    place: swim.place == null ? null : { value: swim.place, medal: swim.medal },
    styleName: swim.styleName,
    distanceLabel: swim.distanceLabel,
    isRelay: swim.isRelay,
    timeText: posterTime(swim.time),
    badge: posterBadge(swim.mark),
  };
}

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

/** `1 age record`, `2 records` (виды разные) или null. */
function recordsLabel(swims: PosterSwimSource[]): string | null {
  const kinds = swims.flatMap((s) => (s.mark?.kind === 'record' ? [s.mark.record] : []));
  if (kinds.length === 0) return null;
  const same = kinds.every((k) => k === kinds[0]);
  return plural(kinds.length, same ? `${kinds[0]} record` : 'record');
}

/**
 * Счёт медалей — по ВСЕМ заплывам пловца с результатом, а не только по отмеченным для плаката:
 * это итог соревнования. Медаль — только там, где её вручали (место ≠ медаль).
 */
export function posterSummary(swims: PosterSwimSource[]): PosterSummary {
  const medals = (place: number) => swims.filter((s) => s.medal && s.place === place).length;
  return {
    gold: medals(1),
    silver: medals(2),
    bronze: medals(3),
    swimsLabel: plural(swims.length, 'swim'),
    recordsLabel: recordsLabel(swims),
  };
}

/** Плакат для отмеченных заплывов `selected`; порядок строк — по дням. */
export function buildPosterData(src: PosterSource, selected: ReadonlySet<number>): PosterData {
  const swims = posterSwims(src.swims);
  const year = src.firstDay.slice(0, 4);
  return {
    competitionName: src.competitionName,
    tile: src.tile,
    emblem: src.emblem,
    year,
    seasonLabel: posterSeasonLabel(src.tile.season, year),
    ageLabel: posterAgeLabel(src.tile.ageGroup),
    swimmerName: src.swimmerName,
    clubName: src.clubName || null,
    rows: swims.filter((s) => selected.has(s.id)).map(posterRow),
    summary: posterSummary(swims),
  };
}

/** Имя файла без расширения: первый день соревнования и раскладка. */
export function posterFileName(firstDay: string, layout: PosterLayout): string {
  return `swimhub-poster-${firstDay}-${layout}`;
}

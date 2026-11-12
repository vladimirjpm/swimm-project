// Данные обложки YouTube: ЧТО на ней напечатано. Чистые функции без React и без обращений к
// приложению (импорты — только типы), поэтому проверяются юнит-тестом `cover-data.test.mjs`.
// Откуда берутся входы (медаль, метки, плитка) — `cover-source.ts`; как это нарисовано —
// `layouts/` и `parts/`.

import type { CompetitionTileData } from '../../../utils/helpers/competition-source';

export type CoverLayout = 'A' | 'B' | 'C';

/** Что стоит на месте значка соревнования. */
export type CoverEmblem =
  | { kind: 'image'; url: string }
  | { kind: 'cup' }
  | { kind: 'empty' };

/** Место на обложке: число как в протоколе и отдельно — вручали ли за него медаль. */
export interface CoverPlace {
  value: number;
  medal: boolean;
}

/** Всё, что обложка печатает. */
export interface CoverData {
  competitionName: string;
  tile: CompetitionTileData;
  emblem: CoverEmblem;
  /** День заплыва: `5 NOV`. */
  dayLabel: string;
  /** Дни соревнования: `5–6 NOV`; у однодневного совпадает с `dayLabel`. */
  rangeLabel: string;
  year: string;
  /** null — места нет (DNS / DQ). */
  place: CoverPlace | null;
  styleName: string;
  /** `100m`; у эстафеты — как в протоколе (`4X50`). */
  distanceLabel: string;
  /** Время, как в протоколе, либо пометка DNS / DQ. */
  timeText: string;
  /** Одна метка достижения или null. */
  badge: string | null;
  swimmerName: string;
  clubName: string | null;
}

export interface CoverOptions {
  /** Строка «логотип клуба и имя пловца» (B и C; у A имя есть всегда). */
  showName: boolean;
  /** Место под фото включено (B и C). */
  photoSlot: boolean;
  /** Фото пловца — data-URL выбранного файла; null — не выбрано. */
  photo: string | null;
}

/** Вид рекорда — те же ключи, что у `UI_RecordBadge` (`RecordKind`). */
export type CoverRecordKind = 'world' | 'national' | 'age' | 'masters';

/** Метки достижений заплыва, уже посчитанные общим `swimMarks` (у `Under review` все пустые, Р76). */
export interface CoverMarks {
  record: { kind: CoverRecordKind } | null;
  sb: boolean;
  pb: boolean;
}

/** Входы обложки: поля заплыва и то, что о нём уже решили общие правила продукта. */
export interface CoverSource {
  competitionName: string;
  tile: CompetitionTileData;
  emblemUrl: string | null;
  isChampionship: boolean;
  /** День заплыва, yyyy-MM-dd. */
  date: string;
  /** Первый и последний день соревнования, yyyy-MM-dd. */
  firstDay: string;
  lastDay: string;
  place: number | null;
  /** Медаль вручали (`isMedal`) — место ≠ медаль. */
  medal: boolean;
  styleName: string;
  distance: string;
  time: string;
  /** Пометка протокола вместо времени (DNS / DQ); null — время есть. */
  failLabel: string | null;
  marks: CoverMarks;
  swimmerName: string;
  clubName: string | null;
}

const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];

/** yyyy-MM-dd → `5 NOV` (подписи обложки — на английском). */
export function coverDay(iso: string): string {
  const [, month, day] = iso.split('-').map(Number);
  return `${day} ${MONTHS[month - 1] ?? ''}`.trim();
}

/** Дни соревнования: `5–6 NOV`, через границу месяца — `30 OCT–1 NOV`. */
export function coverRange(fromIso: string, toIso: string): string {
  if (fromIso === toIso) return coverDay(fromIso);
  const [, fromMonth, fromDay] = fromIso.split('-').map(Number);
  const [, toMonth] = toIso.split('-').map(Number);
  return fromMonth === toMonth ? `${fromDay}–${coverDay(toIso)}` : `${coverDay(fromIso)}–${coverDay(toIso)}`;
}

/** Метка словами: в сетке YouTube сокращение `REC·AGE` не читается. */
const RECORD_LABEL: Record<CoverRecordKind, string> = {
  world: 'WORLD RECORD',
  national: 'NATIONAL RECORD',
  age: 'AGE RECORD',
  masters: 'MASTERS RECORD',
};

/** Одна метка на обложку, приоритет общий для продукта: рекорд > SB > PB. */
export function coverBadge(marks: CoverMarks): string | null {
  if (marks.record) return RECORD_LABEL[marks.record.kind];
  if (marks.sb) return 'SEASON BEST';
  return marks.pb ? 'PERSONAL BEST' : null;
}

/**
 * Что на месте значка (решение Влада 12.11.2026): есть значок серии — значок; значка нет, но
 * соревнование — чемпионат — кубок; иначе пусто (место при этом остаётся — это дело вёрстки).
 */
export function coverEmblem(emblemUrl: string | null, isChampionship: boolean): CoverEmblem {
  if (emblemUrl) return { kind: 'image', url: emblemUrl };
  return isChampionship ? { kind: 'cup' } : { kind: 'empty' };
}

/** `100` → `100m`; дистанцию эстафеты (`4X50`) печатаем как в протоколе. */
export function coverDistance(distance: string): string {
  return /^\d+$/.test(distance) ? `${distance}m` : distance;
}

export function buildCoverData(src: CoverSource): CoverData {
  return {
    competitionName: src.competitionName,
    tile: src.tile,
    emblem: coverEmblem(src.emblemUrl, src.isChampionship),
    dayLabel: coverDay(src.date),
    rangeLabel: coverRange(src.firstDay, src.lastDay),
    year: src.date.slice(0, 4),
    place: src.place == null ? null : { value: src.place, medal: src.medal },
    styleName: src.styleName,
    distanceLabel: coverDistance(src.distance),
    timeText: src.failLabel ?? src.time,
    badge: coverBadge(src.marks),
    swimmerName: src.swimmerName,
    clubName: src.clubName || null,
  };
}

/** Имя файла без расширения: дата, дисциплина и раскладка — чтобы файлы не слиплись. */
export function coverFileName(date: string, distance: string, styleShort: string, layout: CoverLayout): string {
  return `swimhub-cover-${date}-${distance}${styleShort}-${layout}`.replace(/[^\w-]+/g, '-');
}

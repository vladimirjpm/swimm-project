// Данные обложки YouTube: ЧТО на ней напечатано. Чистые функции без React и без обращений к
// приложению (импорты — только типы), поэтому проверяются юнит-тестом `cover-data.test.mjs`.
// Откуда берутся входы (медаль, метка, значок, плитка) — `cover-source.ts`; как это
// нарисовано — `layouts/` и `parts/`. Общее с плакатом (значок или кубок, одна метка,
// дистанция) — `print/print-data.ts`.

import type { CompetitionTileData } from '../../../utils/helpers/competition-source';
import type { PrintEmblem, PrintMark, PrintPlace, PrintRecordKind } from '../print/print-data';

export type CoverLayout = 'A' | 'B' | 'C';

/** Всё, что обложка печатает. */
export interface CoverData {
  competitionName: string;
  tile: CompetitionTileData;
  emblem: PrintEmblem;
  /** День заплыва: `5 NOV`. */
  dayLabel: string;
  /** Дни соревнования: `5–6 NOV`; у однодневного совпадает с `dayLabel`. */
  rangeLabel: string;
  year: string;
  /** null — места нет (DNS / DQ). */
  place: PrintPlace | null;
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

/** Входы обложки: поля заплыва и то, что о нём уже решили общие правила продукта. */
export interface CoverSource {
  competitionName: string;
  tile: CompetitionTileData;
  /** Значок серии, кубок или пусто — `printEmblem`. */
  emblem: PrintEmblem;
  /** День заплыва, yyyy-MM-dd. */
  date: string;
  /** Первый и последний день соревнования, yyyy-MM-dd. */
  firstDay: string;
  lastDay: string;
  place: number | null;
  /** Медаль вручали (`isMedal`) — место ≠ медаль. */
  medal: boolean;
  styleName: string;
  /** Дистанция, готовая к печати, — `printDistance`. */
  distanceLabel: string;
  time: string;
  /** Пометка протокола вместо времени (DNS / DQ); null — время есть. */
  failLabel: string | null;
  /** Одна метка заплыва — `topMark` (у `Under review` её нет, Р76). */
  mark: PrintMark | null;
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
const RECORD_LABEL: Record<PrintRecordKind, string> = {
  world: 'WORLD RECORD',
  national: 'NATIONAL RECORD',
  age: 'AGE RECORD',
  masters: 'MASTERS RECORD',
};

/** Подпись метки обложки. */
export function coverBadge(mark: PrintMark | null): string | null {
  if (!mark) return null;
  if (mark.kind === 'record') return RECORD_LABEL[mark.record];
  return mark.kind === 'sb' ? 'SEASON BEST' : 'PERSONAL BEST';
}

export function buildCoverData(src: CoverSource): CoverData {
  return {
    competitionName: src.competitionName,
    tile: src.tile,
    emblem: src.emblem,
    dayLabel: coverDay(src.date),
    rangeLabel: coverRange(src.firstDay, src.lastDay),
    year: src.date.slice(0, 4),
    place: src.place == null ? null : { value: src.place, medal: src.medal },
    styleName: src.styleName,
    distanceLabel: src.distanceLabel,
    timeText: src.failLabel ?? src.time,
    badge: coverBadge(src.mark),
    swimmerName: src.swimmerName,
    clubName: src.clubName || null,
  };
}

/** Имя файла без расширения: дата, дисциплина и раскладка — чтобы файлы не слиплись. */
export function coverFileName(date: string, distance: string, styleShort: string, layout: CoverLayout): string {
  return `swimhub-cover-${date}-${distance}${styleShort}-${layout}`.replace(/[^\w-]+/g, '-');
}

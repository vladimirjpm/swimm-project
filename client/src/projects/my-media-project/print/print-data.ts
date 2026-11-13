// Общие данные «печатной» картинки My media (обложка YouTube и плакат): типы и чистые функции
// без React и без обращений к приложению — проверяются юнит-тестом `print-data.test.mjs`.

/** Что стоит на месте значка соревнования. */
export type PrintEmblem =
  | { kind: 'image'; url: string }
  | { kind: 'cup' }
  | { kind: 'empty' };

/** Место: число как в протоколе и отдельно — вручали ли за него медаль (место ≠ медаль). */
export interface PrintPlace {
  value: number;
  medal: boolean;
}

/** Вид рекорда — те же ключи, что у `UI_RecordBadge` (`RecordKind`). */
export type PrintRecordKind = 'world' | 'national' | 'age' | 'masters';

/** Метки достижений заплыва, уже посчитанные общим `swimMarks` (у `Under review` все пустые, Р76). */
export interface PrintMarks {
  record: { kind: PrintRecordKind } | null;
  sb: boolean;
  pb: boolean;
}

/** Какая ОДНА метка у заплыва; приоритет общий для продукта: рекорд > SB > PB. */
export type PrintMark =
  | { kind: 'record'; record: PrintRecordKind }
  | { kind: 'sb' }
  | { kind: 'pb' };

export function topMark(marks: PrintMarks): PrintMark | null {
  if (marks.record) return { kind: 'record', record: marks.record.kind };
  if (marks.sb) return { kind: 'sb' };
  return marks.pb ? { kind: 'pb' } : null;
}

/**
 * Что на месте значка (решение Влада 12.11.2026): есть значок серии — значок; значка нет, но
 * соревнование — чемпионат — кубок; иначе пусто (держать ли место — дело вёрстки).
 */
export function printEmblem(emblemUrl: string | null, isChampionship: boolean): PrintEmblem {
  if (emblemUrl) return { kind: 'image', url: emblemUrl };
  return isChampionship ? { kind: 'cup' } : { kind: 'empty' };
}

/** `100` → `100m`; дистанцию эстафеты (`4X50`) печатаем как в протоколе. */
export function printDistance(distance: string): string {
  return /^\d+$/.test(distance) ? `${distance}m` : distance;
}

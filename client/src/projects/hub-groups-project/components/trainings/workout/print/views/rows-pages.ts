import type { SheetCube, SheetModel } from '../print-types';

/**
 * Листы «листа строк» — видов `rows` и `two-sides` (хендофф дизайнера 27.11.2026,
 * docs/plans/workout-print-plan.md §4.3). Чистые функции без DOM; тесты — `rows-pages.test.mjs`.
 */

export interface TwoSidesSplit {
  /** Лицо: сеты до первого главного. */
  front: SheetCube[];
  /** Оборот: первый главный и всё после него. */
  back: SheetCube[];
  /** Номер первого главного — в полосе «Next ④ Main set … page 2». */
  mainNo: number;
}

/**
 * Деление по ПЕРВОМУ главному — только галочка `main`, без эвристик (решение Влада 27.11).
 * Главного нет или он первый — делить нечего: null, вид `two-sides` недоступен.
 */
export function splitTwoSides(cubes: SheetCube[]): TwoSidesSplit | null {
  const i = cubes.findIndex((c) => c.main);
  if (i <= 0) return null;
  return { front: cubes.slice(0, i), back: cubes.slice(i), mainNo: cubes[i].no };
}

/** Один лист строк = одна напечатанная страница. */
export interface RowsPage {
  /** Ключ листа и его копии замера: 'rows', 'front', 'back'. */
  key: string;
  cubes: SheetCube[];
  /** Метка страницы в шапке: «1 / 2»; null — лист один. */
  pageMark: string | null;
  /** Полоса внизу «Next ④ Main set … page 2» — номер главного; null — полосы нет. */
  nextMainNo: number | null;
  /** Заметка тренера — только на первом листе. */
  note: string | null;
}

export type RowsViewKind = 'rows' | 'two-sides';

/** Листы вида: `rows` — один со всеми сетами; `two-sides` — лицо и оборот (не делится — null). */
export function rowsPages(model: Pick<SheetModel, 'cubes' | 'note'>, kind: RowsViewKind): RowsPage[] | null {
  if (kind === 'rows') return [{ key: 'rows', cubes: model.cubes, pageMark: null, nextMainNo: null, note: model.note }];
  const split = splitTwoSides(model.cubes);
  if (!split) return null;
  return [
    { key: 'front', cubes: split.front, pageMark: '1 / 2', nextMainNo: split.mainNo, note: model.note },
    { key: 'back', cubes: split.back, pageMark: '2 / 2', nextMainNo: null, note: null },
  ];
}

/**
 * Линия-разделитель над сетом: у сета, который не первый на листе, не главный и не идёт сразу
 * после главного (рядом с рамкой главного линия не нужна).
 */
export function setRuled(cubes: SheetCube[], i: number): boolean {
  return i > 0 && !cubes[i].main && !cubes[i - 1].main;
}

/** Строка заголовка сета — только если есть своё название, повтор или `main` («Set N» не печатается). */
export function setHasTitleRow(cube: SheetCube): boolean {
  return !!cube.ownTitle || cube.repeat > 1 || cube.main;
}

/** Ключ строки на листе (для правила «текст под дистанцией»): номер сета · индекс строки. */
export const rowsLineKey = (cube: SheetCube, index: number) => `${cube.no}.${index}`;

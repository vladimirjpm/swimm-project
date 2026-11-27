import type { SheetDir } from './print-types';

/**
 * Геометрия и подгонка шрифта листа печати (docs/plans/workout-print-plan.md, §3.2–3.5). Чистые
 * функции без DOM: замер (влезло ли при размере `f`) приходит снаружи — в приложении его делает
 * `use-sheet-fit.ts` по скрытой копии листа, в тестах — поддельный. Тесты — `print-fit.test.mjs`.
 */

// ── Размеры (CSS-пиксели, 96 px/дюйм) ───────────────────────────────────────────

export const A4_WIDTH = 794; // 210 мм
export const A4_HEIGHT = 1123; // 297 мм
export const SHEET_PADDING = 38; // 10 мм внутри листа: принтеры не печатают у края
export const CONTENT_WIDTH = A4_WIDTH - 2 * SHEET_PADDING; // 718
export const CONTENT_HEIGHT = A4_HEIGHT - 2 * SHEET_PADDING; // 1047
export const COLUMN_GAP = 24;
export const COLUMN_WIDTH = (CONTENT_WIDTH - COLUMN_GAP) / 2; // 347

/** Размеры основного шрифта, px: потолок 72 pt, порог читаемости 14 pt (В16), пол 8 pt. */
export const F_MAX = 96;
export const F_READABLE = 18.7;
export const F_FLOOR = 10.7;
export const F_STEP = 0.5;

/** Промежуток под стрелку между кубами, em от основного шрифта (§3.4). */
export const ARROW_GAP_EM = 1.6;

export const pxToPt = (px: number) => px * 0.75;

/** Ширина куба при `k` кубах в ряду и основном шрифте `f`: (718 − (k − 1)·g) / k. */
export function cubeWidth(k: number, f: number): number {
  return (CONTENT_WIDTH - (k - 1) * ARROW_GAP_EM * f) / k;
}

// ── Поиск размера шрифта ────────────────────────────────────────────────────────

export interface FitResult {
  /** Подобранный основной шрифт, px. */
  size: number;
  /** Не влезло даже на полу: лист печатается с полом, низ обрезан (§3.5). */
  overflow: boolean;
}

export interface FitOptions {
  floor?: number;
  max?: number;
  step?: number;
}

const round1 = (n: number) => Math.round(n * 10) / 10;

/**
 * Наибольший размер из [floor, max] с шагом `step` (сам `max` — верхний кандидат всегда), при
 * котором `fits(f)` истинно. Бинарный поиск, ~10 замеров. Ступенчатый рост высоты из-за переносов
 * строк ему не мешает; страховка — найденный размер перепроверяется и при неудаче уменьшается.
 * Не влезло и на полу — `{ size: floor, overflow: true }`.
 */
export function searchFontSize(fits: (f: number) => boolean, opts: FitOptions = {}): FitResult {
  const floor = opts.floor ?? F_FLOOR;
  const max = opts.max ?? F_MAX;
  const step = opts.step ?? F_STEP;

  const sizes: number[] = [];
  for (let s = floor; s < max - 1e-9; s += step) sizes.push(round1(s));
  sizes.push(max);

  if (!fits(sizes[0])) return { size: sizes[0], overflow: true };
  let lo = 0; // известно: влезает
  let hi = sizes.length - 1;
  if (fits(sizes[hi])) return { size: sizes[hi], overflow: false };
  // fits(sizes[hi]) — нет: ищем границу между lo (влезает) и hi (нет).
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (fits(sizes[mid])) lo = mid;
    else hi = mid;
  }
  // Страховка от немонотонности: выбранный размер обязан влезать.
  while (lo > 0 && !fits(sizes[lo])) lo -= 1;
  return { size: sizes[lo], overflow: false };
}

// ── Выбор раскладки ─────────────────────────────────────────────────────────────

export type CubeColumns = 1 | 2 | 3;
export const CUBE_COLUMN_OPTIONS: readonly CubeColumns[] = [1, 2, 3];

export interface CubesFit extends FitResult {
  columns: CubeColumns;
}

/**
 * Из подгонки при k = 1, 2, 3 — самый крупный шрифт; при равенстве — меньше k (шире кубы).
 * Не влезло ни при каком k (все на полу) — больше k: при k = 3 обрезано меньше всего (§3.5).
 */
export function pickCubeColumns(fits: CubesFit[]): CubesFit {
  if (fits.every((f) => f.overflow)) return fits.reduce((best, f) => (f.columns > best.columns ? f : best));
  const fitting = fits.filter((f) => !f.overflow);
  return fitting.reduce((best, f) => (f.size > best.size || (f.size === best.size && f.columns < best.columns) ? f : best));
}

export type LayoutChoice =
  | (FitResult & { layout: 'main' })
  | (CubesFit & { layout: 'cubes' });

/**
 * «Главный справа» или «кубы» (§3.5, В16). `main` — подгонка раскладки «главный справа»; null — её
 * нет (главного нет, все главные или сет один) → кубы. Иначе «главный справа», если её шрифт
 * не меньше `F_READABLE` (14 pt); а если меньше — кубы, когда они дают крупнее (или первая не
 * влезла вовсе, а кубы влезли).
 */
export function chooseLayout(main: FitResult | null, cubes: CubesFit): LayoutChoice {
  const asCubes: LayoutChoice = { layout: 'cubes', ...cubes };
  if (!main) return asCubes;
  if (main.size >= F_READABLE && !main.overflow) return { layout: 'main', ...main };
  if (main.overflow && !cubes.overflow) return asCubes;
  return cubes.size > main.size ? asCubes : { layout: 'main', ...main };
}

/** Подпись выбранного в окне предпросмотра: «Main on the right · 28 pt» / «Blocks · 3 per row · 22 pt». */
export function layoutCaption(choice: LayoutChoice): string {
  const pt = `${Math.round(pxToPt(choice.size))} pt`;
  return choice.layout === 'main' ? `Main on the right · ${pt}` : `Blocks · ${choice.columns} per row · ${pt}`;
}

/**
 * Подпись вида, у которого шрифт подбирается по каждому листу отдельно (`rows`, `two-sides`):
 * «Rows · 29 pt», «Two sides · 40 pt / 45 pt» — размеры всех листов по порядку.
 */
export function sheetsCaption(label: string, sizes: number[]): string {
  return `${label} · ${sizes.map((s) => `${Math.round(pxToPt(s))} pt`).join(' / ')}`;
}

// ── Сетка кубов змейкой ─────────────────────────────────────────────────────────

export type ArrowKind = 'left' | 'right' | 'down';

/**
 * Стрелка между кубами `from` → `to` (индексы 0…n−1 в порядке выполнения). Место: горизонтальная —
 * в промежутке справа от физической колонки `col` (между `col` и `col + 1`) ряда `row`; вниз —
 * под кубом колонки `col` ряда `row`.
 */
export interface CubeArrow {
  from: number;
  to: number;
  kind: ArrowKind;
  row: number;
  col: number;
}

export interface CubeGrid {
  columns: CubeColumns;
  /** Ряды сверху вниз; в ряду — физические колонки слева направо: индекс куба или null (пусто). */
  rows: Array<Array<number | null>>;
  arrows: CubeArrow[];
}

/**
 * Кубы по сетке листа «змейкой» (§3.4, В15). Ивритский лист (`rtl`): ряд 1 — справа налево, ряд 2 —
 * слева направо и т. д.; английский (`ltr`) — зеркально. Стрелки: внутри ряда — горизонтальная
 * (◀ / ▶), на смене ряда — вниз под последним кубом ряда. Неполный ряд — только последний и
 * заполняется от стороны начала.
 */
export function cubeGrid(n: number, k: CubeColumns, dir: SheetDir): CubeGrid {
  const rowCount = Math.ceil(n / k);
  const rows: Array<Array<number | null>> = [];
  const colOf: Array<{ row: number; col: number }> = [];
  for (let r = 0; r < rowCount; r += 1) {
    const cells: Array<number | null> = Array(k).fill(null);
    // Ряд идёт справа налево: у ивритского листа чётные ряды (0, 2…), у английского — нечётные.
    const leftward = (dir === 'rtl') === (r % 2 === 0);
    for (let i = 0; i < k && r * k + i < n; i += 1) {
      const col = leftward ? k - 1 - i : i;
      cells[col] = r * k + i;
      colOf[r * k + i] = { row: r, col };
    }
    rows.push(cells);
  }
  const arrows: CubeArrow[] = [];
  for (let i = 0; i + 1 < n; i += 1) {
    const a = colOf[i];
    const b = colOf[i + 1];
    if (a.row !== b.row) arrows.push({ from: i, to: i + 1, kind: 'down', row: a.row, col: a.col });
    else arrows.push({ from: i, to: i + 1, kind: b.col < a.col ? 'left' : 'right', row: a.row, col: Math.min(a.col, b.col) });
  }
  return { columns: k, rows, arrows };
}

// Подгонка строк плаката под их число: в макетах 5–6 результатов, а у пловца их бывает и 2, и
// 10. Ячейки результата раскладываются сеткой и равномерно масштабируются, пока помещаются в
// отведённую область. Чистая функция, тест — `poster-fit.test.mjs`.

export interface GridArea {
  /** Число ячеек. */
  count: number;
  /** Область под сетку, px. */
  width: number;
  height: number;
  /** Ячейка в масштабе 1 (размеры макета). */
  cell: { width: number; height: number };
  /** Зазоры между ячейками в масштабе 1; масштабируются вместе с ячейкой. */
  gap: { x: number; y: number };
  /** Допустимое число колонок, по возрастанию. */
  columns: number[];
  /** Потолок увеличения: два результата не должны раздуться на весь плакат. */
  maxScale: number;
}

export interface GridFit {
  columns: number;
  rows: number;
  scale: number;
}

/** Варианты с масштабом не меньше этой доли лучшего считаются равными по крупности. */
const CLOSE_SHARE = 0.9;

/** Пустые места в последнем ряду сетки. */
const emptyCells = (fit: GridFit, count: number): number => fit.columns * fit.rows - count;

function fitFor(area: GridArea, columns: number): GridFit {
  const rows = Math.ceil(area.count / columns);
  const byWidth = area.width / (columns * area.cell.width + (columns - 1) * area.gap.x);
  const byHeight = area.height / (rows * area.cell.height + (rows - 1) * area.gap.y);
  return { columns, rows, scale: Math.min(byWidth, byHeight, area.maxScale) };
}

/**
 * Сетка с самым крупным масштабом ячеек. Колонок больше, чем ячеек, не бывает. Из близких по
 * масштабу вариантов берётся самый ровный — где меньше пустых мест в последнем ряду, а при
 * равенстве — где меньше колонок (строки длиннее и заполняют высоту).
 */
export function fitGrid(area: GridArea): GridFit {
  const count = Math.max(1, area.count);
  const fits = area.columns
    .filter((c, i) => c <= count || i === 0)
    .map((c) => fitFor({ ...area, count }, Math.min(c, count)));
  const best = Math.max(...fits.map((f) => f.scale));
  return fits
    .filter((f) => f.scale >= best * CLOSE_SHARE)
    .sort((a, b) => emptyCells(a, count) - emptyCells(b, count) || a.columns - b.columns)[0];
}

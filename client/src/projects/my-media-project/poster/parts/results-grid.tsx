import React from 'react';
import type { GridFit } from '../poster-fit';

/**
 * Сетка результатов по расчёту `fitGrid`: колонки шириной ячейки в текущем масштабе, зазоры в
 * том же масштабе, сама сетка — по центру отведённой области. `stretch` — колонки делят всю
 * ширину поровну (лента раскладки C: ячейки уже ужаты по высоте, а ширина свободна).
 */
export default function ResultsGrid({ fit, cellWidth, gap, stretch = false, style, children }: {
  fit: GridFit;
  /** Ширина ячейки в масштабе 1. */
  cellWidth: number;
  /** Зазоры в масштабе 1. */
  gap: { x: number; y: number };
  stretch?: boolean;
  style?: React.CSSProperties;
  children: React.ReactNode;
}) {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: `repeat(${fit.columns}, ${stretch ? 'minmax(0, 1fr)' : `${cellWidth * fit.scale}px`})`,
        columnGap: gap.x * fit.scale, rowGap: gap.y * fit.scale,
        justifyContent: 'center', alignContent: 'center', ...style,
      }}
    >
      {children}
    </div>
  );
}

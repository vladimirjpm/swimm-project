import type React from 'react';
import type { PrintSize } from '../print/print-export';
import { PRINT_CLASS, PRINT_COLOR, printRoot } from '../print/print-theme';
import type { PosterBadge } from './poster-data';

// Размер, цвета и корень плаката — общие для трёх раскладок (макеты `PosterA/B/C.dc.html`).
// Общее с обложкой (и объяснение, почему цвета — хексы, а не роли `--t-*`) —
// `print/print-theme.ts`.

/** Пропорция A4. */
export const POSTER_W = 1414;
export const POSTER_H = 2000;

/** Поле слева и справа у раскладок A и C; ширина полотна между полями. */
export const POSTER_MARGIN = 86;
export const POSTER_INNER_W = POSTER_W - 2 * POSTER_MARGIN;

/** Файл плаката; `print` — вдвое больше по каждой стороне, 2828×4000 (A4 при ~340 dpi). */
export const posterSize = (print: boolean): PrintSize => ({ width: POSTER_W, height: POSTER_H, pixelRatio: print ? 2 : 1 });

/** Какая часть снимка бассейна остаётся в кадре. */
export const POSTER_BG_FOCUS = 'center top';

export const POSTER_COLOR = {
  ...PRINT_COLOR,
  /** Затемнение фона раскладки B. */
  dim: 'rgba(11, 27, 63, 0.5)',
  /** Разделитель строк таблицы B. */
  rule: '#1d4f86',
  /** Линии по сторонам значка в раскладке B. */
  line: 'rgba(255, 255, 255, 0.45)',
} as const;

/** Цвета метки строки: рекорд, SB, PB (макет). */
export const POSTER_BADGE_COLOR: Record<PosterBadge['kind'], { background: string; color: string }> = {
  record: { background: '#cbd5e1', color: '#12305a' },
  sb: { background: '#fbbf24', color: '#3b2600' },
  pb: { background: '#ffffff', color: '#1d4f86' },
};

/** Корень любой раскладки плаката. */
export const POSTER_ROOT: React.CSSProperties = printRoot(POSTER_W, POSTER_H);

/** Класс корня — под него заперт общий `print.css`. */
export const POSTER_CLASS = PRINT_CLASS;

/** Размер макета в текущем масштабе строки, целым числом пикселей. */
export const scaled = (value: number, scale: number): number => Math.round(value * scale);

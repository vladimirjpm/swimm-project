import type React from 'react';
import { PRINT_CLASS, PRINT_COLOR, printRoot } from '../print/print-theme';

// Размер, цвета и корень обложки — общие для трёх раскладок. Цвета и корень, общие с плакатом
// (и объяснение, почему это хексы, а не роли `--t-*`), — в `print/print-theme.ts`.

export const COVER_W = 1280;
export const COVER_H = 720;

export const COVER_COLOR = {
  ...PRINT_COLOR,
  /** Скруглённый угол со значком (A и B). */
  corner: '#2b5d8c',
  dateStrip: '#ff7a1a',
  /** Год в колонке раскладки C. */
  yearGold: PRINT_COLOR.gold,
  /** Колонка раскладки C и затемнение фона под ней. */
  column: PRINT_COLOR.panelStrong,
  dim: 'rgba(11, 27, 63, 0.4)',
} as const;

/** Какая часть снимка бассейна остаётся в кадре 16:9. */
export const COVER_BG_FOCUS = 'center 62%';

/** Корень любой раскладки обложки. */
export const COVER_ROOT: React.CSSProperties = { ...printRoot(COVER_W, COVER_H), display: 'flex' };

/** Класс корня — под него заперт общий `print.css`. */
export const COVER_CLASS = PRINT_CLASS;

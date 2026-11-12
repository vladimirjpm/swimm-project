import type React from 'react';
import { COVER_FONT } from './cover-fonts';

// Размер, цвета и корень обложки — общие для трёх раскладок.
//
// Цвета — хексы макета, а не роли `--t-*`, и это осознанно: обложка — картинка, а не экран
// сайта. Она обязана выйти одинаковой в светлой и тёмной теме, у любого пользователя. По той
// же причине готовым компонентам (`CompetitionTile`, `UI_MedalIcon`, `UI_ClubLogo`) их токены
// темы заданы на корне обложки постоянными значениями (`ROOT_TOKENS`).

export const COVER_W = 1280;
export const COVER_H = 720;

export const COVER_COLOR = {
  navy: '#0b1b3f',
  ink: '#12305a',
  /** Тёмная подложка панели результата. */
  panel: 'rgba(11, 27, 63, 0.72)',
  /** Светлая плита под иконкой стиля. */
  plate: '#e6f2fb',
  badge: '#cbd5e1',
  /** Скруглённый угол со значком (A и B). */
  corner: '#2b5d8c',
  dateStrip: '#ff7a1a',
  /** Год в колонке раскладки C. */
  yearGold: '#f5a623',
  /** Колонка раскладки C и затемнение фона под ней. */
  column: 'rgba(11, 27, 63, 0.82)',
  dim: 'rgba(11, 27, 63, 0.4)',
  photoFrame: '#9cc3e8',
} as const;

/** Токены готовых компонентов — постоянные: обложка не зависит от темы страницы. */
const ROOT_TOKENS = {
  '--theme-comp-tile-bg': '#d3e3ff',
  '--theme-comp-tile-border': '#8fb4ee',
  '--theme-comp-tile-letter': '#0048b8',
  '--theme-comp-tile-strip-bg': '#ffffff',
  '--theme-comp-tile-age-bg': '#ffffff',
  '--theme-comp-tile-age-text': '#00265e',
  // `UI_MedalIcon` без медали печатает номер цветом текста поверхности — здесь она тёмная.
  '--theme-mode-text': '#ffffff',
  // `UI_ClubLogo`: белый круг под логотипом, инициалы тёмным.
  '--theme-mode-border': 'rgba(255, 255, 255, 0.55)',
  '--theme-mode-surface-alt': '#ffffff',
  '--theme-mode-text-secondary': COVER_COLOR.ink,
} as React.CSSProperties;

/** Корень любой раскладки: размер, фон, шрифт и сброс того, что могло прийти со страницы. */
export const COVER_ROOT: React.CSSProperties = {
  ...ROOT_TOKENS,
  position: 'relative', zIndex: 0, overflow: 'hidden', width: COVER_W, height: COVER_H, boxSizing: 'border-box',
  display: 'flex', background: COVER_COLOR.navy, fontFamily: COVER_FONT.text, color: '#ffffff', direction: 'ltr',
  textAlign: 'left', lineHeight: 'normal', letterSpacing: 'normal',
};

/** Класс корня — под него заперт `cover.css`. */
export const COVER_CLASS = 'ytc';

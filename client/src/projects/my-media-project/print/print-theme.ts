import type React from 'react';
import { PRINT_FONT } from './print-fonts';

// Цвета и корень «печатной» картинки My media — общие для обложки YouTube (`cover/`) и плаката
// (`poster/`).
//
// Цвета — хексы макета, а не роли `--t-*`, и это осознанно: картинка — не экран сайта. Она
// обязана выйти одинаковой в светлой и тёмной теме, у любого пользователя. По той же причине
// готовым компонентам (`CompetitionTile`, `UI_MedalIcon`, `UI_ClubLogo`) их токены темы заданы
// на корне картинки постоянными значениями (`ROOT_TOKENS`).

export const PRINT_COLOR = {
  navy: '#0b1b3f',
  ink: '#12305a',
  /** Тёмная подложка панели результата. */
  panel: 'rgba(11, 27, 63, 0.72)',
  /** Плотная подложка: колонка обложки C, таблица плаката B. */
  panelStrong: 'rgba(11, 27, 63, 0.82)',
  /** Светлая плита под иконкой стиля. */
  plate: '#e6f2fb',
  /** Дистанция под иконкой стиля. */
  plateLen: '#b3261e',
  badge: '#cbd5e1',
  /** Год, строка сезона. */
  gold: '#f5a623',
  /** Второстепенный светлый текст (возрастная группа, подписи счёта). */
  sky: '#cfe8ff',
  muted: '#a8c6e8',
  photoFrame: '#9cc3e8',
} as const;

/**
 * Во сколько раз значок сезона (❄️ / ☀️) на обложке и плакате крупнее обычного (правка Влада
 * 13.11.2026: «на всех коверах и постерах увелич снежинку на 50%»). Одно число на оба случая:
 * значок на месте несодержательной плитки (`--comp-tile-mark-scale`) и отдельный значок сезона
 * плаката C. На сайте значок не меняется; сезон в полосе содержательной плитки — тоже.
 */
export const PRINT_SEASON_SCALE = 1.5;

/** Токены готовых компонентов — постоянные: картинка не зависит от темы страницы. */
const ROOT_TOKENS = {
  '--theme-comp-tile-bg': '#d3e3ff',
  '--theme-comp-tile-border': '#8fb4ee',
  '--theme-comp-tile-letter': '#0048b8',
  '--theme-comp-tile-strip-bg': '#ffffff',
  '--theme-comp-tile-age-bg': '#ffffff',
  '--theme-comp-tile-age-text': '#00265e',
  '--comp-tile-mark-scale': PRINT_SEASON_SCALE,
  // `UI_MedalIcon` без медали печатает номер цветом текста поверхности — здесь она тёмная.
  '--theme-mode-text': '#ffffff',
  // `UI_ClubLogo`: белый круг под логотипом, инициалы тёмным.
  '--theme-mode-border': 'rgba(255, 255, 255, 0.55)',
  '--theme-mode-surface-alt': '#ffffff',
  '--theme-mode-text-secondary': PRINT_COLOR.ink,
} as React.CSSProperties;

/** Класс корня — под него заперт `print.css`. */
export const PRINT_CLASS = 'prn';

/** Корень любой раскладки: размер, фон, шрифт и сброс того, что могло прийти со страницы. */
export function printRoot(width: number, height: number): React.CSSProperties {
  return {
    ...ROOT_TOKENS,
    position: 'relative', zIndex: 0, overflow: 'hidden', width, height, boxSizing: 'border-box',
    background: PRINT_COLOR.navy, fontFamily: PRINT_FONT.text, color: '#ffffff', direction: 'ltr',
    textAlign: 'left', lineHeight: 'normal', letterSpacing: 'normal',
  };
}

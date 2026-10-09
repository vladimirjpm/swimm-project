import type React from 'react';
import type { Cat } from './normative-model';

// ==================== Дизайн-токены (design_handoff_normative_info) ====================

export type CatColors = { accent: string; deep: string; soft: string; border: string };

// Палитра категорий уровней (pro / adult / youth) + нейтраль для рекордов
export const CAT_LIGHT: Record<Cat, CatColors> = {
  pro:   { accent: '#ea580c', deep: '#7c2d12', soft: '#fff0e6', border: '#fcd5bb' },
  adult: { accent: '#4f46e5', deep: '#312e81', soft: '#eeecfe', border: '#d6d1fb' },
  youth: { accent: '#16a34a', deep: '#14532d', soft: '#e7f6ed', border: '#c2ebd1' },
  rec:   { accent: '#475569', deep: '#0f172a', soft: '#eef1f6', border: '#d7dde6' },
};
export const CAT_DARK: Record<Cat, CatColors> = {
  pro:   { accent: '#ff8a4c', deep: '#ffd9c2', soft: 'rgba(255,138,76,.16)', border: '#5a3320' },
  adult: { accent: '#8b83ff', deep: '#dcd8ff', soft: 'rgba(139,131,255,.17)', border: '#332f5c' },
  youth: { accent: '#3ecb7f', deep: '#c7f0d8', soft: 'rgba(62,203,127,.15)', border: '#245038' },
  rec:   { accent: '#94a3b8', deep: '#e2e8f0', soft: 'rgba(148,163,184,.14)', border: '#2b3442' },
};

// Поверхности / текст / пилюли M-W
export const UI_LIGHT = {
  card: '#ffffff', cardBorder: '#e6e9ee', cardShadow: '0 20px 60px rgba(20,28,45,.14)',
  subtle: '#f1f3f7', grid: '#eceff4', text: '#141824', label: '#8a93a2',
  menPill: '#e6f0fd', menText: '#1d4ed8', womenPill: '#fdeaf2', womenText: '#be185d',
  rowWash: '#fffbeb', rowWashSoft: '#fffdf5',
};
export const UI_DARK: typeof UI_LIGHT = {
  card: '#151a22', cardBorder: '#232b36', cardShadow: '0 20px 60px rgba(0,0,0,.5)',
  subtle: '#1c232e', grid: '#232b36', text: '#eef2f7', label: '#8794a4',
  menPill: 'rgba(90,162,245,.16)', menText: '#9cc4fb', womenPill: 'rgba(240,114,166,.16)', womenText: '#f6a9cd',
  rowWash: 'rgba(245,184,0,.10)', rowWashSoft: 'rgba(245,184,0,.05)',
};

// Стили в дропдауне «Swimming Style»: порядок и короткие подписи (для title и sr-only)
export const STROKES: [string, string][] = [
  ['freestyle', 'Freestyle'],
  ['backstroke', 'Backstroke'],
  ['breaststroke', 'Breaststroke'],
  ['butterfly', 'Butterfly'],
  ['individual_medley', 'Medley'],
];

// Вариант медальонов: 'A' (Aurora, мягкие чипы) / 'B' (Podium, залитые accent).
// Единственное отличие вариантов — заливка медальона (см. README handoff).
const VARIANT = 'A' as 'A' | 'B';

export const strokeIconUrl = (stroke: string) =>
  `${import.meta.env.BASE_URL}images/swimm-style-icon/${stroke}.png`;

// ==== Медальон уровня (вариант A: мягкий чип; B: заливка accent) ====
export const medStyle = (cc: CatColors): React.CSSProperties => ({
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
  minWidth: 46, height: 36, padding: '0 12px', borderRadius: 11,
  fontWeight: 900, fontSize: 14, letterSpacing: '.01em',
  ...(VARIANT === 'B'
    ? { background: cc.accent, color: '#fff', border: `1.5px solid ${cc.accent}` }
    : { background: cc.soft, color: cc.deep, border: `1.5px solid ${cc.border}` }),
});

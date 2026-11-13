import React from 'react';
import FitText, { type FitSize } from '../../print/parts/fit-text';
import { PRINT_FONT } from '../../print/print-fonts';
import type { PosterData } from '../poster-data';
import { POSTER_COLOR } from '../poster-theme';

const HEBREW: React.CSSProperties = { fontFamily: PRINT_FONT.hebrew, lineHeight: 1.1 };

/** Имя пловца — самая крупная строка плаката; длинное имя ужимается. */
export function SwimmerName({ name, size, style }: { name: string; size: FitSize; style?: React.CSSProperties }) {
  return <FitText text={name} dir="rtl" size={size} style={{ ...HEBREW, fontWeight: 900, lineHeight: 1, textAlign: 'center', ...style }} />;
}

/** Название соревнования; не поместилось и на минимальном кегле — вторая строка. */
export function CompetitionName({ name, size }: { name: string; size: FitSize }) {
  return <FitText text={name} dir="rtl" size={size} wrap style={{ ...HEBREW, fontWeight: 900, textAlign: 'center' }} />;
}

/** Строка под названием: «חורף 2026» золотом и «גילאי 8-11» светлым (если группа есть). */
export function SeasonLine({ data, size }: { data: PosterData; size: number }) {
  return (
    <div dir="rtl" style={{ ...HEBREW, display: 'flex', alignItems: 'baseline', gap: 24, fontSize: size, whiteSpace: 'nowrap' }}>
      <span style={{ fontWeight: 900, color: POSTER_COLOR.gold }}>{data.seasonLabel}</span>
      {data.ageLabel && <span style={{ fontWeight: 700, color: POSTER_COLOR.sky }}>{data.ageLabel}</span>}
    </div>
  );
}

/** Год — Montserrat 900 italic золотом. */
export function Year({ year, size, style }: { year: string; size: number; style?: React.CSSProperties }) {
  return (
    <span style={{ fontFamily: PRINT_FONT.digits, fontStyle: 'italic', fontSize: size, fontWeight: 900, lineHeight: 1, color: POSTER_COLOR.gold, ...style }}>
      {year}
    </span>
  );
}

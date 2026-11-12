import React from 'react';
import { PRINT_FONT } from '../print-fonts';
import { PRINT_COLOR } from '../print-theme';
import FitText, { type FitSize } from './fit-text';

export interface TimeSpec extends FitSize {
  /** Толщина тёмной обводки, px. */
  stroke: number;
}

/** Время — Montserrat 900 italic с тёмной обводкой (`.prn-time`); кегль подгоняется по ширине. */
export function TimeText({ text, spec }: { text: string; spec: TimeSpec }) {
  return (
    <FitText
      text={text}
      size={spec}
      className="prn-time"
      // Поля — под наклон курсива и обводку: `overflow: hidden` подгонки иначе срезал бы края.
      style={{
        fontFamily: PRINT_FONT.digits, fontStyle: 'italic', fontWeight: 900, lineHeight: 1,
        padding: '0 10px 2px 4px', margin: '0 -10px -2px -4px', boxSizing: 'content-box',
        WebkitTextStroke: `${spec.stroke}px ${PRINT_COLOR.navy}`,
      }}
    />
  );
}

export interface BadgeSpec extends FitSize {
  padding: string;
  radius: number;
}

/** Метка достижения (`AGE RECORD`, `SEASON BEST`, `PERSONAL BEST`); нет метки — нет и плашки. */
export function Badge({ text, spec }: { text: string | null; spec: BadgeSpec }) {
  if (!text) return null;
  return (
    <FitText
      text={text}
      size={spec}
      style={{
        boxSizing: 'border-box', padding: spec.padding, borderRadius: spec.radius, background: PRINT_COLOR.badge,
        fontWeight: 800, letterSpacing: '0.04em', lineHeight: 1, color: PRINT_COLOR.ink,
      }}
    />
  );
}

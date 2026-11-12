import React from 'react';
import { COVER_FONT } from '../cover-fonts';
import { COVER_COLOR } from '../cover-theme';
import FitText, { type FitSize } from './fit-text';

export interface TimeSpec extends FitSize {
  /** Толщина тёмной обводки, px. */
  stroke: number;
}

/** Время — Montserrat 900 italic с тёмной обводкой (`.ytc-time`); кегль подгоняется по ширине. */
export function TimeText({ text, spec }: { text: string; spec: TimeSpec }) {
  return (
    <FitText
      text={text}
      size={spec}
      className="ytc-time"
      // Поля — под наклон курсива и обводку: `overflow: hidden` подгонки иначе срезал бы края.
      style={{
        fontFamily: COVER_FONT.digits, fontStyle: 'italic', fontWeight: 900, lineHeight: 1,
        padding: '0 10px 2px 4px', margin: '0 -10px -2px -4px', boxSizing: 'content-box',
        WebkitTextStroke: `${spec.stroke}px ${COVER_COLOR.navy}`,
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
        boxSizing: 'border-box', padding: spec.padding, borderRadius: spec.radius, background: COVER_COLOR.badge,
        fontWeight: 800, letterSpacing: '0.04em', lineHeight: 1, color: COVER_COLOR.ink,
      }}
    />
  );
}

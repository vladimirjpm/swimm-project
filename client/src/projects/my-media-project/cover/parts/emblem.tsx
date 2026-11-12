import React from 'react';
import type { PrintEmblem } from '../../print/print-data';
import EmblemMark from '../../print/parts/emblem-mark';
import { COVER_COLOR } from '../cover-theme';

const CORNER = { width: 180, height: 204, radius: '0 0 0 104px', emblem: 156, cup: 116 } as const;

/**
 * Скруглённый угол справа сверху (A и B). Рисуется всегда, даже пустым: «место остаётся»
 * (решение Влада 12.11.2026) — значок у серии может появиться позже.
 */
export function EmblemCorner({ emblem }: { emblem: PrintEmblem }) {
  return (
    <div
      style={{
        position: 'absolute', right: 0, top: 0, width: CORNER.width, height: CORNER.height, borderRadius: CORNER.radius,
        background: COVER_COLOR.corner, display: 'flex', alignItems: 'center', justifyContent: 'center',
      }}
    >
      <EmblemMark emblem={emblem} size={CORNER.emblem} cup={CORNER.cup} />
    </div>
  );
}

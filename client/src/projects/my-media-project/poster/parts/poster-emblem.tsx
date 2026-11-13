import React from 'react';
import EmblemMark from '../../print/parts/emblem-mark';
import type { PrintEmblem } from '../../print/print-data';

/** Доля стороны слота, которую занимает кубок (он рисуется плотнее значка серии). */
const CUP_SHARE = 0.75;

/**
 * Есть ли на плакате блок значка. Решение Влада (12.11.2026): значок серии; без значка у
 * чемпионата — кубок; иначе блок убирается и результаты поднимаются (на обложке место
 * остаётся пустым, на плакате значок крупный — пустое место было бы дырой).
 */
export const hasEmblem = (emblem: PrintEmblem): boolean => emblem.kind !== 'empty';

/** Значок соревнования в квадратном слоте со стороной `size`. */
export default function PosterEmblem({ emblem, size, style }: { emblem: PrintEmblem; size: number; style?: React.CSSProperties }) {
  return (
    <div style={{ width: size, height: size, flex: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center', ...style }}>
      <EmblemMark emblem={emblem} size={size} cup={Math.round(size * CUP_SHARE)} />
    </div>
  );
}

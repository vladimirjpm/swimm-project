import React from 'react';
import type { CoverEmblem } from '../cover-data';
import { COVER_COLOR } from '../cover-theme';

/** Кубок чемпионата — рисунок макета (на сайте кубок эмодзи, а эмодзи у каждой системы свой). */
function CupIcon({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" role="img" aria-label="Championship">
      <path d="M14 6h20v12a10 10 0 0 1-20 0z" fill="#f2b632" />
      <path d="M14 9H7v4a7 7 0 0 0 7 7M34 9h7v4a7 7 0 0 1-7 7" fill="none" stroke="#f2b632" strokeWidth="3" />
      <path d="M21 28h6v8h-6z" fill="#e09a1a" />
      <path d="M15 36h18v5H15z" fill="#f2b632" />
    </svg>
  );
}

/** Значок серии, кубок или ничего. ЧТО из трёх — решает `coverEmblem` (`cover-data.ts`). */
export function EmblemMark({ emblem, size, cup }: { emblem: CoverEmblem; size: number; cup: number }) {
  if (emblem.kind === 'image') {
    return <img src={emblem.url} alt="" style={{ width: size, height: size, objectFit: 'contain' }} />;
  }
  return emblem.kind === 'cup' ? <CupIcon size={cup} /> : null;
}

const CORNER = { width: 180, height: 204, radius: '0 0 0 104px', emblem: 156, cup: 116 } as const;

/**
 * Скруглённый угол справа сверху (A и B). Рисуется всегда, даже пустым: «место остаётся»
 * (решение Влада 12.11.2026) — значок у серии может появиться позже.
 */
export function EmblemCorner({ emblem }: { emblem: CoverEmblem }) {
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

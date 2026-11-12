import React from 'react';
import type { PrintEmblem } from '../print-data';

/** Кубок чемпионата — рисунок макета (на сайте кубок эмодзи, а эмодзи у каждой системы свой). */
export function CupIcon({ size }: { size: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" role="img" aria-label="Championship" style={{ flex: 'none' }}>
      <path d="M14 6h20v12a10 10 0 0 1-20 0z" fill="#f2b632" />
      <path d="M14 9H7v4a7 7 0 0 0 7 7M34 9h7v4a7 7 0 0 1-7 7" fill="none" stroke="#f2b632" strokeWidth="3" />
      <path d="M21 28h6v8h-6z" fill="#e09a1a" />
      <path d="M15 36h18v5H15z" fill="#f2b632" />
    </svg>
  );
}

/** Значок серии, кубок или ничего. ЧТО из трёх — решает `printEmblem` (`print-data.ts`). */
export default function EmblemMark({ emblem, size, cup }: { emblem: PrintEmblem; size: number; cup: number }) {
  if (emblem.kind === 'image') {
    return <img src={emblem.url} alt="" style={{ width: size, height: size, flex: 'none', objectFit: 'contain' }} />;
  }
  return emblem.kind === 'cup' ? <CupIcon size={cup} /> : null;
}

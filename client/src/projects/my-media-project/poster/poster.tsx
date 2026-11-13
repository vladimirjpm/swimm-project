import React from 'react';
import type { PhotoOptions } from '../print/dialog/photo-controls';
import type { PosterData, PosterLayout } from './poster-data';
import { POSTER_H, POSTER_W } from './poster-theme';
import PosterA from './layouts/poster-a';
import PosterB from './layouts/poster-b';
import PosterC from './layouts/poster-c';
import '../print/print.css';

// Плакат выбранной раскладки (хендофф `!design_handoff/my-media-handoff-nov2026`, задача 2).
// Рисуется в ФИКСИРОВАННОМ размере 1414×2000: предпросмотр уменьшает его transform-ом
// (`print/dialog/print-preview.tsx`), экспорт снимает как есть (`print/print-export.ts`).
// `forwardRef` — экспорту нужен именно этот узел.

/** У раскладки B фото нет (макет). */
export const POSTER_HAS_PHOTO: Record<PosterLayout, boolean> = { A: true, B: false, C: true };

function Layout({ layout, data, options }: { layout: PosterLayout; data: PosterData; options: PhotoOptions }) {
  if (layout === 'B') return <PosterB data={data} />;
  return layout === 'A' ? <PosterA data={data} options={options} /> : <PosterC data={data} options={options} />;
}

const Poster = React.forwardRef<HTMLDivElement, { layout: PosterLayout; data: PosterData; options: PhotoOptions }>(
  (props, ref) => (
    <div ref={ref} style={{ width: POSTER_W, height: POSTER_H }}>
      <Layout {...props} />
    </div>
  ),
);
Poster.displayName = 'Poster';

export default Poster;

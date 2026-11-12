import React from 'react';
import type { CoverData, CoverLayout, CoverOptions } from './cover-data';
import { COVER_H, COVER_W } from './cover-theme';
import CoverA from './layouts/cover-a';
import CoverB from './layouts/cover-b';
import CoverC from './layouts/cover-c';
import './cover.css';

// Обложка YouTube выбранной раскладки (хендофф `!design_handoff/my-media-handoff-nov2026`,
// задача 2). Рисуется в ФИКСИРОВАННОМ размере 1280×720: предпросмотр уменьшает её transform-ом
// (`cover-preview.tsx`), экспорт снимает как есть (`cover-export.ts`). `forwardRef` — экспорту
// нужен именно этот узел.

function Layout({ layout, data, options }: { layout: CoverLayout; data: CoverData; options: CoverOptions }) {
  if (layout === 'A') return <CoverA data={data} />;
  return layout === 'B' ? <CoverB data={data} options={options} /> : <CoverC data={data} options={options} />;
}

const Cover = React.forwardRef<HTMLDivElement, { layout: CoverLayout; data: CoverData; options: CoverOptions }>(
  (props, ref) => (
    <div ref={ref} style={{ width: COVER_W, height: COVER_H }}>
      <Layout {...props} />
    </div>
  ),
);
Cover.displayName = 'Cover';

export default Cover;

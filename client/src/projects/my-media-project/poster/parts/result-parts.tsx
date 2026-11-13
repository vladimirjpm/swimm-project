import React from 'react';
import { TimeText } from '../../print/parts/result-text';
import StylePlate from '../../print/parts/style-plate';
import type { PosterRow } from '../poster-data';
import { scaled } from '../poster-theme';

/** Плита стиля в размерах макета (масштаб 1). */
export interface PlateSize {
  width: number;
  icon: number;
  len: number;
  radius: number;
}

/** Плита со стилем и дистанцией строки, в масштабе строки. */
export function RowPlate({ row, size, scale }: { row: PosterRow; size: PlateSize; scale: number }) {
  return (
    <StylePlate
      styleName={row.styleName}
      distanceLabel={row.distanceLabel}
      spec={{
        width: scaled(size.width, scale), icon: scaled(size.icon, scale), len: scaled(size.len, scale),
        radius: scaled(size.radius, scale), padding: `${scaled(8, scale)}px 0 ${scaled(6, scale)}px`,
      }}
    />
  );
}

/** Длинное время (1500 м) ужимается не сильнее этой доли кегля. */
const TIME_MIN_SHARE = 0.6;

/** Время строки в масштабе строки; `stroke` — тёмная обводка (0 — без неё). */
export function RowTime({ text, size, stroke, scale, color }: {
  text: string;
  size: number;
  stroke: number;
  scale: number;
  color?: string;
}) {
  const max = scaled(size, scale);
  return (
    <span style={{ minWidth: 0, maxWidth: '100%', color }}>
      <TimeText text={text} spec={{ max, min: Math.round(max * TIME_MIN_SHARE), stroke: stroke && Math.max(1, scaled(stroke, scale)) }} />
    </span>
  );
}

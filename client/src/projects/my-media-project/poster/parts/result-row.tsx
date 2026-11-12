import React from 'react';
import type { PosterRow } from '../poster-data';
import { scaled } from '../poster-theme';
import { BadgeChip, RibbonPlace } from './poster-marks';
import { RowPlate, RowTime, type PlateSize } from './result-parts';

// Строка результата раскладки A (макет `PosterA.dc.html`): метка, время, плита стиля, медаль.

/** Строка в масштабе 1: ширина панели макета без её полей и высота самой высокой ячейки. */
export const RESULT_ROW = { width: 629, height: 108 } as const;

const COLUMNS = { badge: 76, plate: 160, medal: 50, gap: 14 } as const;
const PLATE: PlateSize = { width: 160, icon: 146, len: 32, radius: 16 };
const TIME = { size: 72, stroke: 4 } as const;
const BADGE = { size: 28, radius: 12 } as const;
const PLACE_NUMBER = 44;

export default function ResultRow({ row, scale }: { row: PosterRow; scale: number }) {
  const px = (value: number) => scaled(value, scale);
  return (
    <div
      style={{
        display: 'grid', alignItems: 'center', gap: px(COLUMNS.gap), height: px(RESULT_ROW.height),
        gridTemplateColumns: `${px(COLUMNS.badge)}px minmax(0, 1fr) ${px(COLUMNS.plate)}px ${px(COLUMNS.medal)}px`,
      }}
    >
      <span style={{ display: 'flex' }}>
        {row.badge && (
          <BadgeChip badge={row.badge} text={row.badge.short} size={px(BADGE.size)} padding={`${px(8)}px ${px(10)}px`} radius={px(BADGE.radius)} />
        )}
      </span>
      <RowTime text={row.timeText} size={TIME.size} stroke={TIME.stroke} scale={scale} />
      <RowPlate row={row} size={PLATE} scale={scale} />
      <span style={{ display: 'flex', justifyContent: 'center' }}>
        <RibbonPlace place={row.place} width={px(COLUMNS.medal)} numberSize={px(PLACE_NUMBER)} />
      </span>
    </div>
  );
}

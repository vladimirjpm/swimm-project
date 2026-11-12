import React from 'react';
import type { PosterRow } from '../poster-data';
import { scaled } from '../poster-theme';
import { BadgeChip, RibbonPlace } from './poster-marks';
import { RowPlate, RowTime, type PlateSize } from './result-parts';

// Ячейка результата раскладки C (макет `PosterC.dc.html`): столбиком медаль, плита стиля,
// время, метка.

/** Ячейка в масштабе 1: пятая часть ленты и высота столбика с меткой. */
export const RESULT_CELL = { width: 230, height: 342 } as const;

const GAP = 12;
const MEDAL = { width: 44, height: 94, number: 56 } as const;
const PLATE: PlateSize = { width: 190, icon: 160, len: 32, radius: 16 };
const TIME = { size: 58, stroke: 3 } as const;
const BADGE = { size: 24, radius: 10 } as const;

export default function ResultCell({ row, scale }: { row: PosterRow; scale: number }) {
  const px = (value: number) => scaled(value, scale);
  return (
    <div style={{ height: px(RESULT_CELL.height), display: 'flex', flexDirection: 'column', alignItems: 'center', gap: px(GAP) }}>
      {/* Слот медали держит высоту и без медали: плиты соседних ячеек стоят на одной линии. */}
      <span style={{ height: px(MEDAL.height), flex: 'none', display: 'flex', alignItems: 'center' }}>
        <RibbonPlace place={row.place} width={px(MEDAL.width)} numberSize={px(MEDAL.number)} />
      </span>
      <RowPlate row={row} size={PLATE} scale={scale} />
      <RowTime text={row.timeText} size={TIME.size} stroke={TIME.stroke} scale={scale} />
      {row.badge && (
        <BadgeChip badge={row.badge} text={row.badge.short} size={px(BADGE.size)} padding={`${px(6)}px ${px(12)}px`} radius={px(BADGE.radius)} />
      )}
    </div>
  );
}

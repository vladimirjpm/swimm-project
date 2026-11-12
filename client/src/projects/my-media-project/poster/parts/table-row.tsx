import React from 'react';
import type { PosterRow } from '../poster-data';
import { POSTER_COLOR, scaled } from '../poster-theme';
import { BadgeChip, CirclePlace } from './poster-marks';
import { RowPlate, RowTime, type PlateSize } from './result-parts';

// Строка таблицы раскладки B (макет `PosterB.dc.html`): место кружком, плита стиля, время,
// справа метка словами; у эстафеты без метки — контурная плашка `Relay`.

/** Строка в масштабе 1: плита стиля (самая высокая ячейка, 136) и поля по 20 сверху и снизу. */
export const TABLE_ROW_HEIGHT = 176;

const ROW = { side: 40, gap: 30, plateColumn: 210, placeColumn: 110, rule: 2 } as const;
const PLATE: PlateSize = { width: 210, icon: 190, len: 36, radius: 18 };
const TIME = { size: 92 } as const;
const BADGE = { size: 34, radius: 16 } as const;
const RELAY = { size: 30, border: 3 } as const;

function RelayChip({ scale }: { scale: number }) {
  const px = (value: number) => scaled(value, scale);
  return (
    <span
      style={{
        padding: `${px(8)}px ${px(18)}px`, border: `${Math.max(1, px(RELAY.border))}px solid ${POSTER_COLOR.muted}`,
        borderRadius: px(BADGE.radius), fontSize: px(RELAY.size), fontWeight: 800, lineHeight: 1, color: POSTER_COLOR.muted,
      }}
    >
      Relay
    </span>
  );
}

export default function TableRow({ row, scale, first }: { row: PosterRow; scale: number; first: boolean }) {
  const px = (value: number) => scaled(value, scale);
  return (
    <div
      style={{
        display: 'grid', alignItems: 'center', gap: px(ROW.gap), boxSizing: 'border-box', height: px(TABLE_ROW_HEIGHT),
        gridTemplateColumns: `${px(ROW.placeColumn)}px ${px(ROW.plateColumn)}px minmax(0, 1fr) auto`,
        padding: `0 ${ROW.side}px`, borderTop: first ? undefined : `${ROW.rule}px solid ${POSTER_COLOR.rule}`,
      }}
    >
      <CirclePlace place={row.place} scale={scale} />
      <RowPlate row={row} size={PLATE} scale={scale} />
      {/* Время рекордного заплыва — золотом (макет). */}
      <RowTime text={row.timeText} size={TIME.size} stroke={0} scale={scale} color={row.badge?.kind === 'record' ? POSTER_COLOR.gold : undefined} />
      <span style={{ display: 'flex' }}>
        {row.badge
          ? <BadgeChip badge={row.badge} text={row.badge.long} size={px(BADGE.size)} padding={`${px(10)}px ${px(20)}px`} radius={px(BADGE.radius)} />
          : row.isRelay && <RelayChip scale={scale} />}
      </span>
    </div>
  );
}

import React from 'react';
import type { CoverData, CoverOptions } from '../cover-data';
import { COVER_CLASS, COVER_COLOR, COVER_ROOT } from '../cover-theme';
import Background from '../parts/cover-background';
import { EmblemCorner } from '../parts/emblem';
import PhotoAndName from '../parts/photo-and-name';
import { PlaceCircle } from '../../print/parts/place';
import { Badge, TimeText, type BadgeSpec, type TimeSpec } from '../../print/parts/result-text';
import StylePlate, { type StylePlateSpec } from '../../print/parts/style-plate';
import TopBlock from '../parts/top-block';

// Раскладка B (макет `CoverB2.dc.html`): верх как у A; панель результата на половину ширины
// слева внизу, справа — необязательное фото и имя под ним.

const PAGE = { padding: '40px 48px', gap: 24, columnsGap: 40 } as const;
const PANEL = { width: 652, padding: '18px 22px', radius: 36, gap: 16, textGap: 6 } as const;
const PLATE: StylePlateSpec = { width: 172, icon: 150, len: 32, radius: 18, padding: '8px 0 6px' };
const TIME: TimeSpec = { max: 82, min: 48, stroke: 3 };
const BADGE: BadgeSpec = { max: 40, min: 22, padding: '8px 14px', radius: 12 };

export default function CoverB({ data, options }: { data: CoverData; options: CoverOptions }) {
  return (
    <div className={COVER_CLASS} style={{ ...COVER_ROOT, padding: PAGE.padding, flexDirection: 'column', gap: PAGE.gap }}>
      <Background />
      <EmblemCorner emblem={data.emblem} />
      <TopBlock data={data} />
      <div style={{ flex: '1 1 0', minHeight: 0, display: 'flex', alignItems: 'stretch', gap: PAGE.columnsGap }}>
        <div
          style={{
            width: PANEL.width, flex: 'none', alignSelf: 'flex-end', boxSizing: 'border-box', padding: PANEL.padding,
            borderRadius: PANEL.radius, background: COVER_COLOR.panel, display: 'flex', alignItems: 'center', gap: PANEL.gap,
          }}
        >
          <PlaceCircle place={data.place} size="medal-80" />
          <StylePlate styleName={data.styleName} distanceLabel={data.distanceLabel} spec={PLATE} />
          <div style={{ flex: '1 1 0', minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: PANEL.textGap }}>
            <TimeText text={data.timeText} spec={TIME} />
            <Badge text={data.badge} spec={BADGE} />
          </div>
        </div>
        <PhotoAndName data={data} options={options} />
      </div>
    </div>
  );
}

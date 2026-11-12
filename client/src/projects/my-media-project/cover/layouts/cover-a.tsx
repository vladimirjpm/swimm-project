import React from 'react';
import type { CoverData } from '../cover-data';
import { COVER_CLASS, COVER_COLOR, COVER_ROOT } from '../cover-theme';
import Background from '../parts/background';
import { EmblemCorner } from '../parts/emblem';
import NameRow, { type NameSpec } from '../parts/name-row';
import { PlaceCircle } from '../parts/place';
import { Badge, TimeText, type BadgeSpec, type TimeSpec } from '../parts/result-text';
import StylePlate, { type StylePlateSpec } from '../parts/style-plate';
import TopBlock from '../parts/top-block';

// Раскладка A (макет `CoverB.dc.html`): результат одной крупной строкой, имя с логотипом
// клуба мелко внизу. Переключателей нет: имя есть всегда, фото нет.

const PAGE = { padding: '40px 48px', gap: 18 } as const;
const PANEL = { padding: '18px 40px 18px 26px', radius: 40, gap: 34, textGap: 8 } as const;
const PLATE: StylePlateSpec = { width: 290, icon: 250, len: 44, radius: 22, padding: '10px 0 8px' };
const TIME: TimeSpec = { max: 132, min: 84, stroke: 4 };
const BADGE: BadgeSpec = { max: 50, min: 30, padding: '10px 22px', radius: 16 };
const NAME: NameSpec = { logo: 62, size: 54 };

export default function CoverA({ data }: { data: CoverData }) {
  return (
    <div className={COVER_CLASS} style={{ ...COVER_ROOT, padding: PAGE.padding, flexDirection: 'column', gap: PAGE.gap }}>
      <Background />
      <EmblemCorner emblem={data.emblem} />
      <TopBlock data={data} />
      <div
        style={{
          marginTop: 'auto', alignSelf: 'center', maxWidth: '100%', boxSizing: 'border-box', padding: PANEL.padding,
          borderRadius: PANEL.radius, background: COVER_COLOR.panel, display: 'flex', alignItems: 'center', gap: PANEL.gap,
        }}
      >
        <PlaceCircle place={data.place} size="medal-116" />
        <StylePlate data={data} spec={PLATE} />
        <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: PANEL.textGap }}>
          <TimeText text={data.timeText} spec={TIME} />
          <Badge text={data.badge} spec={BADGE} />
        </div>
      </div>
      <NameRow data={data} spec={NAME} />
    </div>
  );
}

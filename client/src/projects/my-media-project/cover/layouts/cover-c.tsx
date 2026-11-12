import React from 'react';
import { hasTileContent } from '../../../../utils/helpers/competition-source';
import type { CoverData, CoverOptions } from '../cover-data';
import { PRINT_FONT } from '../../print/print-fonts';
import { COVER_CLASS, COVER_COLOR, COVER_ROOT } from '../cover-theme';
import Background from '../parts/cover-background';
import EmblemMark from '../../print/parts/emblem-mark';
import FitText from '../../print/parts/fit-text';
import PhotoAndName from '../parts/photo-and-name';
import { PlaceCircle, RibbonMedal } from '../../print/parts/place';
import { Badge, TimeText, type BadgeSpec, type TimeSpec } from '../../print/parts/result-text';
import PrintTile from '../../print/parts/print-tile';
import StylePlate, { type StylePlateSpec } from '../../print/parts/style-plate';

// Раскладка C (макет `CoverG.dc.html`): слева тёмная колонка — дни соревнования и год, значок,
// плитка; справа название, результат столбиком, необязательное фото и имя под ним.

const COLUMN = { width: 300, padding: '36px 24px', range: { max: 26, min: 16 }, year: 100, emblem: 220, cup: 170 } as const;
const MAIN = { padding: '36px 48px 40px', gap: 22, columnsGap: 40, title: { max: 60, min: 40 } } as const;
const PANEL = { width: 400, padding: '14px 20px', radius: 36, gap: 12 } as const;
const PLATE: StylePlateSpec = { width: 330, icon: 280, len: 50, radius: 22, padding: '10px 0 8px' };
const TIME: TimeSpec = { max: 92, min: 56, stroke: 3 };
const BADGE: BadgeSpec = { max: 44, min: 26, padding: '9px 20px', radius: 14 };
/** Ширина медали с лентой; без медали — номер места кружком без заливки, как в строке списка. */
const RIBBON_WIDTH = 40;

/** Левая колонка: дни и год сверху, значок посередине, плитка внизу. */
function CompetitionColumn({ data }: { data: CoverData }) {
  return (
    <div
      style={{
        width: COLUMN.width, flex: 'none', boxSizing: 'border-box', padding: COLUMN.padding, background: COVER_COLOR.column,
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'space-between',
      }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4, maxWidth: '100%' }}>
        <FitText text={data.rangeLabel} size={COLUMN.range} style={{ fontWeight: 800, letterSpacing: '0.06em', lineHeight: 1 }} />
        <span style={{ fontFamily: PRINT_FONT.digits, fontStyle: 'italic', fontSize: COLUMN.year, fontWeight: 900, lineHeight: 1, color: COVER_COLOR.yearGold }}>
          {data.year}
        </span>
      </div>
      {/* Место значка держит размер и без значка — колонка не перестраивается. */}
      <div style={{ width: COLUMN.emblem, height: COLUMN.emblem, flex: 'none', margin: hasTileContent(data.tile) ? undefined : 'auto 0', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <EmblemMark emblem={data.emblem} size={COLUMN.emblem} cup={COLUMN.cup} />
      </div>
      {/* Почти пустой плитки нет (`PrintTile`): значок тогда стоит по центру оставшейся высоты. */}
      <PrintTile tile={data.tile} size="cover-lg" />
    </div>
  );
}

/** Панель результата столбиком: медаль, плита стиля, время, метка. */
function ResultColumn({ data }: { data: CoverData }) {
  return (
    <div
      style={{
        width: PANEL.width, flex: 'none', boxSizing: 'border-box', padding: PANEL.padding, borderRadius: PANEL.radius,
        background: COVER_COLOR.panel, display: 'flex', flexDirection: 'column', alignItems: 'center',
        justifyContent: 'center', gap: PANEL.gap,
      }}
    >
      {data.place?.medal ? <RibbonMedal place={data.place} width={RIBBON_WIDTH} /> : <PlaceCircle place={data.place} size="medal-80" />}
      <StylePlate styleName={data.styleName} distanceLabel={data.distanceLabel} spec={PLATE} />
      <TimeText text={data.timeText} spec={TIME} />
      <Badge text={data.badge} spec={BADGE} />
    </div>
  );
}

export default function CoverC({ data, options }: { data: CoverData; options: CoverOptions }) {
  return (
    <div className={COVER_CLASS} style={COVER_ROOT}>
      <Background dim />
      <CompetitionColumn data={data} />
      <div style={{ flex: '1 1 0', minWidth: 0, boxSizing: 'border-box', padding: MAIN.padding, display: 'flex', flexDirection: 'column', gap: MAIN.gap }}>
        <FitText
          text={data.competitionName}
          dir="rtl"
          size={MAIN.title}
          wrap
          style={{ flex: 'none', alignSelf: 'center', textAlign: 'center', fontFamily: PRINT_FONT.hebrew, fontWeight: 900, lineHeight: 1.1 }}
        />
        <div style={{ flex: '1 1 0', minHeight: 0, display: 'flex', alignItems: 'stretch', gap: MAIN.columnsGap }}>
          <ResultColumn data={data} />
          <PhotoAndName data={data} options={options} />
        </div>
      </div>
    </div>
  );
}

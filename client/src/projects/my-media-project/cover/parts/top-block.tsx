import React from 'react';
import CompetitionTile from '../../../results-main-project/components/competition-header/competition-tile';
import type { CoverData } from '../cover-data';
import { COVER_FONT } from '../cover-fonts';
import { COVER_COLOR } from '../cover-theme';
import FitText from './fit-text';

const TOP = {
  gap: 22,
  /** Ширина под название: полотно минус поля, плитка слева и угол справа. */
  nameWidth: 860,
  name: { max: 66, min: 40 },
  day: 44,
  year: 84,
  plateRadius: 28,
} as const;

/** Плашка-календарь: день заплыва на оранжевой полосе, год под ней. */
function DatePlate({ day, year }: { day: string; year: string }) {
  return (
    <div style={{ flex: 'none', borderRadius: TOP.plateRadius, overflow: 'hidden', background: '#ffffff', display: 'flex', flexDirection: 'column', textAlign: 'center' }}>
      <span style={{ padding: '12px 40px 10px', background: COVER_COLOR.dateStrip, fontSize: TOP.day, fontWeight: 800, letterSpacing: '0.08em', lineHeight: 1, color: COVER_COLOR.ink, whiteSpace: 'nowrap' }}>
        {day}
      </span>
      <span style={{ padding: '10px 40px 14px', fontFamily: COVER_FONT.digits, fontSize: TOP.year, fontWeight: 900, lineHeight: 1, color: COVER_COLOR.ink }}>
        {year}
      </span>
    </div>
  );
}

/** Верх раскладок A и B: плитка слева, название и плашка-календарь по центру. */
export default function TopBlock({ data }: { data: CoverData }) {
  return (
    <div style={{ flex: 'none', display: 'grid', gridTemplateColumns: '1fr auto 1fr', alignItems: 'start', gap: TOP.gap }}>
      <CompetitionTile {...data.tile} size="cover" />
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10, maxWidth: TOP.nameWidth }}>
        <FitText
          text={data.competitionName}
          dir="rtl"
          size={TOP.name}
          wrap
          style={{ textAlign: 'center', fontFamily: COVER_FONT.hebrew, fontWeight: 900, lineHeight: 1.1 }}
        />
        <DatePlate day={data.dayLabel} year={data.year} />
      </div>
      <span />
    </div>
  );
}

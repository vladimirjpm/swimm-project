import React from 'react';
import { medalColor } from '../../print/parts/place';
import { PRINT_FONT } from '../../print/print-fonts';
import type { PosterSummary } from '../poster-data';
import { POSTER_COLOR } from '../poster-theme';

const TALLY = { disc: 110, count: 120, gap: 44, pairGap: 16, note: 40 } as const;

function Medals({ place, label, count }: { place: 1 | 2 | 3; label: string; count: number }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: TALLY.pairGap }}>
      <span role="img" aria-label={label} style={{ width: TALLY.disc, height: TALLY.disc, borderRadius: 999, background: medalColor(place) }} />
      <span style={{ fontFamily: PRINT_FONT.digits, fontSize: TALLY.count, fontWeight: 900, lineHeight: 1 }}>{count}</span>
    </div>
  );
}

/** Счёт медалей раскладки B: золото, серебро, бронза и справа итог (`6 swims`, `1 age record`). */
export default function MedalTally({ summary }: { summary: PosterSummary }) {
  return (
    <div style={{ flex: 'none', height: TALLY.count, display: 'flex', alignItems: 'center', gap: TALLY.gap }}>
      <Medals place={1} label="Gold medals" count={summary.gold} />
      <Medals place={2} label="Silver medals" count={summary.silver} />
      <Medals place={3} label="Bronze medals" count={summary.bronze} />
      <span style={{ marginLeft: 'auto', fontSize: TALLY.note, fontWeight: 700, lineHeight: 1.3, textAlign: 'right', color: POSTER_COLOR.muted }}>
        {summary.swimsLabel}
        {summary.recordsLabel && <><br />{summary.recordsLabel}</>}
      </span>
    </div>
  );
}

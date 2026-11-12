import React from 'react';
import UI_ClubLogo from '../../../components/mix/club-logo/club-logo';
import Background from '../../print/parts/background';
import PrintTile from '../../print/parts/print-tile';
import type { PosterData } from '../poster-data';
import { fitGrid } from '../poster-fit';
import { POSTER_BG_FOCUS, POSTER_CLASS, POSTER_COLOR, POSTER_H, POSTER_ROOT, POSTER_W } from '../poster-theme';
import MedalTally from '../parts/medal-tally';
import PosterEmblem, { hasEmblem } from '../parts/poster-emblem';
import { SeasonLine, SwimmerName } from '../parts/poster-text';
import TableRow, { TABLE_ROW_HEIGHT } from '../parts/table-row';

// Раскладка B (макет `PosterB.dc.html`, с правками Влада на холсте): все результаты таблицей.
// Сверху логотип клуба, строка сезона и плитка; имя пловца; значок соревнования между двумя
// линиями; счёт медалей; таблица результатов. Фото нет. Названия соревнования в макете нет —
// его несёт значок; без значка (и не чемпионат) блок значка убирается.

const PAGE = { padding: { top: 76, side: 80, bottom: 64 }, gap: 36 } as const;
const HEAD = { height: 130, logo: 130, season: 58, gap: 24 } as const;
const NAME = { height: 196, size: { max: 196, min: 110 } } as const;
const EMBLEM = { size: 230, gap: 40, line: 4 } as const;
const TALLY_HEIGHT = 120;
/** Потолок увеличения строк: без блока значка таблице достаётся больше места. */
const TABLE = { radius: 40, maxScale: 1.25 } as const;

const CONTENT_W = POSTER_W - 2 * PAGE.padding.side;
/** Высота под таблицу: полотно минус поля, шапка, имя, счёт и зазоры между блоками. */
const tableRoom = (emblem: boolean): number => POSTER_H - PAGE.padding.top - PAGE.padding.bottom
  - HEAD.height - NAME.height - TALLY_HEIGHT - 3 * PAGE.gap - (emblem ? EMBLEM.size + PAGE.gap : 0);

function Head({ data }: { data: PosterData }) {
  return (
    <div style={{ flex: 'none', height: HEAD.height, display: 'grid', gridTemplateColumns: '1fr auto 1fr', alignItems: 'center', gap: HEAD.gap }}>
      <span style={{ justifySelf: 'start' }}>{data.clubName && <UI_ClubLogo clubName={data.clubName} size={HEAD.logo} />}</span>
      <SeasonLine data={data} size={HEAD.season} />
      <span style={{ justifySelf: 'end', display: 'flex' }}><PrintTile tile={data.tile} size="cover-lg" /></span>
    </div>
  );
}

function EmblemRule({ data }: { data: PosterData }) {
  const line: React.CSSProperties = { flex: '1 1 0', height: EMBLEM.line, borderRadius: 999, background: POSTER_COLOR.line };
  return (
    <div style={{ flex: 'none', display: 'flex', alignItems: 'center', gap: EMBLEM.gap }}>
      <span aria-hidden="true" style={line} />
      <PosterEmblem emblem={data.emblem} size={EMBLEM.size} />
      <span aria-hidden="true" style={line} />
    </div>
  );
}

function Table({ data, room }: { data: PosterData; room: number }) {
  const { scale } = fitGrid({
    count: data.rows.length, width: CONTENT_W, height: room, cell: { width: CONTENT_W, height: TABLE_ROW_HEIGHT },
    gap: { x: 0, y: 0 }, columns: [1], maxScale: TABLE.maxScale,
  });
  return (
    <div style={{ flex: 'none', borderRadius: TABLE.radius, background: POSTER_COLOR.panelStrong, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
      {data.rows.map((row, i) => <TableRow key={row.id} row={row} scale={scale} first={i === 0} />)}
    </div>
  );
}

export default function PosterB({ data }: { data: PosterData }) {
  const emblem = hasEmblem(data.emblem);
  return (
    <div
      className={POSTER_CLASS}
      style={{
        ...POSTER_ROOT, display: 'flex', flexDirection: 'column', gap: PAGE.gap,
        padding: `${PAGE.padding.top}px ${PAGE.padding.side}px ${PAGE.padding.bottom}px`,
      }}
    >
      <Background width={POSTER_W} height={POSTER_H} focus={POSTER_BG_FOCUS} dim={POSTER_COLOR.dim} />
      <Head data={data} />
      <div style={{ flex: 'none', height: NAME.height, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <SwimmerName name={data.swimmerName} size={NAME.size} style={{ width: CONTENT_W }} />
      </div>
      {emblem && <EmblemRule data={data} />}
      <MedalTally summary={data.summary} />
      <Table data={data} room={tableRoom(emblem)} />
    </div>
  );
}

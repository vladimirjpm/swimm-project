import React from 'react';
import CompetitionTile from '../../../results-main-project/components/competition-header/competition-tile';
import UI_ClubLogo from '../../../components/mix/club-logo/club-logo';
import type { PhotoOptions } from '../../print/dialog/photo-controls';
import Background from '../../print/parts/background';
import PhotoSlot from '../../print/parts/photo';
import type { PosterData } from '../poster-data';
import { fitGrid } from '../poster-fit';
import { POSTER_BG_FOCUS, POSTER_CLASS, POSTER_COLOR, POSTER_H, POSTER_INNER_W, POSTER_MARGIN, POSTER_ROOT, POSTER_W } from '../poster-theme';
import PosterEmblem, { hasEmblem } from '../parts/poster-emblem';
import { CompetitionName, SeasonLine, SwimmerName, Year } from '../parts/poster-text';
import ResultRow, { RESULT_ROW } from '../parts/result-row';
import ResultsGrid from '../parts/results-grid';

// Раскладка A (макет `PosterA.dc.html`, с правками Влада на холсте): логотип клуба, год по
// центру, плитка; имя пловца; тёмная полоса с названием соревнования; значок соревнования;
// ниже — фото слева и результаты списком справа. Без фото список занимает всю ширину; без
// значка (и не чемпионат) блок значка убирается, и низ поднимается под полосу.

const HEAD = { yearTop: 44, year: 180, logo: { left: POSTER_MARGIN, top: 36, size: 196 }, tile: { right: POSTER_MARGIN, top: 46 } } as const;
const NAME = { top: 262, side: 60, size: { max: 190, min: 110 } } as const;
const STRIP = { top: 486, height: 176, radius: 36, gap: 6, side: 40, name: { max: 72, min: 46 }, season: 44 } as const;
const EMBLEM = { top: 690, size: 400 } as const;
/** Низ плаката: фото и панель. `top` — со значком; без значка низ начинается с `topNoEmblem`. */
const LOWER = { top: 1139, topNoEmblem: 700, bottom: 1936 } as const;
const PHOTO = { width: 575, radius: 24 } as const;
/** Панель результатов: в макете она на 15px ниже фото и правее поля (правка Влада). */
const PANEL = { left: 713, width: 685, inset: 15, padding: { x: 28, y: 30 }, radius: 36 } as const;
const ROWS_GAP = { x: 40, y: 26 } as const;
/** Потолок увеличения строк, когда результатов мало. */
const ROWS_MAX_SCALE = 1.5;

function Results({ data, left, width, top, height }: { data: PosterData; left: number; width: number; top: number; height: number }) {
  const area = { width: width - 2 * PANEL.padding.x, height: height - 2 * PANEL.padding.y };
  const fit = fitGrid({ ...area, count: data.rows.length, cell: RESULT_ROW, gap: ROWS_GAP, columns: [1, 2], maxScale: ROWS_MAX_SCALE });
  return (
    <ResultsGrid
      fit={fit}
      cellWidth={RESULT_ROW.width}
      gap={ROWS_GAP}
      style={{
        position: 'absolute', left, top, width, height, boxSizing: 'border-box', padding: `${PANEL.padding.y}px ${PANEL.padding.x}px`,
        borderRadius: PANEL.radius, background: POSTER_COLOR.panel,
      }}
    >
      {data.rows.map((row) => <ResultRow key={row.id} row={row} scale={fit.scale} />)}
    </ResultsGrid>
  );
}

export default function PosterA({ data, options }: { data: PosterData; options: PhotoOptions }) {
  const emblem = hasEmblem(data.emblem);
  const lowerTop = emblem ? LOWER.top : LOWER.topNoEmblem;
  const panelTop = lowerTop + PANEL.inset;
  return (
    <div className={POSTER_CLASS} style={POSTER_ROOT}>
      <Background width={POSTER_W} height={POSTER_H} focus={POSTER_BG_FOCUS} />
      <Year year={data.year} size={HEAD.year} style={{ position: 'absolute', left: 0, top: HEAD.yearTop, width: POSTER_W, textAlign: 'center' }} />
      {data.clubName && (
        <div style={{ position: 'absolute', left: HEAD.logo.left, top: HEAD.logo.top }}>
          <UI_ClubLogo clubName={data.clubName} size={HEAD.logo.size} />
        </div>
      )}
      <div style={{ position: 'absolute', right: HEAD.tile.right, top: HEAD.tile.top }}>
        <CompetitionTile {...data.tile} size="poster-lg" />
      </div>
      <div style={{ position: 'absolute', left: NAME.side, top: NAME.top, width: POSTER_W - 2 * NAME.side }}>
        <SwimmerName name={data.swimmerName} size={NAME.size} />
      </div>
      <div
        style={{
          position: 'absolute', left: POSTER_MARGIN, top: STRIP.top, width: POSTER_INNER_W, height: STRIP.height, boxSizing: 'border-box',
          padding: `0 ${STRIP.side}px`, borderRadius: STRIP.radius, background: POSTER_COLOR.panel,
          display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: STRIP.gap,
        }}
      >
        <CompetitionName name={data.competitionName} size={STRIP.name} />
        <SeasonLine data={data} size={STRIP.season} />
      </div>
      {emblem && (
        <PosterEmblem emblem={data.emblem} size={EMBLEM.size} style={{ position: 'absolute', left: (POSTER_W - EMBLEM.size) / 2, top: EMBLEM.top }} />
      )}
      {options.photoSlot && (
        <PhotoSlot
          photo={options.photo}
          hintSize={30}
          style={{ position: 'absolute', left: POSTER_MARGIN, top: lowerTop, width: PHOTO.width, height: LOWER.bottom - lowerTop, borderRadius: PHOTO.radius }}
        />
      )}
      <Results
        data={data}
        left={options.photoSlot ? PANEL.left : POSTER_MARGIN}
        width={options.photoSlot ? PANEL.width : POSTER_INNER_W}
        top={panelTop}
        height={LOWER.bottom - panelTop}
      />
    </div>
  );
}

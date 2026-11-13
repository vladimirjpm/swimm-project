import React from 'react';
import { hasTileContent } from '../../../../utils/helpers/competition-source';
import CompetitionTile from '../../../results-main-project/components/competition-header/competition-tile';
import UI_ClubLogo from '../../../components/mix/club-logo/club-logo';
import type { PhotoOptions } from '../../print/dialog/photo-controls';
import Background from '../../print/parts/background';
import PhotoSlot from '../../print/parts/photo';
import type { PosterData } from '../poster-data';
import { fitGrid, type GridFit } from '../poster-fit';
import { POSTER_BG_FOCUS, POSTER_CLASS, POSTER_COLOR, POSTER_H, POSTER_INNER_W, POSTER_MARGIN, POSTER_ROOT, POSTER_W } from '../poster-theme';
import PosterEmblem, { hasEmblem } from '../parts/poster-emblem';
import { CompetitionName, SeasonLine, SwimmerName, Year } from '../parts/poster-text';
import ResultCell, { RESULT_CELL } from '../parts/result-cell';
import ResultsGrid from '../parts/results-grid';
import SeasonIcon from '../parts/season-icon';

// Раскладка C (макет `PosterC.dc.html`): год и значок сезона, логотип клуба и имя; полоса
// «плитка, название, значок»; фото во всю ширину; результаты лентой в пять колонок. Больше
// пяти результатов — вторая лента (фото становится ниже). Без фото результаты занимают всё
// освободившееся место крупной сеткой.

const HEAD = { year: { top: 48, size: 150 }, season: { left: 500, top: 26, size: 140 } } as const;
const NAME = { logo: { left: 32, top: 265, size: 196 }, left: 254, top: 262, size: { max: 196, min: 110 } } as const;
const STRIP = { top: 492, height: 216, side: 36, gap: 36, radius: 36, textGap: 10, name: { max: 72, min: 46 }, season: 46, emblem: 196 } as const;
/** Низ плаката под полосой: фото и лента результатов. */
const LOWER = { top: 744, bottom: 1930, gap: 36 } as const;
const PHOTO_RADIUS = 24;
const BAND = { padding: { x: 20, y: 28 }, radius: 36, perRow: 5, oneRow: 420, twoRows: 620 } as const;
const CELLS_GAP = { x: 12, y: 20 } as const;
/** Без фото: сетка любой ширины и потолок увеличения ячеек. */
const FREE = { columns: [1, 2, 3, 4, 5], maxScale: 1.8 } as const;

const LOWER_HEIGHT = LOWER.bottom - LOWER.top;
const BAND_INNER_W = POSTER_INNER_W - 2 * BAND.padding.x;

/**
 * Сетка ленты. С фото — пять в ряд, как в макете; больше пяти — две ленты поровну (6 → 3 + 3,
 * а не 5 + 1). Без фото — самая крупная сетка на весь низ.
 */
function bandFit(count: number, bandHeight: number, withPhoto: boolean): GridFit {
  return fitGrid({
    count, width: BAND_INNER_W, height: bandHeight - 2 * BAND.padding.y, cell: RESULT_CELL, gap: CELLS_GAP,
    columns: withPhoto ? [count > BAND.perRow ? Math.ceil(count / 2) : BAND.perRow] : [...FREE.columns], maxScale: withPhoto ? 1 : FREE.maxScale,
  });
}

function Band({ data, fit, top, height }: { data: PosterData; fit: GridFit; top: number; height: number }) {
  return (
    <ResultsGrid
      fit={fit}
      cellWidth={RESULT_CELL.width}
      gap={CELLS_GAP}
      stretch
      style={{
        position: 'absolute', left: POSTER_MARGIN, top, width: POSTER_INNER_W, height, boxSizing: 'border-box',
        padding: `${BAND.padding.y}px ${BAND.padding.x}px`, borderRadius: BAND.radius, background: POSTER_COLOR.panel,
      }}
    >
      {data.rows.map((row) => <ResultCell key={row.id} row={row} scale={fit.scale} />)}
    </ResultsGrid>
  );
}

/** Полоса соревнования: плитка, название со строкой сезона, значок (если он есть). */
function CompetitionStrip({ data }: { data: PosterData }) {
  return (
    <div
      style={{
        position: 'absolute', left: POSTER_MARGIN, top: STRIP.top, width: POSTER_INNER_W, height: STRIP.height, boxSizing: 'border-box',
        padding: `0 ${STRIP.side}px`, borderRadius: STRIP.radius, background: POSTER_COLOR.panel, display: 'flex', alignItems: 'center', gap: STRIP.gap,
      }}
    >
      {/* Несодержательная плитка здесь не рисуется вовсе: её заменил бы значок сезона, а он
          на этой раскладке уже стоит крупно рядом с годом. */}
      {hasTileContent(data.tile) && <CompetitionTile {...data.tile} size="poster" />}
      <div style={{ flex: '1 1 0', minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: STRIP.textGap }}>
        <CompetitionName name={data.competitionName} size={STRIP.name} />
        <SeasonLine data={data} size={STRIP.season} />
      </div>
      {hasEmblem(data.emblem) && <PosterEmblem emblem={data.emblem} size={STRIP.emblem} />}
    </div>
  );
}

export default function PosterC({ data, options }: { data: PosterData; options: PhotoOptions }) {
  const bandHeight = !options.photoSlot ? LOWER_HEIGHT : data.rows.length > BAND.perRow ? BAND.twoRows : BAND.oneRow;
  const bandTop = LOWER.bottom - bandHeight;
  return (
    <div className={POSTER_CLASS} style={POSTER_ROOT}>
      <Background width={POSTER_W} height={POSTER_H} focus={POSTER_BG_FOCUS} />
      <Year year={data.year} size={HEAD.year.size} style={{ position: 'absolute', left: POSTER_MARGIN, top: HEAD.year.top }} />
      <SeasonIcon season={data.tile.season} size={HEAD.season.size} style={{ position: 'absolute', left: HEAD.season.left, top: HEAD.season.top }} />
      {data.clubName && (
        <div style={{ position: 'absolute', left: NAME.logo.left, top: NAME.logo.top }}>
          <UI_ClubLogo clubName={data.clubName} size={NAME.logo.size} />
        </div>
      )}
      <div style={{ position: 'absolute', left: NAME.left, top: NAME.top, width: POSTER_W - NAME.left - POSTER_MARGIN }}>
        <SwimmerName name={data.swimmerName} size={NAME.size} />
      </div>
      <CompetitionStrip data={data} />
      {options.photoSlot && (
        <PhotoSlot
          photo={options.photo}
          hintSize={30}
          style={{ position: 'absolute', left: POSTER_MARGIN, top: LOWER.top, width: POSTER_INNER_W, height: bandTop - LOWER.gap - LOWER.top, borderRadius: PHOTO_RADIUS }}
        />
      )}
      <Band data={data} fit={bandFit(data.rows.length, bandHeight, options.photoSlot)} top={bandTop} height={bandHeight} />
    </div>
  );
}

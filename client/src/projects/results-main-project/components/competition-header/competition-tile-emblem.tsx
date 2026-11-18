import React from 'react';
import type { CompetitionTileData } from '../../../../utils/helpers/competition-source';
import UI_SeasonIcon from '../../../components/mix/season-icon/season-icon';

// Плитка со значком серии турнира (docs/plans/series-emblem-plan.md; решение Влада 18.11.2026):
// значок на месте плитки, а то, что обычно в ней, — уменьшенной полосой сверху: сезон, кубок и
// буква категории; возрастная лента, если есть, — снизу, как у обычной плитки. Габарит тот же,
// что у остальных вариантов (`TILE_SIZE_CLASS`). Какой вариант рисовать, решает переключатель
// `CompetitionTile`; картинка не загрузилась — он сам вернётся к обычной плитке (`onError`).

export default function CompetitionTileEmblem({
  letter, ageGroup, season, isChampionship, emblemUrl, sizeClass, onError,
}: CompetitionTileData & { sizeClass: string; onError: () => void }) {
  const strip = Boolean(season || isChampionship || letter);
  return (
    <span className={['comp-tile', 'comp-tile--emblem', sizeClass].filter(Boolean).join(' ')} aria-hidden="true">
      {strip && (
        <span className="comp-tile__strip">
          {season && <UI_SeasonIcon season={season} />}
          {isChampionship && <span>🏆</span>}
          {letter && <span className="comp-tile__strip-letter">{letter}</span>}
        </span>
      )}
      <span className="comp-tile__emblem">
        <img src={emblemUrl ?? ''} alt="" draggable={false} onError={onError} />
      </span>
      {ageGroup && <span className="comp-tile__age">{ageGroup}</span>}
    </span>
  );
}

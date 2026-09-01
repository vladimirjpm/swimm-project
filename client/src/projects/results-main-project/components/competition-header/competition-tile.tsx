import React from 'react';
import type { CompetitionTileData } from '../../../../utils/helpers/competition-source';
import './competition-tile.css';

// Плитка соревнования (design_handoff_competition_overview3, вариант 11c):
// полоса значков (сезон + кубок) · буква группы (K/M) · лента возрастной группы.
// Каждый слот скрывается отдельно, если данных нет; без возраста — модификатор --no-age.

interface Props extends CompetitionTileData {
  /** 'responsive' — полный габарит, ниже 640px сам ужимается до xs (шапка соревнования).
   *  'mini' — 34×42 для стики-компакт-бара мобайла (16b). */
  size?: 'responsive' | 'md' | 'sm' | 'xs' | 'mini';
}

const SIZE_CLASS: Record<NonNullable<Props['size']>, string> = {
  responsive: 'comp-tile--responsive',
  md: '',
  sm: 'comp-tile--sm',
  xs: 'comp-tile--xs',
  mini: 'comp-tile--mini',
};

export default function CompetitionTile({
  letter, ageGroup, season, isChampionship, size = 'responsive',
}: Props) {
  const classes = [
    'comp-tile',
    SIZE_CLASS[size],
    ageGroup ? '' : 'comp-tile--no-age',
  ].filter(Boolean).join(' ');

  return (
    <span className={classes} aria-hidden="true">
      {(season || isChampionship) && (
        <span className="comp-tile__icons">
          {season && <span className="comp-tile__season">{season}</span>}
          {isChampionship && <span className="comp-tile__cup">🏆</span>}
        </span>
      )}
      <span className="comp-tile__letter">{letter}</span>
      {ageGroup && <span className="comp-tile__age">{ageGroup}</span>}
    </span>
  );
}

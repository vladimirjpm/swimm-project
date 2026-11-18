import React from 'react';
import type { CompetitionTileData } from '../../../../utils/helpers/competition-source';
import UI_SeasonIcon from '../../../components/mix/season-icon/season-icon';

// Обычная плитка соревнования (design_handoff_competition_overview3, вариант 11c): полоса значков
// (сезон + кубок) · буква группы (K/M) · лента возрастной группы. Сезон в полосе — эмодзи ❄️ / ☀️
// через общий `UI_SeasonIcon` (решение Влада 13.11.2026), кубок — эмодзи 🏆; кегль им задаёт CSS
// плитки. Каждый слот скрывается отдельно, если данных нет; без возраста — модификатор --no-age.
// Какой вариант рисовать, решает переключатель `CompetitionTile`.

export default function CompetitionTilePlain({
  letter, ageGroup, season, isChampionship, sizeClass,
}: CompetitionTileData & { sizeClass: string }) {
  const classes = ['comp-tile', sizeClass, ageGroup ? '' : 'comp-tile--no-age'].filter(Boolean).join(' ');
  return (
    <span className={classes} aria-hidden="true">
      {(season || isChampionship) && (
        <span className="comp-tile__icons">
          {season && <span className="comp-tile__season"><UI_SeasonIcon season={season} /></span>}
          {isChampionship && <span className="comp-tile__cup">🏆</span>}
        </span>
      )}
      <span className="comp-tile__letter">{letter}</span>
      {ageGroup && <span className="comp-tile__age">{ageGroup}</span>}
    </span>
  );
}

import React from 'react';
import type { CompetitionTileData } from '../../../../utils/helpers/competition-source';
import UI_SeasonIcon from '../../../components/mix/season-icon/season-icon';

// Значок сезона на месте несодержательной плитки (нет ни буквы категории, ни возрастной группы),
// в её габарите — раскладки вокруг не прыгают (решение Влада 13.11.2026: «вместо него поставь
// иконку зимы или лета»). У чемпионата под ним кубок; сезон неизвестен — только кубок. Значок —
// общий `UI_SeasonIcon` (эмодзи). Какой вариант рисовать, решает переключатель `CompetitionTile`.

export default function CompetitionTileMark({
  season, isChampionship, sizeClass,
}: Pick<CompetitionTileData, 'season' | 'isChampionship'> & { sizeClass: string }) {
  return (
    <span className={['comp-tile', 'comp-tile--bare', sizeClass].filter(Boolean).join(' ')} aria-hidden="true">
      {season && <UI_SeasonIcon season={season} className="comp-tile__mark" />}
      {isChampionship && <span className="comp-tile__mark-cup">🏆</span>}
    </span>
  );
}

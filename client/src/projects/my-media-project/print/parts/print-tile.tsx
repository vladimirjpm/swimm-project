import React from 'react';
import { hasTileContent, type CompetitionTileData } from '../../../../utils/helpers/competition-source';
import CompetitionTile from '../../../results-main-project/components/competition-header/competition-tile';

type TileSize = NonNullable<React.ComponentProps<typeof CompetitionTile>['size']>;

/**
 * Плитка соревнования на картинке (обложка, плакат). Почти пустую плитку — без буквы категории
 * и без возрастной группы — не рисуем вовсе (`hasTileContent`, решение Влада 13.11.2026);
 * что занимает её место, решает раскладка.
 */
export default function PrintTile({ tile, size }: { tile: CompetitionTileData; size: TileSize }) {
  return hasTileContent(tile) ? <CompetitionTile {...tile} size={size} /> : null;
}

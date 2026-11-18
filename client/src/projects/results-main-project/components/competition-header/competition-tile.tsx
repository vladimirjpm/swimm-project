import React, { useState } from 'react';
import { competitionTileKind, type CompetitionTileData } from '../../../../utils/helpers/competition-source';
import CompetitionTileEmblem from './competition-tile-emblem';
import CompetitionTileMark from './competition-tile-mark';
import CompetitionTilePlain from './competition-tile-plain';
import { TILE_SIZE_CLASS, type CompetitionTileSize } from './competition-tile-size';
import './competition-tile.css';

// Плитка соревнования — ПЕРЕКЛЮЧАТЕЛЬ (решение Влада 18.11.2026): одна точка входа на всех экранах
// (шапка соревнования, липкая полоса, карточка My media, обложки и плакаты), а что рисовать, решает
// правило `competitionTileKind` (`utils/helpers/competition-tile-content.ts`, с юнит-тестом):
// - значок серии турнира есть → `CompetitionTileEmblem` (значок + уменьшенная полоса сверху);
// - есть буква категории или возрастная группа → `CompetitionTilePlain` (обычная плитка, 11c);
// - есть только сезон / кубок → `CompetitionTileMark` (значок сезона в габарите плитки);
// - ничего → ничего.
// Вызывающим проверять ничего не нужно: им достаточно передать данные (`competitionTileData`).

interface Props extends CompetitionTileData {
  /** 'responsive' — полный габарит, ниже 640px сам ужимается до xs (шапка соревнования).
   *  'mini' — 34×42 для стики-компакт-бара мобайла (16b).
   *  'cover' (86×104) и 'cover-lg' (110×134) — обложка YouTube в My media;
   *  'poster' (132×160) и 'poster-lg' (150×182) — плакат там же. */
  size?: CompetitionTileSize;
}

export default function CompetitionTile({ size = 'responsive', ...data }: Props) {
  // Картинка значка не загрузилась (удалили, сеть) — дальше этот адрес как будто не задан.
  const [brokenUrl, setBrokenUrl] = useState<string | null>(null);
  const tile = data.emblemUrl && data.emblemUrl === brokenUrl ? { ...data, emblemUrl: null } : data;
  const sizeClass = TILE_SIZE_CLASS[size];

  switch (competitionTileKind(tile)) {
    case 'emblem':
      return <CompetitionTileEmblem {...tile} sizeClass={sizeClass} onError={() => setBrokenUrl(tile.emblemUrl ?? null)} />;
    case 'tile':
      return <CompetitionTilePlain {...tile} sizeClass={sizeClass} />;
    case 'mark':
      return <CompetitionTileMark season={tile.season} isChampionship={tile.isChampionship} sizeClass={sizeClass} />;
    default:
      return null;
  }
}

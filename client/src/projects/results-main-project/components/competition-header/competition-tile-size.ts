// Ступени размера плитки соревнования — общие для всех её вариантов (`CompetitionTile`):
// обычной плитки, значка сезона и значка серии. Габарит у всех один — раскладки вокруг не прыгают.

export type CompetitionTileSize =
  'responsive' | 'md' | 'sm' | 'xs' | 'mini' | 'cover' | 'cover-lg' | 'poster' | 'poster-lg';

export const TILE_SIZE_CLASS: Record<CompetitionTileSize, string> = {
  responsive: 'comp-tile--responsive',
  md: '',
  sm: 'comp-tile--sm',
  xs: 'comp-tile--xs',
  mini: 'comp-tile--mini',
  cover: 'comp-tile--cover',
  'cover-lg': 'comp-tile--cover-lg',
  poster: 'comp-tile--poster',
  'poster-lg': 'comp-tile--poster-lg',
};

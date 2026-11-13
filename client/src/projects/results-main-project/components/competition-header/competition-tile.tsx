import React from 'react';
import { hasTileContent, type CompetitionTileData } from '../../../../utils/helpers/competition-source';
import { IconSnowflake, IconSun, IconTrophy } from '../../../competitions-project/calendar/calendar-icons';
import './competition-tile.css';

// Плитка соревнования (design_handoff_competition_overview3, вариант 11c):
// полоса значков (сезон + кубок) · буква группы (K/M) · лента возрастной группы.
// Сезон в полосе — рисунок (снежинка / солнце из `calendar-icons`), кубок — эмодзи.
// Каждый слот скрывается отдельно, если данных нет; без возраста — модификатор --no-age.
//
// Несодержательная плитка — без буквы категории и без возрастной группы (`hasTileContent`) —
// рамкой не рисуется: на её месте, в том же габарите, стоит значок сезона (решение Влада
// 13.11.2026: «вместо него поставь иконку зимы или лета»). Так бывает у соревнования без
// канонической категории. Значки — рисунки календаря (`calendar-icons`), не эмодзи.

interface Props extends CompetitionTileData {
  /** 'responsive' — полный габарит, ниже 640px сам ужимается до xs (шапка соревнования).
   *  'mini' — 34×42 для стики-компакт-бара мобайла (16b).
   *  'cover' (86×104) и 'cover-lg' (110×134) — обложка YouTube в My media;
   *  'poster' (132×160) и 'poster-lg' (150×182) — плакат там же. */
  size?: 'responsive' | 'md' | 'sm' | 'xs' | 'mini' | 'cover' | 'cover-lg' | 'poster' | 'poster-lg';
}

const SIZE_CLASS: Record<NonNullable<Props['size']>, string> = {
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

/** Рисунок сезона — значки календаря, не эмодзи: у каждой системы эмодзи своё («сделай нашу
 *  снежинку», Влад 13.11.2026). Размер и цвет задаёт CSS плитки. */
function SeasonGlyph({ season, className }: { season: 'winter' | 'summer'; className?: string }) {
  const Icon = season === 'winter' ? IconSnowflake : IconSun;
  return <Icon className={className} />;
}

/**
 * Значок сезона на месте несодержательной плитки, в её габарите — раскладки вокруг не прыгают.
 * У чемпионата под ним кубок (в плитке он стоял бы в полосе значков — пропасть он не должен);
 * сезон неизвестен — только кубок; нет ни сезона, ни чемпионата — ничего.
 */
function SeasonMark({ season, isChampionship, sizeClass }: {
  season: CompetitionTileData['season'];
  isChampionship: boolean;
  sizeClass: string;
}) {
  if (!season && !isChampionship) return null;
  const tone = season ? `comp-tile--${season}` : '';
  return (
    <span className={['comp-tile', 'comp-tile--bare', sizeClass, tone].filter(Boolean).join(' ')} aria-hidden="true">
      {season && <SeasonGlyph season={season} className="comp-tile__mark" />}
      {isChampionship && <IconTrophy className="comp-tile__mark-cup" />}
    </span>
  );
}

export default function CompetitionTile({
  letter, ageGroup, season, isChampionship, size = 'responsive',
}: Props) {
  const classes = [
    'comp-tile',
    SIZE_CLASS[size],
    ageGroup ? '' : 'comp-tile--no-age',
  ].filter(Boolean).join(' ');

  if (!hasTileContent({ letter, ageGroup })) {
    return <SeasonMark season={season} isChampionship={isChampionship} sizeClass={SIZE_CLASS[size]} />;
  }

  return (
    <span className={classes} aria-hidden="true">
      {(season || isChampionship) && (
        <span className="comp-tile__icons">
          {season && (
            <span className={`comp-tile__season comp-tile__season--${season}`}>
              <SeasonGlyph season={season} />
            </span>
          )}
          {isChampionship && <span className="comp-tile__cup">🏆</span>}
        </span>
      )}
      <span className="comp-tile__letter">{letter}</span>
      {ageGroup && <span className="comp-tile__age">{ageGroup}</span>}
    </span>
  );
}

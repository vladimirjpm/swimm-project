import React from 'react';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import { clubLabel, strokeLabel, type SbFilters } from '../sb-filters-model';
import type { SeasonBestClubOption } from '../../../hooks/useSeasonBestList';

/**
 * Шапка фильтров — полоса выбранных значений над списком.
 *
 * Прямой аналог `ResultsFilteredInfo` со страницы results (хендофф
 * `design_handoff_position_filter`), и правила оттуда сохранены:
 *  • колонка в состоянии «All» не занимает полноценное место — она приглушена и мельче;
 *  • полоса в ДВЕ строки: сверху мелко всё, что «All», снизу крупно выбранное;
 *  • пустая строка не рендерится вообще.
 *
 * ⚠ Отличие от results: там две строки только на мобайле, а на десктопе одна полоса равных
 * колонок. Здесь двухстрочный вид ВЕЗДЕ (решение Влада 2026-08-26) — фильтров много, и
 * равные колонки размазывали выбранное между пустыми «All».
 *
 * Чипы некликабельны: выбор живёт в сайдбаре, полоса только отвечает на вопрос
 * «что я сейчас смотрю».
 */

interface Chip {
  key: string;
  label: string;
  active: boolean;
  value: React.ReactNode;
  /** Не печатать подпись над значением — она нужна не всем чипам (см. Event). */
  hideLabel?: boolean;
}

interface Props {
  filters: SbFilters;
  seasonLabel: string | null;
  clubs: SeasonBestClubOption[];
  /** Показывать ли чип клуба (модуль `filterClub`). */
  showClub: boolean;
  /** Латинские названия клубов вместо протокольных (модуль `latinNames`). */
  latinNames: boolean;
}

function SbFilterBar({ filters, seasonLabel, clubs, showClub, latinNames }: Props) {
  const club = clubs.find((c) => c.club_id === filters.clubId);

  const chips: Chip[] = [
    {
      key: 'event',
      label: 'Event',
      active: !!filters.stroke,
      // Только картинка: она сама несёт и стиль, и дистанцию (`icon-len` печатает её в
      // углу), а название стиля рядом было лишним словом — решение Влада 2026-08-26.
      // Слово «EVENT» над ней тоже снято: место отдано самой картинке. Полное имя
      // дисциплины остаётся в заголовке страницы и в aria-label.
      hideLabel: true,
      value: filters.stroke ? (
        <span className="sb-chip__event" aria-label={`${filters.distance ?? ''} ${strokeLabel(filters.stroke)}`}>
          <UI_SwimmStyleIcon
            styleName={filters.stroke}
            styleLen={filters.distance ?? ''}
            styleType="icon-len"
            className="sb-chip__event-icon"
          />
        </span>
      ) : 'All',
    },
    {
      key: 'pool',
      label: 'Pool',
      active: filters.poolType != null,
      // Бассейн — общим `UI_PoolIcon` (тот же, что в плашке события на странице пловца).
      // «Both» — не то же самое, что «All» у остальных фильтров: времена 25м и 50м
      // несравнимы, поэтому смешанный список надо назвать вслух, а не показать иконкой.
      value: filters.poolType
        ? (
          <UI_PoolIcon
            styleType="icon-text-center"
            iconWidth="46"
            label={filters.poolType}
            labelClassName="sb-chip__pool-label"
          />
        )
        : 'Both 25m + 50m',
    },
    {
      key: 'gender',
      label: 'Gender',
      active: filters.gender != null,
      value: filters.gender === 'male' ? '♂ Male' : filters.gender === 'female' ? '♀ Female' : 'All',
    },
    {
      key: 'age',
      // Подпись меняется вместе с осью: «Masters» — это уже не возраст в сезоне, а группа.
      label: filters.ageGroup ? 'Masters' : 'Age',
      active: filters.age != null || filters.ageGroup != null,
      value: filters.ageGroup
        ? filters.ageGroup
        : filters.age != null
          ? (filters.ageTo != null ? `${filters.age}+` : String(filters.age))
          : 'All',
    },
    ...(showClub ? [{
      key: 'club',
      label: 'Club',
      active: filters.clubId != null,
      value: club ? <span dir="auto">{clubLabel(club.name, club.name_en, latinNames)}</span> : 'All',
    }] : []),
    {
      key: 'season',
      label: 'Season',
      active: filters.season != null,
      value: seasonLabel ?? 'All seasons',
    },
  ];

  const idle = chips.filter((c) => !c.active);
  const active = chips.filter((c) => c.active);

  return (
    <div className="sb-bar">
      {/* Верхняя строка — мелкие «All»; пустой не бывает, её просто нет. */}
      {idle.length > 0 && (
        <div className="sb-bar__row sb-bar__row--idle">
          {idle.map((c) => (
            <div key={c.key} className={`sb-bar__chip sb-bar__chip--idle sb-bar__chip--${c.key}`}>
              <span className="sb-bar__label">{c.label}</span>
              <span className="sb-bar__value">{c.value}</span>
            </div>
          ))}
        </div>
      )}
      {/* Нижняя строка — выбранное, крупно и акцентом. */}
      {active.length > 0 && (
        <div className="sb-bar__row sb-bar__row--active">
          {active.map((c) => (
            <div key={c.key} className={`sb-bar__chip sb-bar__chip--active sb-bar__chip--${c.key}`}>
              {!c.hideLabel && <span className="sb-bar__label">{c.label}</span>}
              <span className="sb-bar__value">{c.value}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default SbFilterBar;

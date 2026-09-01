import React from 'react';
import {
  FilterHostProvider,
  type FilterHost,
} from '../../components/filter-section/filter-host';
import FilterCard from '../../components/filter-section/filter-card';
import FilterSwimmingStyle from '../../components/filter-section/filter-swimming-style';
import FilterPoolType from '../../components/filter-section/filter-pool-type';
import FilterGender from '../../components/filter-section/filter-gender';
import FilterAge from '../../components/filter-section/filter-age';
import FilterClub from '../../components/filter-section/filter-club';
import type { SbFilters } from '../sb-filters-model';
import type { SeasonBestModules } from '../season-best-modules';

/**
 * Сайдбар фильтров страницы `/season-best` — **та же панель, что на results** (Ф4 плана
 * `docs/plans/filters-reusable-panel-plan.md`). Своих фильтров у страницы больше нет: она
 * даёт панели свой хост (`useSeasonBestFilterHost`), а он переводит общую модель в срез,
 * который живёт в адресе.
 *
 * Панель — это просто список карточек: выключить фильтр = убрать строку JSX. Отсюда и
 * состав: Date и Event date не нужны (список охватывает весь сезон, дату выбирает карусель),
 * Name — искать человека нужно на его странице, Position — место в протоколе своего заплыва
 * не имеет отношения к месту в сезонном рейтинге, Best level — другая система координат
 * (решения Влада 2026-08-26).
 *
 * Палитра deep приезжает переопределением токенов `--fc-*`/`--fseg-*` на `.sb-side`
 * (`season-best-page.css`), а не правкой компонентов.
 *
 * Своя карточка здесь ровно одна — «Rows»: это не фильтр общей модели, а способ показа
 * (схлопывать ли повторные заплывы одного пловца).
 */
interface Props {
  host: FilterHost;
  filters: SbFilters;
  onChange: (patch: Partial<SbFilters>) => void;
  modules: SeasonBestModules;
}

function SbFilterPanel({ host, filters, onChange, modules }: Props) {
  const anyActive =
    filters.poolType != null ||
    filters.gender != null ||
    filters.age != null ||
    // Мастерская группа — тоже сужение среза, причём самое сильное (другая выборка целиком).
    filters.ageGroup != null ||
    filters.clubId != null ||
    filters.bestPerSwimmer;

  return (
    <FilterHostProvider host={host}>
      <div className="sb-filters">
        <div className="sb-filters__head">
          <span className="sb-filters__title">Filters</span>
          {anyActive && (
            <button type="button" className="sb-filters__reset" onClick={host.reset}>
              Reset
            </button>
          )}
        </div>

        <FilterSwimmingStyle />
        <FilterPoolType />
        <FilterGender />
        <FilterAge />
        {modules.filterClub && <FilterClub />}

        {modules.bestPerSwimmerToggle && (
          <FilterCard
            title="Rows"
            summary={filters.bestPerSwimmer ? 'Best per swimmer' : 'All swims'}
            isActive={filters.bestPerSwimmer}
          >
            <div className="flex flex-wrap gap-2">
              {/* Умолчание — все заплывы: один пловец законно занимает и первое место, и
                  третье, и витрина обязана это показывать, а не прятать. */}
              <button
                type="button"
                className={`fseg ${!filters.bestPerSwimmer ? 'fseg-active' : ''}`}
                title="Every swim of the season, so one swimmer can hold several places"
                onClick={() => onChange({ bestPerSwimmer: false })}
              >
                All swims
              </button>
              <button
                type="button"
                className={`fseg ${filters.bestPerSwimmer ? 'fseg-active' : ''}`}
                title="One best swim per swimmer"
                onClick={() => onChange({ bestPerSwimmer: true })}
              >
                Best per swimmer
              </button>
            </div>
          </FilterCard>
        )}
      </div>
    </FilterHostProvider>
  );
}

export default SbFilterPanel;

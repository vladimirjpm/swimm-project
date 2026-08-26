import React from 'react';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';
import { useFilterHost } from './filter-host';
import FilterCard from './filter-card';
import FilterDistance from './filter-distance';

/**
 * Стиль плавания. Значения, список стилей и доступность — через хост (Ф3): сам компонент
 * не знает ни про стор, ни про то, что в paged-режиме доступность приходит из filter-hints.
 *
 * Дистанции выбранного стиля рисует `FilterDistance` — колонкой справа, внутри этой же
 * карточки: на узком сайдбаре они переносятся вниз.
 */
const FilterSwimmingStyle: React.FC = () => {
  const { values, set, options, isAvailable } = useFilterHost();
  const styleName = values.style_name ?? '';
  const styleLen = values.style_len;

  if (options.styles.length === 0) return null;

  const styleLabel =
    options.styles.find((s) => s.style_name === styleName)?.label ?? styleName;
  const summary = styleName
    ? `${styleLabel}${styleLen ? ` · ${styleLen}m` : ''}`
    : 'All';

  return (
    <FilterCard
      title="Swimming Style"
      summary={summary}
      isActive={!!styleName}
      defaultOpen
    >
      {/* Колонки стиль | дистанция; на узком сайдбаре дистанции переносятся вниз */}
      <div className="flex flex-wrap gap-3">
        <div className="flex flex-col gap-2">
          <button
            className={`fseg flex items-center justify-center ${
              !styleName ? 'fseg-active' : ''
            }`}
            onClick={() => set({ style_name: '', style_len: 0 })}
          >
            All
          </button>
          {options.styles.map((style) => {
            const disabled = isAvailable
              ? !isAvailable('style', style.style_name)
              : false;
            return (
              <button
                key={style.style_name}
                disabled={disabled}
                className={`fseg flex items-center justify-between ${
                  styleName === style.style_name ? 'fseg-active' : ''
                }`}
                onClick={() => !disabled && set({ style_name: style.style_name })}
              >
                <UI_SwimmStyleIcon
                  className="w-20"
                  styleName={style.style_name}
                />
              </button>
            );
          })}
        </div>
        {/* Дистанции выбранного стиля — колонка справа от стилей */}
        <FilterDistance />
      </div>
    </FilterCard>
  );
};

export default FilterSwimmingStyle;

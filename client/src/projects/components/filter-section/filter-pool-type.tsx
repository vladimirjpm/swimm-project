import React from 'react';
import Helper from '../../../utils/helpers/data-helper';
import { useFilterHost } from './filter-host';
import FilterCard from './filter-card';

/** Тип бассейна. Значения и опции — через хост (Ф2).
 *
 *  Сравнение идёт через `Helper.resolvePoolType`, а не строкой: одно и то же значение
 *  приходит написанным по-разному ('25', '25m', 'SCM'). */
const FilterPoolType: React.FC = () => {
  const { values, set, options } = useFilterHost();
  const poolType = values.pool_type ?? 'all';

  if (options.poolTypes.length === 0) return null;

  const isActive = poolType !== 'all';

  return (
    <FilterCard
      title="Pool Type"
      summary={isActive ? poolType : 'All'}
      isActive={isActive}
    >
      <div className="flex flex-wrap gap-2">
        {options.poolTypes.map((type) => (
          <button
            key={type}
            className={`fseg ${
              (type === 'all' && poolType === 'all') ||
              (type !== 'all' &&
                poolType !== 'all' &&
                Helper.resolvePoolType(poolType) === Helper.resolvePoolType(type))
                ? 'fseg-active'
                : ''
            }`}
            onClick={() => set({ pool_type: type })}
          >
            {type}
          </button>
        ))}
      </div>
    </FilterCard>
  );
};

export default FilterPoolType;

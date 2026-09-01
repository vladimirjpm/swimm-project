import React from 'react';
import { useFilterHost } from './filter-host';
import FilterCard from './filter-card';

const genderLabel = (gen: string) =>
  gen === 'male' ? 'M' : gen === 'female' ? 'W' : 'All';

/** Пол. Значения и опции — через хост (Ф2), поэтому карточка одинаково работает
 *  и на results (Redux), и на странице с состоянием в адресе. */
const FilterGender: React.FC = () => {
  const { values, set, options } = useFilterHost();
  const gender = values.gender ?? 'all';

  // Опций нет — карточку не рисуем (на results это значит, что не загрузился filter_data).
  if (options.genders.length === 0) return null;

  return (
    <FilterCard
      title="Gender"
      summary={genderLabel(gender)}
      isActive={gender !== 'all'}
    >
      <div className="flex flex-wrap gap-2">
        {options.genders.map((gen) => (
          <button
            key={gen}
            className={`fseg ${gender === gen ? 'fseg-active' : ''}`}
            onClick={() => set({ gender: gen })}
          >
            {genderLabel(gen)}
          </button>
        ))}
      </div>
    </FilterCard>
  );
};

export default FilterGender;

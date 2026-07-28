import React, { useMemo } from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { useFilteredByTypeResults } from './use-filtered-results';
import FilterCard from './filter-card';

/**
 * Подписи категорий. Возрастные («17», «25-29») показываем как есть — они и так читаемы.
 */
const LABELS: Record<string, string> = {
  open: 'Open',
  para: 'Para',
  mix: 'Mix',
};

function labelOf(category: string): string {
  const v = category.toLowerCase();
  if (LABELS[v]) return LABELS[v];
  if (v.startsWith('mix-')) return `Mix ${category.slice(4)}`;
  // Возрастная категория заплыва: «17» значит «до 17», как U17 в протоколе.
  return /^\d+$/.test(v) ? `U${category}` : category;
}

/**
 * Сортировка: сперва основная программа, затем возрастные (по числу), затем para и mix.
 * Порядок фиксированный, чтобы кнопки не прыгали между соревнованиями.
 */
function sortKey(category: string): [number, number, string] {
  const v = category.toLowerCase();
  if (v === 'open') return [0, 0, v];
  if (/^\d/.test(v)) return [1, parseInt(v, 10), v];
  if (v === 'para') return [2, 0, v];
  return [3, 0, v];
}

/**
 * Фильтр по категории (программе) заплыва.
 *
 * Зачем: в одной дисциплине протокола бывает несколько первых мест, потому что это разные
 * программы одного соревнования — у Маккабиады 50 вольным у мужчин разыгрывалось трижды:
 * «Men», «U17 Boys» и «Men Para». Без фильтра три золота в таблице выглядят ошибкой данных.
 *
 * Показывается только когда категорий действительно больше одной: у обычного возрастного
 * чемпионата она одна на всё соревнование, и кнопка была бы шумом.
 */
const FilterEventCategory: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filteredByTypeResults = useFilteredByTypeResults();

  const current = filters.event_category || 'all';

  const categories = useMemo(() => {
    const set = new Set<string>();
    filteredByTypeResults.forEach((r) => {
      if (r.event_category) set.add(r.event_category);
    });
    return Array.from(set).sort((a, b) => {
      const ka = sortKey(a);
      const kb = sortKey(b);
      return ka[0] - kb[0] || ka[1] - kb[1] || ka[2].localeCompare(kb[2]);
    });
  }, [filteredByTypeResults]);

  const updateFilter = (value: string) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, event_category: value },
      }),
    );
  };

  // Одна категория (или ни одной — данные импортированы до появления поля) — фильтр не нужен.
  if (categories.length <= 1) return null;

  return (
    <FilterCard
      title="Programme"
      summary={current === 'all' ? 'All' : labelOf(current)}
      isActive={current !== 'all'}
    >
      <div className="flex flex-wrap gap-2">
        <button
          className={`fseg ${current === 'all' ? 'fseg-active' : ''}`}
          onClick={() => updateFilter('all')}
        >
          All
        </button>

        {categories.map((category) => (
          <button
            key={category}
            className={`fseg ${current === category ? 'fseg-active' : ''}`}
            onClick={() => updateFilter(category)}
          >
            {labelOf(category)}
          </button>
        ))}
      </div>
    </FilterCard>
  );
};

export default FilterEventCategory;

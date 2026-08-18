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
/**
 * «Программная часть» категории — то, что останется, если отбросить возрастную ось:
 * «9», «17», «13-99» → 'age' (покрыто фильтром Age); «mix-11-12» и «mix-13-14» → 'mix'
 * (возрастные полосы ОДНОЙ программы смешанных эстафет); «open»/«para»/«mix» — как есть.
 * Фильтр Programme оправдан, только когда дисциплину делят РАЗНЫЕ программные части.
 */
function programmeOf(category: string): string {
  const v = category.trim().toLowerCase();
  if (/^\d+(-\d+)?$/.test(v)) return 'age';
  if (v.startsWith('mix-')) return 'mix';
  return v;
}

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
 * Показывается только там, где программы РЕАЛЬНО делят дисциплину, — и только если среди
 * категорий есть хотя бы одна НЕ-возрастная (Open/Para/Mix):
 *   • «делят дисциплину» = в одной дисциплине (стиль × дистанция × пол × день × сессия)
 *     встречается две и более категории — «несколько первых мест на одной дистанции».
 *     Иначе фильтр дублирует Gender (бугрим: женская программа «13-99», мужская «14-99»).
 *   • «не-возрастная» — и именно В ЭТОМ делении: чисто возрастные категории («9»…«14»
 *     молодёжного чемпионата) полностью покрыты фильтром Age — вторая колонка кнопок
 *     U9…U14 была бы шумом; а mix-эстафеты дисциплину не делят вовсе (их пол `none` —
 *     отдельная дисциплина), поэтому само их наличие фильтр тоже не оправдывает.
 */
const FilterEventCategory: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const filteredByTypeResults = useFilteredByTypeResults();

  const current = filters.event_category || 'all';

  const { categories, splitsDiscipline } = useMemo(() => {
    const set = new Set<string>();
    // Дисциплина → её категории: фильтр осмыслен, только если где-то их ≥2.
    const byDiscipline = new Map<string, Set<string>>();
    filteredByTypeResults.forEach((r) => {
      if (!r.event_category) return;
      set.add(r.event_category);
      // heat_type в ключе: прелимы и финал — разные сессии, и их категории могут быть
      // напечатаны с разной возрастной планкой (бугрим 25/05: прелимы «13-99», финал
      // «14-99») — это НЕ параллельные программы.
      const key = `${r.event_style_name}|${r.event_style_len}|${r.event_style_gender}|${r.date}|${r.heat_type ?? ''}`;
      const cats = byDiscipline.get(key) ?? new Set<string>();
      cats.add(programmeOf(r.event_category));
      byDiscipline.set(key, cats);
    });
    return {
      categories: Array.from(set).sort((a, b) => {
        const ka = sortKey(a);
        const kb = sortKey(b);
        return ka[0] - kb[0] || ka[1] - kb[1] || ka[2].localeCompare(kb[2]);
      }),
      // ≥2 РАЗНЫХ программных частей в одной дисциплине: «11 против 12» и
      // «mix-11-12 против mix-13-14» — ось возраста, «open против para/U17» — программы.
      splitsDiscipline: [...byDiscipline.values()].some((cats) => cats.size > 1),
    };
  }, [filteredByTypeResults]);

  const updateFilter = (value: string) => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, event_category: value },
      }),
    );
  };

  // Одна категория (или ни одной — данные импортированы до появления поля) — фильтр не
  // нужен; как и когда программы не делят ни одну дисциплину (бугрим: 13-99=Ж, 14-99=М;
  // молодёжный чемпионат: деления только возрастные + mix-эстафеты со своим полом).
  if (categories.length <= 1 || !splitsDiscipline) return null;

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

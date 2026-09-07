import { useCallback, useMemo, useRef } from 'react';
import type {
  FilterHost,
  FilterOptions,
} from '../components/filter-section/filter-host';
import type { MySwimDto } from './use-my-swims';

/**
 * Хост панели фильтров для `/my-media` (Ф3 плана `docs/plans/my-media-filters-plan.md`).
 *
 * Общая панель говорит на языке `FilterSelected` (плоские строки: `style_name`,
 * `style_len`…), а страница держит срез своими `useState`. Перевод между ними — здесь и
 * только здесь, ровно как у `/season-best` (`sb-filter-host.ts`).
 *
 * Через хост идут ТОЛЬКО стиль и дистанция — единственные фильтры кабинета, для которых в
 * общей модели есть поля. Остальные семь (видео, соревнование, даты, группы, статус) —
 * свои карточки на `FilterCard`: заводить под них поля в общей модели незачем, они есть
 * только здесь. Тот же приём, что у карточки «Rows» на `/season-best`.
 *
 * `isAvailable` не задан намеренно: опции считаются по РЕАЛЬНЫМ заплывам выборки, поэтому
 * недостижимых комбинаций в панели просто нет — гасить нечего.
 */
export interface MyMediaHostState {
  /** 'all' — фильтр выключен. */
  style: string | 'all';
  distance: string | 'all';
}

export function useMyMediaFilterHost(params: {
  swims: MySwimDto[];
  state: MyMediaHostState;
  onChange: (patch: Partial<MyMediaHostState>) => void;
  /** Сброс всех фильтров страницы — не только стиля с дистанцией. */
  onReset: () => void;
}): FilterHost {
  const { swims, state, onChange, onReset } = params;

  // Пишем поверх самых свежих значений, а не захваченных рендером (как в остальных хостах).
  const stateRef = useRef(state);
  stateRef.current = state;

  const values = useMemo(
    () => ({
      style_name: state.style === 'all' ? '' : state.style,
      // Дистанция — СТРОКА: у эстафет она «4X50», и общая модель это допускает.
      style_len: state.distance === 'all' ? '' : state.distance,
    }),
    [state],
  );

  const set = useCallback<FilterHost['set']>(
    (patch) => {
      const next: Partial<MyMediaHostState> = {};

      if ('style_name' in patch) {
        next.style = patch.style_name ? String(patch.style_name) : 'all';
        // Смена стиля сбрасывает дистанцию: 800 брассом в протоколах не бывает, и
        // «100» от вольного, оставшись под баттерфляем, обнулила бы список.
        next.distance = 'all';
      }
      if ('style_len' in patch) {
        const len = patch.style_len;
        next.distance = len === '' || len === 0 || len == null ? 'all' : String(len);
      }

      onChange(next);
    },
    [onChange],
  );

  /**
   * Стили с их дистанциями. Карта строится по выборке: панель обязана показывать то, что
   * пловец реально плавал, а не полный справочник дисциплин.
   */
  const options = useMemo<FilterOptions>(() => {
    const byStyle = new Map<string, Set<string>>();
    swims.forEach((s) => {
      if (!s.style) return;
      const lens = byStyle.get(s.style) ?? new Set<string>();
      if (s.distance) lens.add(String(s.distance));
      byStyle.set(s.style, lens);
    });

    return {
      styles: Array.from(byStyle.entries())
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([style_name, lens]) => ({
          style_name,
          // Числовая сортировка с падением на строковую: «4X50» числом не выражается.
          style_len: Array.from(lens).sort(
            (a, b) => (Number(a) || 0) - (Number(b) || 0) || a.localeCompare(b),
          ),
        })),
      // Пол и бассейн в кабинете не фильтруются — карточек нет, но поля контракта обязаны быть.
      genders: [],
      poolTypes: [],
    };
  }, [swims]);

  return useMemo(
    () => ({ values, set, options, reset: onReset }),
    [values, set, options, onReset],
  );
}

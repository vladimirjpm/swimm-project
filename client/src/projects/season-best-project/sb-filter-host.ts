import { useCallback, useMemo, useRef } from 'react';
import type { FilterHost, FilterOptions } from '../components/filter-section/filter-host';
import type {
  SeasonBestClubOption,
  SeasonBestEventOption,
} from '../../hooks/useSeasonBestList';
import {
  SB_ADULT_FROM,
  SB_ADULT_TO,
  SB_AGES,
  SB_AGE_ADULT,
  clubLabel,
  strokeLabel,
  type SbFilters,
} from './sb-filters-model';
import type { SeasonBestModules } from './season-best-modules';

/**
 * `QueryFilterHost` страницы `/season-best` (Ф4 плана `docs/plans/filters-reusable-panel-plan.md`).
 *
 * Общая панель фильтров говорит на языке `FilterSelected` (плоские строки: `style_name`,
 * `pool_type`, `club`…), а эта страница держит срез в адресе своей моделью `SbFilters`
 * (`stroke`, `poolType`, `clubId`…). Перевод между ними — здесь и только здесь: в этом и
 * состоял смысл шва, компоненты фильтров про разницу не знают.
 *
 * Чего хост НЕ даёт:
 * - `isAvailable` — на этой странице ничего не гасится: список строит сервер, и «пустых»
 *   комбинаций он просто не отдаёт;
 * - сезон и тумблер «все заплывы / лучший на пловца» — это не фильтры общей модели, их
 *   рисует сама страница (карусель сезонов и своя карточка Rows).
 */
export function useSeasonBestFilterHost(params: {
  filters: SbFilters;
  onChange: (patch: Partial<SbFilters>) => void;
  /** Стили с реально проплытыми дистанциями — `GET /api/season-best/options`. */
  events: SeasonBestEventOption[];
  /** Клубы среза — из ответа списка, считаются ДО фильтра по клубу. */
  clubs: SeasonBestClubOption[];
  /** Возрастные группы мастерских протоколов — `GET /api/season-best/options`. */
  ageGroups: string[];
  modules: SeasonBestModules;
}): FilterHost {
  const { filters, onChange, events, clubs, ageGroups, modules } = params;

  // Как и в Redux-хосте: пишем поверх самых свежих значений, а не захваченных рендером.
  // Группы здесь ОБЯЗАТЕЛЬНЫ: они приезжают асинхронно из `/options`, и `set`, замкнувшийся
  // на пустой список первого рендера, принимал бы «45-49» за год и писал `age: NaN`.
  const stateRef = useRef({ filters, events, ageGroups });
  stateRef.current = { filters, events, ageGroups };

  const values = useMemo(
    () => ({
      style_name: filters.stroke ?? '',
      // Дистанция строкой — общая модель это допускает («4X50» у эстафет).
      style_len: filters.distance ?? '',
      pool_type: filters.poolType ?? 'all',
      gender: filters.gender ?? 'all',
      // Возраст в общей модели — одна строка, поэтому три состояния витрины кодируются ею:
      // конкретный год, хвост «21+» и мастерская группа «25-29».
      age: filters.ageGroup
        ? filters.ageGroup
        : filters.ageTo != null
          ? SB_AGE_ADULT
          : filters.age != null
            ? String(filters.age)
            : 'all',
      club: filters.clubId != null ? String(filters.clubId) : 'all',
    }),
    [filters],
  );

  const set = useCallback<FilterHost['set']>(
    (patch) => {
      const { filters: cur, events: curEvents, ageGroups: curGroups } = stateRef.current;
      const next: Partial<SbFilters> = {};

      if ('style_name' in patch) {
        const stroke = patch.style_name ? String(patch.style_name) : null;
        next.stroke = stroke;
        // Смена стиля сбрасывает дистанцию, которой у нового стиля нет: 800 брассом в наших
        // протоколах не плавают, и молча оставленная дистанция дала бы пустой список.
        const distances = curEvents.find((e) => e.style === stroke)?.distances ?? [];
        next.distance =
          stroke && cur.distance && distances.includes(cur.distance) ? cur.distance : null;
      }
      if ('style_len' in patch) {
        next.distance = patch.style_len ? String(patch.style_len) : null;
      }
      if ('pool_type' in patch) {
        next.poolType = patch.pool_type === 'all' ? null : (patch.pool_type ?? null);
      }
      if ('gender' in patch) {
        next.gender =
          patch.gender === 'male' || patch.gender === 'female' ? patch.gender : null;
      }
      if ('age' in patch) {
        const value = patch.age;
        if (!value || value === 'all') {
          // «All» выходит и из мастерского режима: он держится ровно выбранной группой.
          next.age = null;
          next.ageTo = null;
          next.ageGroup = null;
        } else if (value === SB_AGE_ADULT) {
          next.age = SB_ADULT_FROM;
          next.ageTo = SB_ADULT_TO;
          next.ageGroup = null;
        } else if (curGroups.includes(String(value))) {
          // Выбор группы переключает выборку на мастерские старты целиком.
          next.age = null;
          next.ageTo = null;
          next.ageGroup = String(value);
        } else {
          next.age = Number(value);
          next.ageTo = null;
          next.ageGroup = null;
        }
      }
      if ('club' in patch) {
        next.clubId = patch.club && patch.club !== 'all' ? Number(patch.club) : null;
      }

      if (Object.keys(next).length > 0) onChange(next);
    },
    [onChange],
  );

  /**
   * Сброс не трогает сезон и дисциплину: без них списка нет вовсе, и «сбросить» до пустого
   * экрана — не то, чего ждут от кнопки. Гасятся именно сужения среза.
   */
  const reset = useCallback(() => {
    onChange({
      poolType: null,
      gender: null,
      age: null,
      ageTo: null,
      ageGroup: null,
      clubId: null,
      bestPerSwimmer: false,
    });
  }, [onChange]);

  const options = useMemo<FilterOptions>(
    () => ({
      styles: events.map((e) => ({
        style_name: e.style,
        label: strokeLabel(e.style),
        style_len: e.distances,
      })),
      genders: ['all', 'male', 'female'],
      // Бассейн подписан как в данных этой страницы: '25m'/'50m', а не '25'/'50' с results.
      poolTypes: ['all', '25m', '50m'],
      ages: {
        mode: 'age',
        // Хвост «21+» — взрослые в обычных стартах; до него они были видны только под «All».
        values: ['all', ...SB_AGES.map(String), SB_AGE_ADULT],
        // Подпись «сколько лет на стартах» здесь не нужна: возраст и так сезонный.
        compYears: [],
        // Вторая шкала — мастерские группы. Выбор группы меняет и выборку (мастерские
        // старты вместо обычных), и смысл места: оно считается внутри пятилетки.
        extra: ageGroups.length > 0
          ? { title: 'Masters age groups — separate meets, places counted inside the group', values: ageGroups }
          : undefined,
      },
      clubs: modules.filterClub
        ? {
            items: clubs.map((c) => ({
              value: String(c.club_id),
              label: clubLabel(c.name, c.name_en, modules.latinNames),
              // Число заплывов клуба в срезе — единственная метрика, осмысленная здесь:
              // очки и медали со страницы results считаются по соревнованию.
              note: String(c.swims),
            })),
            searchable: true,
          }
        : undefined,
    }),
    [events, clubs, ageGroups, modules.filterClub, modules.latinNames],
  );

  return useMemo(() => ({ values, set, options, reset }), [values, set, options, reset]);
}

export default useSeasonBestFilterHost;

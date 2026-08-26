import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { FilterSelected } from '../../../utils/interfaces/filter-selected';
import Helper from '../../../utils/helpers/data-helper';
import { getFilterData } from './filter-types';
import { useFilteredByTypeResults } from './use-filtered-results';
import { useResultsLoadMode } from '../../../hooks/useResultsLoadMode';
import { useFilterHints } from '../../../hooks/useFilterHints';
import { ClubSummary, useClubSummary } from '../../../hooks/useClubSummary';

/**
 * ШОВ ПАНЕЛИ ФИЛЬТРОВ (Ф2 плана `docs/plans/filters-reusable-panel-plan.md`).
 *
 * Зачем. Каждый `Filter*` до этого сам ходил в Redux results, сам читал глобалку
 * `window.filter_data` и сам считал доступность по загруженной выборке — поэтому поставить
 * его на другую страницу было нельзя, и `/season-best` написала панель заново. Хост
 * отвечает на три вопроса за фильтр: **откуда значения, куда писать, откуда опции** (плюс
 * необязательный четвёртый — что сейчас недоступно). Сам фильтр остаётся вёрсткой.
 *
 * Как этим пользоваться в фильтре — одна строка вместо четырёх:
 *
 * ```tsx
 * const { values, set, options, isAvailable } = useFilterHost();
 * ```
 *
 * Провайдера может не быть — тогда `useFilterHost()` отдаёт Redux-хост, то есть ровно
 * прежнее поведение results. Провайдер нужен странице с ДРУГИМ источником состояния
 * (`/season-best` — query, Ф4).
 */

/**
 * Ось доступности. Сегодня в панели гасятся ровно две вещи: стили и дистанции, которых нет
 * в текущей выборке. Пол и бассейн не гасились никогда — и не начинают: список осей узкий
 * намеренно, расширять его нужно осознанно, а не «на всякий случай».
 */
export type FilterKind = 'style' | 'distance';

export interface FilterStyleOption {
  style_name: string;
  /** Подпись кнопки и сводки. Не задана — печатается сам `style_name` (так на results). */
  label?: string;
  /**
   * Дистанции строками — как они лежат в `filter_data`: там не только числа («4X50»),
   * и приведение к числу теряет эстафеты.
   */
  style_len: string[];
}

/**
 * Клуб как опция фильтра.
 *
 * `value` — то, что хост кладёт в свою модель: у results это ИМЯ клуба (`FilterSelected.club`),
 * у `/season-best` — `club_id` строкой. Компонент про эту разницу не знает, он передаёт
 * `value` обратно в `set()`; маппинг живёт в хосте.
 */
export interface FilterClubOption {
  value: string;
  label: string;
  /** Короткая приписка справа в строке (напр. число заплывов). Только для строк без `stats`. */
  note?: string;
  /** Метрики для строки клуба на results (`UI_ClubDetails`). Нет — рисуется простая строка. */
  stats?: {
    points: number;
    swimmerCount: number;
    successfulCount: number;
    gold: number;
    silver: number;
    bronze: number;
  };
}

/**
 * Что означают кнопки возраста. Различие не косметическое: у results это ГОД РОЖДЕНИЯ
 * (и тогда работает диапазон долгим нажатием, а под кнопкой стоит возраст на дату стартов),
 * у мастерсов — готовая возрастная ГРУППА протокола («25-29»), у season-best будет возраст
 * В СЕЗОНЕ. Диапазон осмыслен только для годов рождения.
 */
export type FilterAgeMode = 'birth-year' | 'age-group' | 'age';

export interface FilterAgeOptions {
  mode: FilterAgeMode;
  /** Кнопки; 'all' первым элементом. */
  values: string[];
  /** Годы соревнований выборки — из них считается подпись «(12-13)» под годом рождения.
   *  Пусто — подписи нет. Осмысленно только при `mode === 'birth-year'`. */
  compYears: number[];
  /**
   * Вторая шкала под чертой — ДРУГАЯ система координат в той же карточке. Заведена под
   * мастерские возрастные группы на `/season-best`: у мастерсов ровесники это группа-пятилетка,
   * а не год, но спрашивают об этом там же, где про возраст. Не задана — карточка выглядит
   * ровно как раньше (так на results).
   */
  extra?: { title: string; values: string[] };
}

export interface FilterClubOptions {
  items: FilterClubOption[];
  /** Показывать поле поиска. Решает хост: список из сервера бывает длинным, локальная
   *  сводка по выборке — нет. */
  searchable?: boolean;
}

export interface FilterOptions {
  styles: FilterStyleOption[];
  /** Включая 'all' — первым элементом, как в `filter_data`. */
  genders: string[];
  /** Включая 'all'. Значения сырые ('25'/'50' на results, '25m'/'50m' у season-best) —
   *  сравнивать через `Helper.resolvePoolType`, а не строкой. */
  poolTypes: string[];
  /**
   * Клубы текущего среза (results: сводка по выборке или `useClubSummary` в paged-режиме;
   * Ф4 — клубы из ответа списка). Не задан — карточка клуба не рисуется.
   */
  clubs?: FilterClubOptions;
  /** Возраста. Не задан — карточка возраста не рисуется. */
  ages?: FilterAgeOptions;
}

export interface FilterHost {
  /** Текущие значения. Ключи — поля `FilterSelected`; хост сам маппит на свою модель. */
  values: Partial<FilterSelected>;
  /** Записать изменение. Хост решает: dispatch в Redux или правка query. */
  set(patch: Partial<FilterSelected>): void;
  options: FilterOptions;
  /** Есть ли такое значение в текущей выборке. Не задан — всё доступно. */
  isAvailable?(kind: FilterKind, value: string): boolean;
  /**
   * Вернуть фильтры к умолчаниям страницы. Живёт в хосте, а не в кнопке: набор полей и
   * сами умолчания у каждой страницы свои (у results — свои десять, у season-best — свои).
   */
  reset(): void;
}

const FilterHostContext = createContext<FilterHost | null>(null);

const EMPTY_AVAILABLE = { styles: new Set<string>(), distances: new Set<string>() };

/**
 * Хост поверх Redux results — ровно то, что фильтры делали сами до Ф2:
 * значения из `state.filterSelected`, запись через `rootActions.updateState`, опции из
 * `window.filter_data`, доступность из загруженной выборки (в paged-режиме — из
 * `/api/results/filter-hints`, потому что на клиенте там только текущая страница).
 *
 * `active === false` — хост построен «вхолостую» (страница дала свой провайдер, но правило
 * хуков требует вызвать этот хук всё равно). В этом состоянии он НЕ читает `filter_data`,
 * НЕ сканирует выборку и НЕ ходит за подсказками фильтров: остаётся только подписка на стор
 * и общий на страницу `/api/client-config` из `useResultsLoadMode` (кэшируется в
 * `ResultsLoadModeHelper`, один запрос на страницу независимо от числа фильтров).
 */
export function useReduxFilterHost(active = true): FilterHost {
  const dispatch = useAppDispatch();
  const values = useAppSelector((state) => state.filterSelected);
  const isMasters = useAppSelector((state) => !!state.dataSourceSelected?.is_masters);
  const sourceParams = useAppSelector(
    (state) => state.dataSourceSelected?.sourceParams,
  );
  // Вхолостую в глобалку не лезем: `getFilterData()` пишет console.error, когда
  // `filter-data.js` на странице не подключён (а на deep-страницах его нет).
  const filterData = active ? getFilterData() : null;

  // Мержим в САМЫЕ СВЕЖИЕ значения, а не в захваченные рендером. Это даёт `set` постоянную
  // ссылку (в зависимостях только `dispatch`) и снимает риск протухшего замыкания: держатель
  // старого колбэка — мемоизированный компонент, обработчик из прошлого рендера — иначе
  // смержил бы патч в устаревшие значения и затёр чужое изменение.
  const valuesRef = useRef(values);
  valuesRef.current = values;

  const set = useCallback(
    (patch: Partial<FilterSelected>) => {
      dispatch(
        rootActions.updateState({
          filterSelected: { ...valuesRef.current, ...patch },
        }),
      );
    },
    [dispatch],
  );

  /**
   * Сброс. Набор полей — прежней кнопки `filter-reset-button` плюс то, что она забывала
   * (решение Влада 2026-08-26): **клуб сбрасывается**, и вместе с `age` обнуляется `age_to` —
   * верх диапазона годов. Раньше и клуб, и недосброшенный верх диапазона переживали нажатие,
   * то есть «Reset Filters» оставлял фильтры включёнными.
   */
  const reset = useCallback(() => {
    dispatch(
      rootActions.updateState({
        filterSelected: {
          ...valuesRef.current,
          date_str: new Date().toISOString().split('T')[0],
          pool_type: 'all',
          gender: 'all',
          style_name: '',
          style_len: 0,
          age: 'all',
          age_to: undefined,
          club: 'all',
          position_filter: 'top',
          level_filter: 'all',
          event_date: 'all',
          event_category: 'all',
          show_prelims: false,
        },
      }),
    );
  }, [dispatch]);

  const styleOptions = useMemo<FilterOptions['styles']>(() => {
    if (!filterData) return [];
    return filterData.style_list.map((style) => ({
      style_name: style.style_name,
      // `.map(String)` не лишний: глобалка не типизирована, в старых выгрузках дистанции
      // попадались числами, а сравнение с выбранным значением строковое.
      style_len: style.style_len.map(String),
    }));
  }, [filterData]);

  const results = useFilteredByTypeResults(active);
  const mode = useResultsLoadMode();
  const paged = active && mode === 'paged';
  // Paged: доступность не вычислить из одной загруженной страницы — источник глобальный
  // filter-hints (контракт 3.2 §4). enabled=false — фетча нет вовсе.
  const styleHints = useFilterHints('style', '', 50, paged);
  const distanceHints = useFilterHints('distance', '', 50, paged);
  const styleName = values.style_name;

  const available = useMemo(() => {
    // Спящий хост не считает ничего: его результат всё равно не будет использован —
    // `useFilterHost()` вернёт хост провайдера.
    if (!active) return EMPTY_AVAILABLE;
    if (paged) {
      return {
        styles: new Set(styleHints),
        distances: new Set(distanceHints.map(String)),
      };
    }
    const styles = new Set<string>();
    const distances = new Set<string>();
    results.forEach((r) => {
      if (r.event_style_name) styles.add(r.event_style_name);
      // Дистанции — только выбранного стиля: 800 брассом в протоколах не бывает.
      if (styleName && r.event_style_name === styleName && r.event_style_len != null) {
        distances.add(String(r.event_style_len));
      }
    });
    return { styles, distances };
  }, [active, paged, styleHints, distanceHints, results, styleName]);

  const isAvailable = useCallback(
    (kind: FilterKind, value: string) =>
      kind === 'style'
        ? available.styles.has(value)
        : available.distances.has(String(value)),
    [available],
  );

  /**
   * Возраста. У results ось — ГОД РОЖДЕНИЯ, а у мастерсов протокол сразу даёт возрастные
   * группы («25-29»), и года рождения там нет. Годы соревнований нужны для подписи под
   * кнопкой: один и тот же 2013-й на осенних и весенних стартах даёт разный возраст.
   */
  const ages = useMemo<FilterAgeOptions | undefined>(() => {
    if (!active) return undefined;

    if (isMasters) {
      const groups = new Set<string>();
      results.forEach((item) => {
        if (item.age_group) groups.add(item.age_group);
      });
      const sorted = Array.from(groups).sort(
        (a, b) => Number(a.split('-')[0]) - Number(b.split('-')[0]),
      );
      return { mode: 'age-group', values: ['all', ...sorted], compYears: [] };
    }

    const birthYears = new Set<number>();
    const compYears = new Set<number>();
    results.forEach((item) => {
      if (item.birth_year) birthYears.add(Number(item.birth_year));
      if (item.date) {
        const parts = item.date.includes('/')
          ? item.date.split('/')
          : item.date.split('-');
        const year = item.date.includes('/') ? Number(parts[2]) : Number(parts[0]);
        if (year > 1900) compYears.add(year);
      }
    });

    return {
      mode: 'birth-year',
      values: [
        'all',
        ...Array.from(birthYears)
          .sort((a, b) => a - b)
          .map(String),
      ],
      compYears: Array.from(compYears).sort((a, b) => a - b),
    };
  }, [active, isMasters, results]);

  /**
   * Клубы. В full-режиме сводку (очки/медали/пловцы) считает клиент по выборке — асинхронно,
   * потому что очки клуба тянут правила соревнования; в paged-режиме её отдаёт сервер
   * (фаза 3.4), и список бывает длинным — там же включается поиск.
   */
  const [summaryClubs, setSummaryClubs] = useState<ClubSummary[]>([]);
  useEffect(() => {
    if (!active || paged) {
      setSummaryClubs([]);
      return;
    }
    let cancelled = false;
    Helper.getClubsSummary(results).then((list: ClubSummary[]) => {
      if (!cancelled) setSummaryClubs(list);
    });
    return () => {
      cancelled = true;
    };
  }, [active, paged, results]);

  const pagedClubs = useClubSummary(sourceParams, paged);

  const clubs = useMemo<FilterClubOptions | undefined>(() => {
    if (!active) return undefined;
    const list = paged ? pagedClubs : summaryClubs;
    return {
      // На results значение фильтра — само ИМЯ клуба (`FilterSelected.club`), поэтому
      // value === label. У страницы с club_id они разойдутся, компонент это переживёт.
      items: list.map((c) => ({
        value: c.club,
        label: c.club,
        stats: {
          points: c.points,
          swimmerCount: c.swimmerCount,
          successfulCount: c.successfulCount,
          gold: c.gold,
          silver: c.silver,
          bronze: c.bronze,
        },
      })),
      searchable: paged,
    };
  }, [active, paged, pagedClubs, summaryClubs]);

  const options = useMemo<FilterOptions>(
    () => ({
      styles: styleOptions,
      genders: filterData?.gender ?? [],
      poolTypes: filterData?.pool_type ?? [],
      ages,
      clubs,
    }),
    [styleOptions, filterData, ages, clubs],
  );

  return useMemo(
    () => ({ values, set, options, isAvailable, reset }),
    [values, set, options, isAvailable, reset],
  );
}

/**
 * Провайдер произвольного хоста — им пользуется страница со СВОИМ источником состояния
 * (Ф4: `QueryFilterHost` на `/season-best`, состояние в адресе). Сам контекст наружу не
 * отдаётся намеренно: единственный способ подменить хост — этот компонент.
 */
export const FilterHostProvider: React.FC<{
  host: FilterHost;
  children: React.ReactNode;
}> = ({ host, children }) => (
  <FilterHostContext.Provider value={host}>{children}</FilterHostContext.Provider>
);

/**
 * Провайдер Redux-хоста. Стоит на панели results (`filter-section.tsx`): дорогая часть —
 * чтение опций, скан выборки, подсказки фильтров — считается ОДИН раз на панель, а фильтры
 * внутри берут готовый хост из контекста.
 */
export const ReduxFilterHost: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const host = useReduxFilterHost();
  return <FilterHostProvider host={host}>{children}</FilterHostProvider>;
};

/** Хост текущей страницы. Провайдера нет — Redux results, как было до Ф2. */
export function useFilterHost(): FilterHost {
  const provided = useContext(FilterHostContext);
  // Хук вызывается всегда (правило хуков), но при живом провайдере его результат не нужен,
  // поэтому `active=false` выключает внутри всё дорогое — остаётся подписка на стор.
  const fallback = useReduxFilterHost(provided == null);
  return provided ?? fallback;
}

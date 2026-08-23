import React from 'react';
import { createPortal } from 'react-dom';
import './filter-data-source-ddl.css';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { Result } from '../../../utils/interfaces/results';
import { routes, parseRoute } from '../../../utils/routes';
import { seasonLabel, seasonStartYear } from '../../../utils/helpers/season-helper';
import { ResultsPaging } from '../../../store/store';
import {
  RESULTS_CATEGORIES,
  resolveCategoryKey,
  ResultsCategory,
} from '../../../utils/constants/results-categories';
import CategoryHelper, { CategoryDisplay } from '../../../utils/helpers/category-helper';
import ResultsLoadModeHelper from '../../../utils/helpers/results-load-mode';
import { useResultsLoadMode } from '../../../hooks/useResultsLoadMode';
import {
  PAGED_PAGE_SIZE,
  buildResultsFilterParams,
  fetchResultsPage,
} from '../../../utils/helpers/results-api';
import {
  CompetitionSource,
  parseDate,
  dateLabel,
  monthLabel,
  sourceUrlParam,
  isChampionshipSource,
} from '../../../utils/helpers/competition-source';

// ── Категорийный селектор соревнований ──────────────────────────────────────
// База: design_handoff_category_selector (табы + поиск + сезон + live/upcoming +
// месячные группы + архив). Дельта design_handoff_selector_all: выбор сворачивает
// селектор в акцентную шапку; «Change» открывает панель как дропдаун (desktop)
// или bottom sheet (mobile, <640px); deep-link ?category= рендерит панель inline.

// Загрузка результатов с серверного API (/api/results).
// Поля ResultDto совпадают с клиентским Result — конвертация не нужна.
// В dev работает через Vite-прокси /api → http://localhost:5078.
//
// Режим загрузки (админ-настройка ResultsLoadMode + ?loadMode=, см. ResultsLoadModeHelper):
//   'full'  — качаем ВСЕ страницы соревнования, фильтрация — на клиенте (ResultsTable);
//   'paged' — этап 3.2 роадмапа: фильтры уходят на сервер query-параметрами (buildResultsFilterParams,
//             контракт §2), грузится только одна страница за раз.
const loadFromApi = async (
  sourceParams: Record<string, string> = {},
  filterParams: Record<string, string> = {},
): Promise<{ results: Result[]; paging?: ResultsPaging }> => {
  const mode = await ResultsLoadModeHelper.getMode();
  if (mode === 'paged') {
    return await fetchResultsPage(sourceParams, filterParams, 1, PAGED_PAGE_SIZE);
  }
  const all: Result[] = [];
  let page = 1;
  const pageSize = 500; // максимум, отдаваемый сервером
  for (;;) {
    const qs = new URLSearchParams({
      ...sourceParams,
      page: String(page),
      pageSize: String(pageSize),
    });
    const resp = await fetch(`/api/results?${qs}`, { credentials: 'same-origin' });
    if (!resp.ok) throw new Error(`API /api/results returned ${resp.status}`);
    const json = await resp.json();
    all.push(...((json.data ?? []) as Result[]));
    if (!json.hasMore) break;
    page += 1;
  }
  return { results: all };
};

// Архив: завершённые старше начала прошлого месяца прячутся за «Show N earlier».
const isArchived = (src: CompetitionSource, now: Date): boolean => {
  if (src.status !== 'done') return false;
  const d = parseDate(src.date);
  if (!d) return false;
  const boundary = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  return d < boundary;
};

// Обновляем только свои параметры, не трогая чужие. id и category взаимоисключающие.
const writeUrl = (
  patch: { category?: string; source?: CompetitionSource | null; season?: number | 'all' | null },
) => {
  const params = new URLSearchParams(window.location.search);
  ['category', 'cat', 'competitionId', 'eventId'].forEach((k) => params.delete(k));
  // Идентичность соревнования уходит в путь (/competitions/{id}); eventId и категория —
  // это фасеты вида, остаются в query на /results.
  let pathname = routes.results();
  if (patch.source) {
    if (patch.source.kind === 'event') params.set('eventId', String(patch.source.id));
    else pathname = routes.competition(patch.source.id);
  } else if (patch.category) {
    params.set('category', patch.category);
  }
  if (patch.season != null) params.set('season', String(patch.season));
  else params.delete('season');
  const qs = params.toString();
  window.history.replaceState(null, '', qs ? `${pathname}?${qs}` : pathname);
};

// Брейкпоинт дропдаун ↔ bottom sheet: <640px (Tailwind sm)
const useIsMobile = () => {
  const [mobile, setMobile] = React.useState(
    () => typeof window !== 'undefined' && window.matchMedia('(max-width: 639px)').matches,
  );
  React.useEffect(() => {
    const mq = window.matchMedia('(max-width: 639px)');
    const onChange = (e: MediaQueryListEvent) => setMobile(e.matches);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);
  return mobile;
};

// Компонент может монтироваться несколько раз — грузить данные
// по URL-параметрам должен только один инстанс.
let urlLoadTriggered = false;

interface DataSourceDDLProps {
  /**
   * Кастомная шапка вместо встроенной (шапка соревнования с табами,
   * design_handoff_competition_overview): DDL отдаёт ей toggle панели и выбранный
   * источник, а сам рендерит только дропдаун/шит выбора. Без выбранного источника
   * игнорируется (панель открыта inline, как раньше).
   */
  renderHeader?: (
    togglePanel: () => void,
    panelOpen: boolean,
    source: CompetitionSource,
  ) => React.ReactNode;
  /**
   * Ленивая загрузка Swims (design_handoff_competition_overview): в режиме соревнования
   * дефолтный таб — Overview, а тяжёлый фетч результатов (`/api/results`, до N страниц)
   * откладывается, пока не пришёл Overview или пока юзер не открыл таб Swims. Когда false,
   * при выборе источника мы только «праймим» шапку (title/sourceParams/флаги из
   * CompetitionSource), но не тянем результаты; фетч случится, когда флаг станет true.
   * Undefined/true → прежнее поведение (грузим сразу). См. pendingSourceRef ниже.
   */
  canLoadResults?: boolean;
}

const DataSourceDDL: React.FC<DataSourceDDLProps> = ({ renderHeader, canLoadResults = true }) => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((s) => s.filterSelected);
  // Выбранная строка — из store (title = имя источника): синхронизирует инстансы.
  const selectedTitle = useAppSelector((s) => s.dataSourceSelected?.title);
  const isMobile = useIsMobile();

  const [sources, setSources] = React.useState<CompetitionSource[]>([]);
  const [loaded, setLoaded] = React.useState(false);
  const [cat, setCat] = React.useState<string>('all');
  const [search, setSearch] = React.useState('');
  const [season, setSeason] = React.useState<number | 'all' | null>(null);
  const [seasonTouched, setSeasonTouched] = React.useState(false);
  const [archiveOpen, setArchiveOpen] = React.useState(false);
  // Тумблер «🏆 Championships» — показывать только чемпионаты Израиля.
  const [champOnly, setChampOnly] = React.useState(false);
  // Панель поверх контента при выбранном соревновании (кнопка «Change» в шапке)
  const [panelOpen, setPanelOpen] = React.useState(false);
  // Мобильное меню категорий (вариант 9b: строка «Category: … ▾» вместо табов)
  const [catMenuOpen, setCatMenuOpen] = React.useState(false);
  // name/badge категорий из /api/categories (БД). Ключи: канонические ('kids8_11' и т.п.,
  // маппинг внутри CategoryHelper) + сырые ключи кастомных категорий (result-maccabiah…).
  // Пусто до первой загрузки — тогда используется статичный RESULTS_CATEGORIES.label.
  const [liveCategoryLabels, setLiveCategoryLabels] = React.useState<Record<string, CategoryDisplay>>({});
  // Кастомные категории из Admin/Categories — отдельные табы после статичных.
  const [customCategories, setCustomCategories] = React.useState<ResultsCategory[]>([]);
  const [categoriesLoaded, setCategoriesLoaded] = React.useState(false);
  const wrapRef = React.useRef<HTMLDivElement>(null);
  const touchStartY = React.useRef<number | null>(null);

  React.useEffect(() => {
    let cancelled = false;
    Promise.all([CategoryHelper.getCanonicalMap(), CategoryHelper.getAll()]).then(([map, all]) => {
      if (cancelled) return;
      const customs = all
        .filter((c) => !CategoryHelper.SYSTEM_DB_KEYS.has(c.key))
        .sort((a, b) => a.display_order - b.display_order);
      const labels: Record<string, CategoryDisplay> = { ...map };
      customs.forEach((c) => {
        labels[c.key] = { name: c.name, badge: c.badge };
      });
      setLiveCategoryLabels(labels);
      setCustomCategories(
        customs.map((c) => ({
          key: c.key,
          label: c.name,
          title: c.name,
          competitionTheme: 'competition-emerald',
        })),
      );
      setCategoriesLoaded(true);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // Статичные канонические табы + кастомные категории из БД.
  const allCategories = React.useMemo(
    () => [...RESULTS_CATEGORIES, ...customCategories],
    [customCategories],
  );

  // Живой name+badge из БД (Admin/Categories), пока не загружен — статичный label.
  const categoryLabel = (c: ResultsCategory): string => {
    const live = liveCategoryLabels[c.key];
    if (!live) return c.label;
    return live.badge ? `${live.badge} ${live.name}` : live.name;
  };

  // Бейдж-кружок (буква из БД) + название — для табов/строк селектора категорий.
  // Кружок не завязан на active/неактивный вариант пилюли отдельными цветами (у tabsRow
  // активная пилюля залита --theme-primary, у categoryRow активная строка — лишь лёгкий
  // --theme-primary-light тон; единой пары «фон+текст», контрастной сразу везде, в теме нет).
  // Вместо этого — обводка currentColor: кружок наследует уже правильно посчитанный цвет
  // текста родителя в каждом состоянии и остаётся читаемым без доп. параметров.
  const categoryLabelNode = (c: ResultsCategory): React.ReactNode => {
    const live = liveCategoryLabels[c.key];
    if (!live?.badge) return categoryLabel(c);
    return (
      <span className="inline-flex items-center gap-1.5">
        <span
          className="inline-flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-full text-[10px] font-extrabold leading-none"
          style={{ border: '1.5px solid currentColor' }}
        >
          {live.badge}
        </span>
        {live.name}
      </span>
    );
  };

  // Закрытие по Esc (всегда) и по клику вне (только desktop-дропдаун —
  // у мобильного sheet эту роль играет backdrop).
  React.useEffect(() => {
    if (!panelOpen) return;
    const onDown = (e: MouseEvent) => {
      if (isMobile) return;
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setPanelOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPanelOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [panelOpen, isMobile]);

  // Панель закрылась → сворачиваем и меню категорий
  React.useEffect(() => {
    if (!panelOpen) setCatMenuOpen(false);
  }, [panelOpen]);

  // Body scroll lock, пока открыт мобильный sheet
  React.useEffect(() => {
    if (!(panelOpen && isMobile)) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [panelOpen, isMobile]);

  React.useEffect(() => {
    const load = async () => {
      try {
        const resp = await fetch('/api/competitions', { credentials: 'same-origin' });
        setSources(resp.ok ? ((await resp.json()) as CompetitionSource[]) : []);
      } catch (e) {
        console.error('Error loading /api/competitions', e);
        setSources([]);
      }
      setLoaded(true);
    };
    load();
  }, []);

  // Ключ последнего paged-фетча (source+фильтры) — чтобы эффект синхронизации фильтров
  // (ниже) не задвоил запрос сразу после loadSource (тот тоже меняет filterSelected: date/date_str).
  const lastPagedFetchKeyRef = React.useRef<string | null>(null);
  const pagedFetchKey = (sourceParams: Record<string, string>, filterParams: Record<string, string>) =>
    JSON.stringify({ sourceParams, filterParams });

  // Источник, у которого «зашита» шапка, но результаты ещё не загружены (ленивый Swims).
  // Заполняется, когда canLoadResults=false на момент выбора; эффект ниже дотягивает
  // результаты, как только флаг откроется (пришёл Overview / открыт таб Swims).
  const pendingSourceRef = React.useRef<CompetitionSource | null>(null);

  // «Прайм» шапки без результатов: title/sourceParams/флаги берём из CompetitionSource
  // (/api/competitions), чтобы шапка соревнования и ✓ в списке появились сразу, а тяжёлый
  // фетч /api/results отложился. is_masters известен из category; is_award/show_combine
  // уточнятся, когда придут результаты (в loadSource). НЕ трогаем lastPagedFetchKeyRef,
  // чтобы paged-эффект синхронизации, разблокировавшись, честно дотянул страницу 1.
  const primeSource = (src: CompetitionSource) => {
    const [k, v] = sourceUrlParam(src);
    const sourceParams = { [k]: v };
    lastPagedFetchKeyRef.current = null;
    dispatch(
      rootActions.updateState({
        dataSourceSelected: {
          results: [],
          title: src.name,
          is_masters: src.category === 'masters',
          is_award: false,
          sourceParams,
          day_dates: src.day_dates,
        },
      }),
    );
  };

  // Загрузка результатов выбранного соревнования в store.
  // Флаги датасета (is_masters/is_award/show_combine) берём из самих результатов —
  // ResultDto несёт их денормализованно per-competition. category соревнования из /api/competitions
  // определяет isMasters ДО загрузки — нужно для маппинга фильтра Age в paged-режиме (contract §2).
  const loadSource = async (src: CompetitionSource) => {
    const [k, v] = sourceUrlParam(src);
    const sourceParams = { [k]: v };
    // event_date — фильтр КОНКРЕТНОГО источника: день Maccabiah бессмыслен для другого
    // соревнования (в paged сервер честно вернёт 0 строк, в full клиентский фильтр так же
    // опустошит таблицу) — при смене источника сбрасываем.
    const effectiveFilters = { ...filters, event_date: 'all' };
    const filterParams = buildResultsFilterParams(effectiveFilters, src.category === 'masters');
    let data: Result[];
    let paging: ResultsPaging | undefined;
    try {
      ({ results: data, paging } = await loadFromApi(sourceParams, filterParams));
    } catch (e) {
      console.error('Error loading data source', e);
      return;
    }
    lastPagedFetchKeyRef.current = pagedFetchKey(sourceParams, filterParams);

    const showCombine =
      data.length > 0 && data.every((r) => r.show_combine_all_results === true);
    const isMasters = data.some((r) => r.is_masters === true);
    const isAward = data.some((r) => r.is_award === true);

    const dates = data.map((r) => r.date).filter(Boolean) as string[];
    const maxDate = dates.length
      ? dates.reduce((max, curr) =>
          (parseDate(curr)?.getTime() ?? 0) > (parseDate(max)?.getTime() ?? 0) ? curr : max,
        )
      : '';
    const [d, m, y] = maxDate.split('/');
    const iso = maxDate ? `${y}-${m.padStart(2, '0')}-${d.padStart(2, '0')}` : '';

    dispatch(
      rootActions.updateState({
        dataSourceSelected: {
          results: data,
          title: src.name,
          is_masters: isMasters,
          is_award: isAward,
          sourceParams,
          day_dates: src.day_dates,
        },
        showCombineAllResults: showCombine,
        resultsPaging: paging,
        filterSelected: maxDate
          ? { ...effectiveFilters, date: maxDate, date_str: iso }
          : effectiveFilters,
      }),
    );
  };

  // Paged-режим: смена любого серверного фильтра → рефетч страницы 1 с новыми query-параметрами
  // (контракт 3.2 §4). lastPagedFetchKeyRef защищает от повторного фетча тех же параметров —
  // срабатывает и на самостоятельный вызов loadSource (тот уже обновил ref), и на «пустые»
  // изменения filterSelected (date/date_str), которые в query не участвуют.
  const mode = useResultsLoadMode();
  const dataSourceSelected = useAppSelector((s) => s.dataSourceSelected);
  const filtersKey = JSON.stringify(filters);
  React.useEffect(() => {
    if (mode !== 'paged') return;
    if (!canLoadResults) return; // ленивый Swims: не тянем страницу, пока гейт закрыт
    const sourceParams = dataSourceSelected?.sourceParams;
    if (!sourceParams) return;

    const filterParams = buildResultsFilterParams(filters, !!dataSourceSelected?.is_masters);
    const key = pagedFetchKey(sourceParams, filterParams);
    if (key === lastPagedFetchKeyRef.current) return;
    lastPagedFetchKeyRef.current = key;

    (async () => {
      try {
        const { results, paging } = await fetchResultsPage(sourceParams, filterParams, 1, PAGED_PAGE_SIZE);
        dispatch(
          rootActions.updateState({
            dataSourceSelected: { ...dataSourceSelected, results, sourceParams },
            resultsPaging: paging,
          }),
        );
      } catch (e) {
        console.error('Error refetching paged results', e);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, canLoadResults, dataSourceSelected?.sourceParams, filtersKey]);

  // Ленивый Swims (full-режим): дотягиваем отложенный источник, когда гейт открылся.
  // В paged-режиме отложенный фетч делает эффект синхронизации выше (по sourceParams),
  // поэтому здесь только full — иначе был бы двойной запрос.
  React.useEffect(() => {
    if (!canLoadResults || mode === 'paged') return;
    const src = pendingSourceRef.current;
    if (!src) return;
    pendingSourceRef.current = null;
    void loadSource(src);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canLoadResults, mode]);

  // Выбор источника: если результаты грузить рано (Overview ещё не пришёл, таб не Swims) —
  // «праймим» шапку и откладываем; иначе тянем сразу (прежнее поведение).
  const selectSource = (src: CompetitionSource) => {
    writeUrl({ source: src, season: seasonTouched ? season : null });
    setPanelOpen(false);
    if (canLoadResults) {
      void loadSource(src);
    } else {
      pendingSourceRef.current = src;
      primeSource(src);
    }
  };

  // Инициализация из URL: ?category= (легаси ?cat=) активирует таб;
  // ?competitionId=<id|last> / ?eventId=<id> грузит соревнование сразу.
  const initializedRef = React.useRef(false);
  React.useEffect(() => {
    if (!loaded || initializedRef.current) return;
    initializedRef.current = true;

    const params = new URLSearchParams(window.location.search);
    const seasonRaw = params.get('season');
    if (seasonRaw === 'all') {
      setSeason('all');
      setSeasonTouched(true);
    } else if (Number(seasonRaw)) {
      setSeason(Number(seasonRaw));
      setSeasonTouched(true);
    }

    const competitionId = parseRoute().competitionId ?? params.get('competitionId');
    const eventId = params.get('eventId');

    let target: CompetitionSource | undefined;
    if (competitionId === 'last') {
      target = sources[0]; // /api/competitions отсортирован по дате desc
    } else if (eventId) {
      target = sources.find((s) => s.kind === 'event' && String(s.id) === eventId);
    } else if (competitionId) {
      target = sources.find((s) => s.kind === 'competition' && String(s.id) === competitionId);
    }

    if (target) {
      setCat(target.category ?? 'all');
      if (!urlLoadTriggered) {
        urlLoadTriggered = true;
        // Ленивый Swims: на старте дефолт — таб Overview, поэтому обычно праймим шапку
        // и откладываем результаты (эффект дотягивания сработает по гейту).
        if (canLoadResults) {
          void loadSource(target);
        } else {
          pendingSourceRef.current = target;
          primeSource(target);
        }
      }
      return;
    }
    // Конкретный id вне списка (например, день события) — грузим напрямую, таб не трогаем.
    // Флаги датасета обязательны и здесь: без is_award медали соревнования не считаются.
    // category соревнования тут неизвестна заранее (нет в CompetitionSource-списке) —
    // Age-фильтр в paged-режиме мапится как non-masters (birthYear); край-кейс direct-link.
    if (competitionId || eventId) {
      if (!urlLoadTriggered) {
        urlLoadTriggered = true;
        const sourceParams: Record<string, string> = eventId ? { eventId } : { competitionId: competitionId! };
        const filterParams = buildResultsFilterParams(filters, false);
        void loadFromApi(sourceParams, filterParams).then(({ results: data, paging }) => {
          if (!data.length) return;
          lastPagedFetchKeyRef.current = pagedFetchKey(sourceParams, filterParams);
          dispatch(
            rootActions.updateState({
              dataSourceSelected: {
                results: data,
                title: data[0].competition,
                is_masters: data.some((r) => r.is_masters === true),
                is_award: data.some((r) => r.is_award === true),
                sourceParams,
              },
              showCombineAllResults:
                data.length > 0 && data.every((r) => r.show_combine_all_results === true),
              resultsPaging: paging,
            }),
          );
        });
      }
      return;
    }

    // resolveCategoryKey сводит неизвестные ключи к 'all' — кастомные категории из БД
    // (result-maccabiah и т.п.) пропускаем как есть: их таб появится после /api/categories,
    // а мусорный ключ сбросит валидация ниже.
    const rawCat = params.get('category') ?? params.get('cat');
    const staticKey = resolveCategoryKey(rawCat);
    setCat(staticKey === 'all' && rawCat && rawCat !== 'all' ? rawCat : staticKey);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loaded, sources]);

  // Ключ из URL, которого нет ни среди статичных, ни среди кастомных категорий → 'all'.
  React.useEffect(() => {
    if (!categoriesLoaded) return;
    if (cat !== 'all' && !allCategories.some((c) => c.key === cat)) setCat('all');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [categoriesLoaded, allCategories]);

  // ── Производные списки ──
  // Сезон, а НЕ календарный год: старт 31/10/2025 принадлежит сезону 2025/26 вместе с
  // февральскими стартами 2026. По getFullYear() он уезжал в «Season 2025» отдельно от
  // своего же сезона (docs/season-boundary-rule.md).
  const seasons = React.useMemo(() => {
    const years = new Set(
      sources
        .map((s) => {
          const d = parseDate(s.date);
          return d ? seasonStartYear(d) : null;
        })
        .filter((y) => y !== null) as number[],
    );
    return [...years].sort((a, b) => b - a);
  }, [sources]);
  const activeSeason = season ?? seasons[0] ?? seasonStartYear();

  const now = new Date();
  // Канонический таб матчится по category, кастомный — по членству в categories
  // (одно соревнование может быть в нескольких: Maccabiah Masters = Masters + Maccabiah).
  const inCat = (s: CompetitionSource) =>
    cat === 'all' || s.category === cat || (s.categories?.includes(cat) ?? false);
  const inSeason = (s: CompetitionSource) => {
    if (activeSeason === 'all') return true;
    const d = parseDate(s.date);
    return d ? seasonStartYear(d) === activeSeason : false;
  };
  const matchesSearch = (s: CompetitionSource) =>
    !search.trim() || s.name.toLowerCase().includes(search.trim().toLowerCase());

  const isChamp = (s: CompetitionSource) => isChampionshipSource(s);

  const visible = sources.filter(
    (s) => inCat(s) && inSeason(s) && matchesSearch(s) && (!champOnly || isChamp(s)),
  );
  const champCount = sources.filter((s) => inCat(s) && inSeason(s) && matchesSearch(s) && isChamp(s)).length;
  const nowItems = visible.filter((s) => s.status !== 'done');
  const doneItems = visible.filter((s) => s.status === 'done');
  const archivedItems = doneItems.filter((s) => isArchived(s, now));
  const shownDone = archiveOpen ? doneItems : doneItems.filter((s) => !isArchived(s, now));

  const monthGroups: { label: string; items: CompetitionSource[] }[] = [];
  shownDone.forEach((s) => {
    const label = monthLabel(s);
    let g = monthGroups.find((x) => x.label === label);
    if (!g) {
      g = { label, items: [] };
      monthGroups.push(g);
    }
    g.items.push(s);
  });

  const activeCategory = allCategories.find((c) => c.key === cat);
  const activeCatLabel = activeCategory ? categoryLabel(activeCategory) : 'All';
  const isSelected = (s: CompetitionSource) =>
    !!selectedTitle && selectedTitle === s.name;

  // ── Рендер-помощник строки (mobile ≥52px, ✓ у выбранной — Row.dc.html) ──
  const row = (s: CompetitionSource) => {
    const live = s.status === 'live';
    const sel = !live && isSelected(s);
    const rowStyle: React.CSSProperties = live
      ? { background: 'var(--status-live-bg)', border: '1px solid var(--status-live-border)' }
      : sel
        ? { background: 'var(--theme-primary-light)', border: '1px solid var(--theme-primary)' }
        : { background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border-row)' };
    return (
      <button
        key={`${s.kind}:${s.id}`}
        type="button"
        onClick={() => selectSource(s)}
        className="dv-sel-row flex w-full cursor-pointer items-center justify-between gap-2.5 rounded-xl px-3.5 py-2 text-left max-sm:min-h-[52px] sm:py-3"
        style={rowStyle}
      >
        <span className="flex min-w-0 items-center gap-2">
          {sel && (
            <span className="shrink-0 text-sm font-black" style={{ color: 'var(--theme-primary)' }}>
              ✓
            </span>
          )}
          {isChamp(s) && (
            <span className="shrink-0 text-sm" title="Israeli championship" aria-label="Israeli championship">
              🏆
            </span>
          )}
          <span
            dir="rtl"
            className={`min-w-0 overflow-hidden text-ellipsis whitespace-nowrap text-sm ${live || sel ? 'font-bold' : 'font-semibold'}`}
            style={{
              // sel: фон --theme-primary-light не зависит от режима (light/dark) — текст красим
              // в --theme-primary (как sportsmen-details.tsx), а не --theme-mode-text.
              color: sel
                ? 'var(--theme-primary)'
                : live
                  ? 'var(--theme-mode-text)'
                  : 'var(--theme-mode-text-secondary)',
              textAlign: 'left',
            }}
          >
            {s.name}
          </span>
        </span>
        {live ? (
          <span className="flex shrink-0 items-center gap-1.5">
            <span
              className="dv-sel-live-dot h-1.5 w-1.5 rounded-full"
              style={{ background: 'var(--status-live)' }}
            />
            <span
              className="font-mono text-[10px] font-extrabold whitespace-nowrap"
              style={{ color: 'var(--status-live)' }}
            >
              LIVE
            </span>
          </span>
        ) : (
          <span
            className="shrink-0 text-xs whitespace-nowrap"
            style={{
              color:
                sel || s.status === 'upcoming'
                  ? 'var(--theme-primary)'
                  : 'var(--theme-mode-text-muted)',
              fontWeight: sel || s.status === 'upcoming' ? 700 : 400,
            }}
          >
            {dateLabel(s)}
          </span>
        )}
      </button>
    );
  };

  const catCount = (key: string) =>
    key === 'all'
      ? sources.length
      : sources.filter((s) => s.category === key || (s.categories?.includes(key) ?? false)).length;

  const selectCat = (key: string) => {
    setCat(key);
    setArchiveOpen(false);
    setCatMenuOpen(false);
    // Переключение категории НЕ сбрасывает выбор (selector_all, Change 4).
    // URL меняем на категорию только пока ничего не выбрано.
    if (!selectedSourceObj) {
      writeUrl({ category: key, season: seasonTouched ? season : null });
    }
  };

  // ── Переиспользуемые части панели (inline / dropdown / sheet) ──
  // Десктоп: табы-пилюли (как в базовом хендоффе)
  const tabsRow = (
    <div className="flex flex-wrap gap-2">
      {allCategories.map((c) => {
        const active = cat === c.key;
        return (
          <button
            key={c.key}
            type="button"
            onClick={() => selectCat(c.key)}
            className="inline-flex shrink-0 cursor-pointer items-center gap-1.5 whitespace-nowrap rounded-full px-4 py-2 text-[13px]"
            style={
              active
                ? {
                    background: 'var(--theme-primary)',
                    color: 'var(--theme-mode-accent-text)',
                    border: '1px solid var(--theme-primary)',
                    fontWeight: 800,
                  }
                : {
                    background: 'var(--theme-mode-input-bg)',
                    color: 'var(--theme-mode-text-secondary)',
                    border: '1px solid var(--theme-mode-border-input)',
                    fontWeight: 700,
                  }
            }
          >
            {categoryLabelNode(c)}{' '}
            <span style={{ fontWeight: 600, opacity: active ? 0.75 : 0.6 }}>{catCount(c.key)}</span>
          </button>
        );
      })}
    </div>
  );

  // Мобайл, вариант 9b: строка «Category: Junior · 3 ▾» (48px) + своё меню (ряды 46px, ✓)
  const categoryRow = (
    <div>
      <button
        type="button"
        onClick={() => setCatMenuOpen((v) => !v)}
        className="flex min-h-[48px] w-full cursor-pointer items-center justify-between rounded-xl px-3.5 text-[13px]"
        style={{
          background: 'var(--theme-mode-input-bg)',
          border: '1px solid var(--theme-mode-border-input)',
          color: 'var(--theme-mode-text)',
        }}
      >
        <span style={{ color: 'var(--theme-mode-text-secondary)', fontWeight: 700 }}>
          Category:{' '}
          <span style={{ color: 'var(--theme-mode-text)', fontWeight: 800 }}>
            {activeCategory ? categoryLabelNode(activeCategory) : 'All'}
          </span>{' '}
          · {catCount(cat)}
        </span>
        <span className="text-[10px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          {catMenuOpen ? '▴' : '▾'}
        </span>
      </button>
      {catMenuOpen && (
        <div
          className="mt-2 flex flex-col rounded-xl p-1"
          style={{
            background: 'var(--theme-mode-surface)',
            border: '1px solid var(--theme-mode-border-input)',
          }}
        >
          {allCategories.map((c) => {
            const active = cat === c.key;
            return (
              <button
                key={c.key}
                type="button"
                onClick={() => selectCat(c.key)}
                className="flex min-h-[46px] cursor-pointer items-center justify-between rounded-[10px] px-3 text-[14px]"
                style={{
                  background: active ? 'var(--theme-primary-light)' : 'transparent',
                  color: active ? 'var(--theme-primary)' : 'var(--theme-mode-text)',
                  fontWeight: active ? 800 : 600,
                }}
              >
                <span>
                  {categoryLabelNode(c)}{' '}
                  <span style={{ color: 'var(--theme-mode-text-muted)', fontWeight: 600 }}>
                    · {catCount(c.key)}
                  </span>
                </span>
                {active && (
                  <span className="font-black" style={{ color: 'var(--theme-primary)' }}>
                    ✓
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );

  const searchBox = (
    <div
      className="flex flex-1 items-center gap-2 rounded-xl px-3.5 py-2.5 max-sm:min-h-[44px]"
      style={{
        background: 'var(--theme-mode-input-bg)',
        border: '1px solid var(--theme-mode-border-input)',
      }}
    >
      <span className="text-sm" style={{ color: 'var(--theme-mode-text-muted)' }}>
        ⌕
      </span>
      <input
        type="text"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder={`Search in ${activeCatLabel}…`}
        className="dv-sel-search w-full bg-transparent text-[13.5px] outline-none"
        style={{ color: 'var(--theme-mode-text)' }}
      />
    </div>
  );

  const seasonSelect =
    seasons.length > 0 ? (
      <select
        value={activeSeason}
        onChange={(e) => {
          setSeason(e.target.value === 'all' ? 'all' : Number(e.target.value));
          setSeasonTouched(true);
        }}
        className="cursor-pointer rounded-xl px-3.5 py-2.5 text-[13px] font-bold max-sm:min-h-[44px]"
        style={{
          background: 'var(--theme-mode-input-bg)',
          border: '1px solid var(--theme-mode-border-input)',
          color: 'var(--theme-mode-text)',
        }}
      >
        <option value="all">All seasons</option>
        {seasons.map((y) => (
          <option key={y} value={y}>
            Season {seasonLabel(y)}
          </option>
        ))}
      </select>
    ) : null;

  // Тумблер «только чемпионаты Израиля» (on/off), рядом с поиском и сезоном.
  const champToggle = (
    <button
      type="button"
      onClick={() => setChampOnly((v) => !v)}
      aria-pressed={champOnly}
      title="Show Israeli championships only"
      className="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-xl px-3.5 py-2.5 text-[13px] font-bold whitespace-nowrap max-sm:min-h-[44px] max-sm:justify-center"
      style={
        champOnly
          ? {
              background: 'var(--theme-primary)',
              color: 'var(--theme-mode-accent-text)',
              border: '1px solid var(--theme-primary)',
            }
          : {
              background: 'var(--theme-mode-input-bg)',
              color: 'var(--theme-mode-text-secondary)',
              border: '1px solid var(--theme-mode-border-input)',
            }
      }
    >
      🏆 Championships{' '}
      <span style={{ fontWeight: 600, opacity: champOnly ? 0.75 : 0.6 }}>{champCount}</span>
    </button>
  );

  const listBody = (
    <>
      {!loaded && (
        <div className="py-2 text-sm" style={{ color: 'var(--theme-mode-text-muted)' }}>
          Loading competitions…
        </div>
      )}

      {/* Live / Upcoming — всегда сверху, вне группировки и архива */}
      {nowItems.length > 0 && (
        <>
          <div
            className="mb-2 text-[10.5px] font-extrabold uppercase tracking-[0.18em]"
            style={{ color: 'var(--status-live)' }}
          >
            Live · Upcoming
          </div>
          <div className="mb-4 flex flex-col gap-2">{nowItems.map(row)}</div>
        </>
      )}

      {/* Завершённые — по месяцам, новые сверху */}
      {monthGroups.map((g) => (
        <React.Fragment key={g.label}>
          <div
            className="mb-2 text-[10.5px] font-extrabold uppercase tracking-[0.18em]"
            style={{ color: 'var(--theme-mode-text-muted)' }}
          >
            {g.label}
          </div>
          <div className="mb-3.5 flex flex-col gap-2">{g.items.map(row)}</div>
        </React.Fragment>
      ))}

      {loaded && visible.length === 0 && (
        <div className="py-2 text-sm" style={{ color: 'var(--theme-mode-text-muted)' }}>
          No competitions found{search ? ` for «${search}»` : ''}.
        </div>
      )}

      {archivedItems.length > 0 && (
        <button
          type="button"
          onClick={() => setArchiveOpen((v) => !v)}
          className="flex w-full cursor-pointer items-center justify-center gap-2 rounded-xl p-3 text-[13px] font-bold max-sm:min-h-[48px]"
          style={{
            background: 'var(--theme-mode-surface)',
            border: '1px dashed var(--theme-mode-border-input)',
            color: 'var(--theme-mode-text-secondary)',
          }}
        >
          {archiveOpen
            ? 'Hide earlier competitions'
            : `Show ${archivedItems.length} earlier competitions`}
          <span className="text-[10px]">{archiveOpen ? '▴' : '▾'}</span>
        </button>
      )}
    </>
  );

  // Выбранное соревнование из списка источников (по title в store)
  const selectedSourceObj = selectedTitle
    ? sources.find((s) => s.name === selectedTitle) ?? null
    : null;

  // Карточка панели: inline (deep-link ?category=) и desktop-дропдаун
  const panel = (inline: boolean) => (
    <div
      className="dv-data-source rounded-[18px] px-[22px] py-5 max-sm:px-3.5"
      style={{
        background: 'var(--theme-mode-surface)',
        border: '1px solid var(--theme-mode-border)',
        boxShadow: 'var(--theme-mode-card-shadow)',
      }}
    >
      <div className="mb-3.5 flex items-center justify-between">
        <div
          className="text-[11px] font-extrabold uppercase tracking-[0.14em]"
          style={{ color: 'var(--theme-mode-text-muted)' }}
        >
          Select competition{inline ? '' : ` — ${activeCatLabel}`}
        </div>
        {!inline && (
          <button
            type="button"
            onClick={() => setPanelOpen(false)}
            className="cursor-pointer text-xs font-bold"
            style={{ color: 'var(--theme-mode-text-muted)' }}
          >
            ✕ Close
          </button>
        )}
      </div>
      <div className="mb-3.5 max-sm:hidden">{tabsRow}</div>
      <div className="mb-3.5 sm:hidden">{categoryRow}</div>
      <div className="mb-[18px] flex gap-2.5 max-sm:flex-col">
        {searchBox}
        {champToggle}
        {seasonSelect}
      </div>
      {listBody}
    </div>
  );

  // Мобильный top sheet («Change» на <640px): панель выезжает сверху вниз
  const sheet = createPortal(
    <div
      className="dv-sel-sheet-backdrop fixed inset-0 z-[130] flex items-start"
      style={{ background: 'rgba(10,14,20,0.5)' }}
      onClick={() => setPanelOpen(false)}
    >
      <div
        className="dv-sel-sheet flex w-full flex-col"
        style={{
          maxHeight: '88%',
          borderRadius: '0 0 22px 22px',
          background: 'var(--theme-mode-surface)',
          boxShadow: '0 18px 50px rgba(0,0,0,0.3)',
          paddingTop: 'env(safe-area-inset-top)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 pb-2.5 pt-3">
          <div className="text-base font-extrabold" style={{ color: 'var(--theme-mode-text)' }}>
            Select competition — {activeCatLabel}
          </div>
          <button
            type="button"
            onClick={() => setPanelOpen(false)}
            className="flex h-11 w-11 shrink-0 cursor-pointer items-center justify-center rounded-xl text-sm"
            style={{
              background: 'var(--theme-mode-input-bg)',
              color: 'var(--theme-mode-text-secondary)',
            }}
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        <div className="px-4 pb-3">{categoryRow}</div>
        <div className="flex flex-col gap-2 px-4 pb-3">
          {searchBox}
          {champToggle}
          {seasonSelect}
        </div>
        <div className="min-h-0 overflow-y-auto px-4 pb-3">{listBody}</div>

        {/* Ручка снизу top-sheet: свайп вверх → закрытие */}
        <div
          className="flex justify-center pb-2.5 pt-1"
          onTouchStart={(e) => {
            touchStartY.current = e.touches[0].clientY;
          }}
          onTouchMove={(e) => {
            if (
              touchStartY.current != null &&
              touchStartY.current - e.touches[0].clientY > 70
            ) {
              touchStartY.current = null;
              setPanelOpen(false);
            }
          }}
          onTouchEnd={() => {
            touchStartY.current = null;
          }}
        >
          <span
            className="h-1 w-10 rounded-[3px]"
            style={{ background: 'var(--theme-mode-border-input)' }}
          />
        </div>
      </div>
    </div>,
    document.body,
  );

  // Без выбора — селектор открыт inline (deep-link ?category=, frame 7a)
  if (!selectedSourceObj) return panel(true);

  // С выбором и кастомной шапкой (шапка соревнования) — встроенную не рисуем:
  // рендерим header из пропа, ниже — тот же дропдаун/шит выбора.
  if (renderHeader) {
    return (
      <div ref={wrapRef} className="relative">
        {renderHeader(() => setPanelOpen((v) => !v), panelOpen, selectedSourceObj)}
        {panelOpen && !isMobile && (
          <div
            className="dv-sel-panel-drop absolute left-0 right-0 z-20"
            style={{ top: 'calc(100% + 10px)', filter: 'drop-shadow(0 18px 30px rgba(0,0,0,0.16))' }}
          >
            {panel(false)}
          </div>
        )}
        {panelOpen && isMobile && sheet}
      </div>
    );
  }

  // С выбором — компактная шапка соревнования; «Change» открывает панель поверх контента
  const selectedDate = parseDate(selectedSourceObj.date);
  const seasonYear = selectedDate ? seasonStartYear(selectedDate) : undefined;
  // day_count в имя не подмешиваем: RTL-имена ломают порядок LTR-суффикса,
  // а многодневность и так видна по диапазону дат в headerMeta («15–16 Jul»).
  const headerName = selectedSourceObj.name;
  const headerMeta = [
    dateLabel(selectedSourceObj),
    selectedSourceObj.pool_type,
    monthLabel(selectedSourceObj),
    seasonYear ? `Season ${seasonLabel(seasonYear)}` : '',
  ]
    .filter(Boolean)
    .join(' · ');
  // Бейдж категории соревнования в шапке — «выбит» в акцентном фоне (--theme-mode-accent-text),
  // а не currentColor-обводка, как в табах: тут фон всегда сплошной --theme-primary,
  // так что можно уверенно закрасить кружок, а не только обвести.
  // Без канонической категории (null) берём бейдж первой кастомной из членства.
  const headerCategoryKey =
    selectedSourceObj.category ??
    selectedSourceObj.categories?.find((k) => liveCategoryLabels[k]?.badge);
  const headerCategoryBadge = headerCategoryKey
    ? liveCategoryLabels[headerCategoryKey]?.badge
    : undefined;

  return (
    <div ref={wrapRef} className="relative">
      {/* Desktop-шапка: одна строка ~72px */}
      <div
        className="hidden items-center justify-between gap-4 rounded-[14px] px-5 py-4 sm:flex md:rounded-t-none"
        style={{
          background: 'var(--theme-primary)',
          color: 'var(--theme-mode-accent-text)',
          boxShadow: 'var(--theme-mode-card-shadow)',
        }}
      >
        <div className="flex min-w-0 items-center gap-3.5">
          {headerCategoryBadge && (
            <span
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-lg font-extrabold leading-none"
              style={{ background: 'var(--theme-mode-accent-text)', color: 'var(--theme-primary)' }}
            >
              {headerCategoryBadge}
            </span>
          )}
          <div className="flex min-w-0 flex-col gap-[5px]">
            <div
              dir="rtl"
              className="overflow-hidden text-ellipsis whitespace-nowrap text-[19px] font-extrabold"
              style={{ textAlign: 'left' }}
            >
              {headerName}
            </div>
            <div className="text-[12.5px] font-semibold opacity-85">{headerMeta}</div>
          </div>
        </div>
        <button
          type="button"
          onClick={() => setPanelOpen((v) => !v)}
          className="flex min-h-[44px] shrink-0 cursor-pointer items-center gap-2 whitespace-nowrap rounded-[10px] px-[18px] text-[13px] font-extrabold"
          style={{
            background: 'rgba(255,255,255,0.16)',
            border: '1px solid var(--theme-mode-header-btn-border)',
            color: 'inherit',
          }}
        >
          Change <span className="text-[10px]">{panelOpen ? '▴' : '▾'}</span>
        </button>
      </div>

      {/* Мобильная шапка (<640px): две строки — имя + «⇄ Change», мета ниже */}
      <div
        className="flex flex-col gap-[9px] rounded-[16px] px-3.5 pb-3 pt-3.5 sm:hidden"
        style={{
          background: 'var(--theme-primary)',
          color: 'var(--theme-mode-accent-text)',
          boxShadow: 'var(--theme-mode-card-shadow)',
        }}
      >
        <div className="flex items-center justify-between gap-3">
          <div className="flex min-w-0 items-center gap-2.5">
            {headerCategoryBadge && (
              <span
                className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-base font-extrabold leading-none"
                style={{ background: 'var(--theme-mode-accent-text)', color: 'var(--theme-primary)' }}
              >
                {headerCategoryBadge}
              </span>
            )}
            <div
              dir="rtl"
              className="min-w-0 overflow-hidden text-ellipsis whitespace-nowrap text-[17px] font-extrabold"
              style={{ textAlign: 'left' }}
            >
              {headerName}
            </div>
          </div>
          <button
            type="button"
            onClick={() => setPanelOpen((v) => !v)}
            className="flex min-h-[44px] shrink-0 cursor-pointer items-center gap-1.5 whitespace-nowrap rounded-xl px-3.5 text-[13px] font-extrabold"
            style={{
              background: 'rgba(127,127,127,0.18)',
              border: '1px solid var(--theme-mode-header-btn-border)',
              color: 'inherit',
            }}
          >
            ⇄ Change
          </button>
        </div>
        <div className="text-xs font-semibold opacity-85">{headerMeta}</div>
      </div>

      {/* Desktop: дропдаун под шапкой; mobile: bottom sheet */}
      {panelOpen && !isMobile && (
        <div
          className="dv-sel-panel-drop absolute left-0 right-0 z-20"
          style={{ top: 'calc(100% + 10px)', filter: 'drop-shadow(0 18px 30px rgba(0,0,0,0.16))' }}
        >
          {panel(false)}
        </div>
      )}
      {panelOpen && isMobile && sheet}
    </div>
  );
};

export default DataSourceDDL;

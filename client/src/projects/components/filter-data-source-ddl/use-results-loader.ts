import React from 'react';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { Result } from '../../../utils/interfaces/results';
import { ResultsPaging } from '../../../store/store';
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
  sourceUrlParam,
} from '../../../utils/helpers/competition-source';

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

/**
 * Результаты выбранного источника → store (`dataSourceSelected`, `resultsPaging`,
 * `filterSelected`). Знает про оба режима загрузки и про ленивый Swims (`canLoadResults`):
 * пока гейт закрыт, источник только «праймится» в шапку, а результаты дотягиваются потом.
 *
 * Наружу — два входа: `requestSource` (выбор из списка: грузить сейчас или отложить) и
 * `loadDirect` (прямая ссылка на id, которого в списке нет).
 */
export const useResultsLoader = (canLoadResults: boolean) => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((s) => s.filterSelected);

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
  const requestSource = (src: CompetitionSource) => {
    if (canLoadResults) {
      void loadSource(src);
    } else {
      pendingSourceRef.current = src;
      primeSource(src);
    }
  };

  // Конкретный id вне списка (например, день события) — грузим напрямую.
  // Флаги датасета обязательны и здесь: без is_award медали соревнования не считаются.
  // category соревнования тут неизвестна заранее (нет в CompetitionSource-списке) —
  // Age-фильтр в paged-режиме мапится как non-masters (birthYear); край-кейс direct-link.
  const loadDirect = (sourceParams: Record<string, string>) => {
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
  };

  return { requestSource, loadDirect };
};

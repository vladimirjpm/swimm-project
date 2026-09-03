import { Result } from '../interfaces/results';
import { FilterSelected } from '../interfaces/filter-selected';
import { ResultsPaging } from '../../store/store';

/** Размер страницы в paged-режиме (контракт 3.2 §4). */
export const PAGED_PAGE_SIZE = 100;

/**
 * Маппинг клиентских фильтров в query-параметры /api/results (контракт 3.2 §2).
 * Значения «all»/пустые не передаются — чистые URL, кэш-ключи сервера.
 */
export function buildResultsFilterParams(
  filters: FilterSelected,
  isMasters: boolean,
): Record<string, string> {
  const params: Record<string, string> = {};

  if (filters.selected_name && filters.selected_name !== 'all') params.name = filters.selected_name;
  if (filters.club && filters.club !== 'all') params.club = filters.club;
  // Диплинк на заплывы одного пловца (?swimmerId=): в paged-режиме фильтрует сервер —
  // клиентский матчинг видит только загруженную страницу.
  if (filters.swimmer_id != null) params.swimmerId = String(filters.swimmer_id);
  if (filters.style_name) params.styleName = filters.style_name;
  if (filters.style_len) params.distance = String(filters.style_len);
  if (filters.gender && filters.gender !== 'all') params.gender = filters.gender;
  if (filters.pool_type && filters.pool_type !== 'all') params.poolType = filters.pool_type;

  if (filters.age && filters.age !== 'all') {
    // masters: age — строка группы («25-29»); обычный режим: age — год рождения (одиночный или диапазон).
    if (isMasters) {
      params.ageGroup = filters.age;
    } else {
      params.birthYearFrom = filters.age;
      params.birthYearTo = filters.age_to || filters.age;
    }
  }

  if (filters.position_filter && filters.position_filter !== 'all') {
    params.position = filters.position_filter;
  }

  if (filters.event_date && filters.event_date !== 'all') params.eventDate = filters.event_date;

  return params;
}

export interface ResultsPageResponse {
  results: Result[];
  paging: ResultsPaging;
}

/** Один fetch страницы /api/results (paged-режим — без цикла while hasMore). */
export async function fetchResultsPage(
  sourceParams: Record<string, string>,
  filterParams: Record<string, string>,
  page: number,
  pageSize: number,
): Promise<ResultsPageResponse> {
  const qs = new URLSearchParams({
    ...sourceParams,
    ...filterParams,
    page: String(page),
    pageSize: String(pageSize),
  });
  const resp = await fetch(`/api/results?${qs}`, { credentials: 'same-origin' });
  if (!resp.ok) throw new Error(`API /api/results returned ${resp.status}`);
  const json = await resp.json();
  return {
    results: (json.data ?? []) as Result[],
    paging: {
      page: json.page,
      pageSize: json.pageSize,
      total: json.total,
      hasMore: json.hasMore,
    },
  };
}

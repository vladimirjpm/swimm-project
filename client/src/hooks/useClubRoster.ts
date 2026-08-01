import { useEffect, useState } from 'react';

/**
 * Ростер клуба — GET /api/clubs/{id}/roster?page=&pageSize=&gender=&ageFrom=&ageTo=&season=.
 * Отдельный от useClubOverview эндпоинт (K4.2, docs/plans/club-page-plan.md): своя пагинация
 * («Show all N» догружает следующую страницу) и свои фильтры — возраст здесь считается от
 * BirthYear, это НЕ зачётная группа Category (см. CARDS.md §6 / club-page-cards-sonnet.md §4.4).
 */

export interface ClubRosterItem {
  swimmer_id: number;
  last_name: string;
  first_name: string;
  last_name_en: string;
  first_name_en: string;
  birth_year: number;
  age: number;
  gender: string | null;
  competitions: number;
  swims: number;
}

interface ClubRosterPageResponse {
  page: number;
  page_size: number;
  total: number;
  has_more: boolean;
  data: ClubRosterItem[];
}

export interface ClubRosterFilters {
  /** 'male' | 'female' | null (без фильтра). */
  gender: 'male' | 'female' | null;
  ageFrom: number | null;
  ageTo: number | null;
  /** Год начала сезона; null — без ограничения по сезону. */
  season: number | null;
}

export interface UseClubRosterResult {
  items: ClubRosterItem[];
  total: number;
  hasMore: boolean;
  loading: boolean;
  error: string | null;
  /** Догружает следующую страницу поверх уже загруженных строк («Show all N»). */
  loadMore: () => void;
}

const PAGE_SIZE = 25;

export function useClubRoster(clubId: number | null, filters: ClubRosterFilters): UseClubRosterResult {
  const { gender, ageFrom, ageTo, season } = filters;
  const [items, setItems] = useState<ClubRosterItem[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState<boolean>(clubId != null);
  const [error, setError] = useState<string | null>(null);

  // Смена скоупа/фильтров — новая выборка с первой страницы, старые строки не годятся
  // (другой фильтр = другой список, не хвост того же).
  useEffect(() => {
    setPage(1);
  }, [clubId, gender, ageFrom, ageTo, season]);

  useEffect(() => {
    if (clubId == null) {
      setItems([]);
      setTotal(0);
      setHasMore(false);
      setLoading(false);
      return;
    }

    const qs = new URLSearchParams();
    qs.set('page', String(page));
    qs.set('pageSize', String(PAGE_SIZE));
    if (gender != null) qs.set('gender', gender);
    if (ageFrom != null) qs.set('ageFrom', String(ageFrom));
    if (ageTo != null) qs.set('ageTo', String(ageTo));
    if (season != null) qs.set('season', String(season));

    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(`/api/clubs/${clubId}/roster?${qs.toString()}`)
      .then((res) => {
        if (!res.ok) throw new Error(`http-${res.status}`);
        return res.json() as Promise<ClubRosterPageResponse>;
      })
      .then((json) => {
        if (cancelled) return;
        setItems((prev) => (json.page === 1 ? json.data : [...prev, ...json.data]));
        setTotal(json.total);
        setHasMore(json.has_more);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [clubId, page, gender, ageFrom, ageTo, season]);

  return {
    items,
    total,
    hasMore,
    loading,
    error,
    loadMore: () => setPage((p) => p + 1),
  };
}

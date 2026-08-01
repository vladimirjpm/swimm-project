import { useEffect, useState } from 'react';

/**
 * Клубные рекорды — GET /api/clubs/{id}/records?pool=. Ось стиль × дистанция × бассейн × пол
 * (K4.2, docs/plans/club-page-plan.md); 25м и 50м физически несравнимы, поэтому единственный
 * локальный фильтр страницы клуба (CARDS.md §5) живёт тут, а не в глобальном скоупе.
 */

export interface ClubRecordItem {
  style_name: string;
  distance: string;
  pool_type: string | null;
  gender: string;
  time_original: string;
  time_ms: number | null;
  swimmer_id: number;
  swimmer_name: string;
  swimmer_name_en: string;
  competition_name: string;
  /** dd/MM/yyyy. */
  date: string;
  points: number;
}

interface ClubRecordsResponse {
  data: ClubRecordItem[];
}

export interface UseClubRecordsResult {
  data: ClubRecordItem[];
  loading: boolean;
  error: string | null;
}

export function useClubRecords(clubId: number | null, pool: '25m' | '50m' | null): UseClubRecordsResult {
  const [data, setData] = useState<ClubRecordItem[]>([]);
  const [loading, setLoading] = useState<boolean>(clubId != null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (clubId == null) {
      setData([]);
      setLoading(false);
      return;
    }

    const qs = pool ? `?pool=${pool}` : '';
    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(`/api/clubs/${clubId}/records${qs}`)
      .then((res) => {
        if (!res.ok) throw new Error(`http-${res.status}`);
        return res.json() as Promise<ClubRecordsResponse>;
      })
      .then((json) => {
        if (!cancelled) setData(json.data);
      })
      .catch((e: Error) => {
        if (!cancelled) {
          setData([]);
          setError(e.message);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [clubId, pool]);

  return { data, loading, error };
}

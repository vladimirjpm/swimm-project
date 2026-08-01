import { useEffect, useState } from 'react';

/**
 * Стена ОФИЦИАЛЬНЫХ рекордов клуба — GET /api/clubs/{id}/record-wall?pool=.
 *
 * Это справочник `Records` (импорт с isr.org.il и World Aquatics): национальные,
 * возрастные, мастерс и мировые рекорды, которые числятся за клубом. Не путать с
 * `useClubRecords` — тот считает лучшие времена клуба по НАШИМ протоколам («Season best»).
 *
 * ⚠ У рекорда нет ни SwimmerId, ни ClubId — только текстовые имена, поэтому ссылки на
 * карточку пловца тут нет, а возраст держателя известен только ступенью (`age_key`).
 */

export interface ClubOfficialRecord {
  /** world | continent | country. */
  region_type: string;
  region_code: string;
  /** open | age | masters. */
  category: string;
  /** «10».. «18» / «adults» / «25-29»…; пусто для open. */
  age_key: string;
  gender: string;
  pool_type: string;
  style: string;
  /** С суффиксом «m»: «100m», «4X50m». */
  distance: string;
  /** Строкой как в источнике — миллисекунд в Records нет. */
  time: string;
  holder_name: string;
  club: string;
  record_date: string;
}

interface ClubRecordWallResponse {
  matched_names: string[];
  data: ClubOfficialRecord[];
}

export interface UseClubRecordWallResult {
  data: ClubOfficialRecord[];
  matchedNames: string[];
  loading: boolean;
  error: string | null;
}

export function useClubRecordWall(
  clubId: number | null,
  pool: '25m' | '50m' | null,
): UseClubRecordWallResult {
  const [data, setData] = useState<ClubOfficialRecord[]>([]);
  const [matchedNames, setMatchedNames] = useState<string[]>([]);
  const [loading, setLoading] = useState<boolean>(clubId != null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (clubId == null) {
      setData([]);
      setMatchedNames([]);
      setLoading(false);
      return;
    }

    const qs = pool ? `?pool=${pool}` : '';
    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(`/api/clubs/${clubId}/record-wall${qs}`)
      .then((res) => {
        if (!res.ok) throw new Error(`http-${res.status}`);
        return res.json() as Promise<ClubRecordWallResponse>;
      })
      .then((json) => {
        if (!cancelled) {
          setData(json.data);
          setMatchedNames(json.matched_names ?? []);
        }
      })
      .catch((e: Error) => {
        if (!cancelled) {
          setData([]);
          setMatchedNames([]);
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

  return { data, matchedNames, loading, error };
}

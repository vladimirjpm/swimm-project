import { useState, useEffect, useCallback, useRef } from 'react';
import { AllUserMediaDto } from './use-all-my-media';

// ── Types — DTO GET /api/me/swims (My media v3, swim-centric) ────────────────

/** Медиа в контексте страницы swims — тот же UserMediaDto + реакции. */
export interface SwimMediaDto extends AllUserMediaDto {
  likes_count: number;
  my_like: boolean;
}

export interface MySwimmerDto {
  id: number;
  name: string;
  is_primary: boolean;
}

export interface MySwimDto {
  result_id: number;
  swimmer_id: number;
  competition_id: number;
  competition_name: string;
  /** dd/MM/yyyy */
  competition_date: string;
  pool_type: string;
  /** yyyy-MM-dd — день заплыва (многодневные) */
  date: string;
  distance: string;
  style: string;
  style_id: number;
  is_relay: boolean;
  /** SwimmerId всех ног эстафеты — для чип-фильтра/счётчиков (эстафета принадлежит всем). */
  member_swimmer_ids: number[];
  place: number | null;
  points: number;
  time: string;
  /** Ошибка протокола (И11). null — заплыв в порядке. */
  suspect_reason?: string | null;
  time_fail: boolean;
  is_pb: boolean;
  congrats_count: number;
  my_cheer: boolean;
  media: SwimMediaDto[];
}

export interface MySwimsResponse {
  swimmers: MySwimmerDto[];
  /** Стартовые годы сезонов (сентябрь–август), по убыванию. */
  seasons: number[];
  season: number;
  swims: MySwimDto[];
  competition_media: SwimMediaDto[];
  unlinked_media: SwimMediaDto[];
}

const EMPTY: MySwimsResponse = {
  swimmers: [], seasons: [], season: 0, swims: [], competition_media: [], unlinked_media: [],
};

export { seasonLabel } from '../../utils/helpers/season-helper';

/**
 * Заплывы favorite-пловцов за сезон + медиа + реакции — GET /api/me/swims.
 * season=null → сервер берёт текущий; reload после add/remove медиа —
 * агрегат дешёвый, точечный merge не оправдан.
 */
export function useMySwims(season: number | null) {
  const [data, setData] = useState<MySwimsResponse>(EMPTY);
  const [loading, setLoading] = useState(true);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const url = season != null ? `/api/me/swims?season=${season}` : '/api/me/swims';
      const r = await fetch(url, { credentials: 'include' });
      if (!r.ok) { if (mountedRef.current) { setData(EMPTY); setLoading(false); } return; }
      const d: MySwimsResponse = await r.json();
      if (mountedRef.current) { setData(d); setLoading(false); }
    } catch {
      if (mountedRef.current) { setData(EMPTY); setLoading(false); }
    }
  }, [season]);

  useEffect(() => { load(); }, [load]);

  return { data, loading, reload: load };
}

// ── Реакции: идемпотентные тогглы (сервер отвечает итогом {count, mine}) ─────

let cachedToken: string | null = null;

async function antiforgeryToken(): Promise<string | null> {
  if (cachedToken) return cachedToken;
  try {
    const r = await fetch('/api/antiforgery/token', { credentials: 'include' });
    if (!r.ok) return null;
    cachedToken = (await r.json()).token ?? null;
    return cachedToken;
  } catch {
    return null;
  }
}

async function toggleReaction(url: string, on: boolean): Promise<{ count: number; mine: boolean } | null> {
  const token = await antiforgeryToken();
  if (!token) return null;
  try {
    const r = await fetch(url, {
      method: on ? 'POST' : 'DELETE',
      credentials: 'include',
      headers: { 'X-XSRF-TOKEN': token },
    });
    if (!r.ok) { cachedToken = null; return null; }
    return await r.json();
  } catch {
    cachedToken = null;
    return null;
  }
}

export const toggleLike = (mediaId: number, on: boolean) => toggleReaction(`/api/media/${mediaId}/like`, on);
export const toggleCheer = (resultId: number, on: boolean) => toggleReaction(`/api/results/${resultId}/cheer`, on);

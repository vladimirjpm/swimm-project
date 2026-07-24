import { useState, useEffect, useCallback, useRef } from 'react';

// ── Types ────────────────────────────────────────────────────────────────────

/** Одна запись «всего медиа юзера» — DTO GET /api/me/media (v2, страница My media). */
export interface AllUserMediaDto {
  id: number;
  swimmer_id: number;
  swimmer_name: string;
  level: 'swimmer' | 'competition' | 'result';
  media_type: 'image' | 'video';
  source_type: 'youtube' | 'vimeo' | 'other';
  url: string;
  result_id?: number | null;
  result_label?: string | null;
  competition_id?: number | null;
  competition_name?: string | null;
  /** dd/MM/yyyy, как отдаёт сервер */
  competition_date?: string | null;
  club_name?: string | null;
  created_at: string;
}

export interface AddMediaInput {
  swimmer_id: number;
  media_type: 'image' | 'video';
  source_type: 'youtube' | 'vimeo' | 'other';
  url: string;
  result_id?: number | null;
  /** Привязка ко всему соревнованию (📎 в шапке группы); игнорируется, если задан result_id. */
  competition_id?: number | null;
}

export interface PublishTargetDto {
  id: number;
  name: string;
}

// ── Antiforgery token cache (та же механика, что и useUserMedia/useFavorites) ──

let cachedToken: string | null = null;

async function fetchAntiforgeryToken(): Promise<string | null> {
  if (cachedToken) return cachedToken;
  try {
    const r = await fetch('/api/antiforgery/token', { credentials: 'include' });
    if (!r.ok) return null;
    const data = await r.json();
    cachedToken = data.token ?? null;
    return cachedToken;
  } catch {
    return null;
  }
}

function invalidateTokenCache() {
  cachedToken = null;
}

/**
 * Standalone POST /api/me/media — вынесено из useAllMyMedia.add, чтобы можно было
 * дёргать addUserMedia() и без всего хука (напр. со страницы результатов, где грузить
 * всё медиа юзера не нужно). Хук использует эту же функцию и сам обновляет свой стейт.
 */
export async function addUserMedia(input: AddMediaInput): Promise<AllUserMediaDto | null> {
  const token = await fetchAntiforgeryToken();
  if (!token) return null;

  try {
    const r = await fetch('/api/me/media', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
      body: JSON.stringify(input),
    });

    if (!r.ok) { invalidateTokenCache(); return null; }
    return await r.json();
  } catch {
    invalidateTokenCache();
    return null;
  }
}

/** Год из dd/MM/yyyy — сезон карточки для фильтра Season. Без даты → null (только "All"). */
export function seasonFromCompetitionDate(date?: string | null): string | null {
  if (!date) return null;
  const parts = date.split('/');
  if (parts.length !== 3) return null;
  const year = parts[2];
  return /^\d{4}$/.test(year) ? year : null;
}

/**
 * Всё медиа залогиненного юзера по всем его пловцам (страница «My media»).
 * Лёгкий хук по образцу useUserMedia — тут не нужен swimmerId и add() для
 * конкретного пловца: список + удаление + общий add (Add link флоу, шаг 3).
 * Публикациями заведует useMyMediaPublications (реиспользуется как есть).
 */
export function useAllMyMedia() {
  const [media, setMedia] = useState<AllUserMediaDto[]>([]);
  const [loading, setLoading] = useState(true);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const r = await fetch('/api/me/media', { credentials: 'include' });
      if (!r.ok) { if (mountedRef.current) { setMedia([]); setLoading(false); } return; }
      const list: AllUserMediaDto[] = await r.json();
      if (mountedRef.current) { setMedia(list); setLoading(false); }
    } catch {
      if (mountedRef.current) { setMedia([]); setLoading(false); }
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const remove = useCallback(async (id: number): Promise<boolean> => {
    const token = await fetchAntiforgeryToken();
    if (!token) return false;

    try {
      const r = await fetch(`/api/me/media/${id}`, {
        method: 'DELETE',
        credentials: 'include',
        headers: { 'X-XSRF-TOKEN': token },
      });

      if (!r.ok) { invalidateTokenCache(); return false; }

      if (mountedRef.current) {
        setMedia(prev => prev.filter(m => m.id !== id));
      }
      return true;
    } catch {
      invalidateTokenCache();
      return false;
    }
  }, []);

  /** Add link, шаг 3: POST /api/me/media — новый элемент попадает в начало сетки. */
  const add = useCallback(async (input: AddMediaInput): Promise<AllUserMediaDto | null> => {
    const item = await addUserMedia(input);
    if (item && mountedRef.current) {
      setMedia(prev => [item, ...prev]);
    }
    return item;
  }, []);

  return { media, loading, remove, add, reload: load };
}

// ── Пикер заплывов (Add link, шаг 3 / «Link to a swim») ────────────────────────

export interface SwimmerCompetitionBrief {
  id: number;
  name: string;
  date: string;
}

export interface SwimmerResultBrief {
  result_id: number;
  distance: string;
  style: string;
  time: string;
  date: string;
}

export async function fetchSwimmerCompetitionsBrief(swimmerId: number): Promise<SwimmerCompetitionBrief[]> {
  try {
    const r = await fetch(`/api/swimmers/${swimmerId}/competitions-brief`, { credentials: 'include' });
    return r.ok ? await r.json() : [];
  } catch {
    return [];
  }
}

export async function fetchSwimmerResultsBrief(swimmerId: number, competitionId: number): Promise<SwimmerResultBrief[]> {
  try {
    const r = await fetch(`/api/swimmers/${swimmerId}/results-brief?competitionId=${competitionId}`, { credentials: 'include' });
    return r.ok ? await r.json() : [];
  } catch {
    return [];
  }
}

/** Куда можно подать выбранное медиа — GET /api/me/media/{id}/publish-targets. */
export async function fetchPublishTargets(mediaId: number): Promise<PublishTargetDto[]> {
  try {
    const r = await fetch(`/api/me/media/${mediaId}/publish-targets`, { credentials: 'include' });
    return r.ok ? await r.json() : [];
  } catch {
    return [];
  }
}


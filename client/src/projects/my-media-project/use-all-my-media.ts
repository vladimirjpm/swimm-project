import { useState, useEffect, useCallback, useRef } from 'react';

// ── Types ────────────────────────────────────────────────────────────────────

/** Одна запись «всего медиа юзера» — DTO GET /api/me/media (без swimmerId). */
export interface AllUserMediaDto {
  id: number;
  swimmer_id: number;
  level: 'swimmer' | 'competition' | 'result';
  media_type: 'image' | 'video';
  source_type: 'youtube' | 'vimeo' | 'other';
  url: string;
  result_id?: number | null;
  competition_id?: number | null;
  created_at: string;
  swimmer_name: string;
  result_label?: string | null;
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
 * Всё медиа залогиненного юзера по всем его пловцам (страница «My media»).
 * Лёгкий хук по образцу useUserMedia — тут не нужен swimmerId и add(), только
 * список + удаление записи. Публикациями заведует useMyMediaPublications (реиспользуется).
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

  return { media, loading, remove, reload: load };
}

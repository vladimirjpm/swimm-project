import { useState, useEffect, useCallback, useRef } from 'react';

// ── Types ────────────────────────────────────────────────────────────────────

export interface UserMediaDto {
  id: number;
  swimmer_id: number;
  level: 'swimmer' | 'competition' | 'result';
  media_type: 'image' | 'video';
  source_type: 'youtube' | 'vimeo' | 'other';
  url: string;
  result_id?: number | null;
  competition_id?: number | null;
  created_at: string;
}

export interface AddUserMediaInput {
  swimmer_id: number;
  media_type: 'image' | 'video';
  source_type: 'youtube' | 'vimeo' | 'other';
  url: string;
  result_id?: number | null;
  competition_id?: number | null;
}

// ── Antiforgery token cache (та же механика, что и useFavorites) ───────────────

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

// ── Hook ─────────────────────────────────────────────────────────────────────

/**
 * Личное owner-only медиа пловца (2A). Загружает список медиа по конкретному
 * swimmerId залогиненного юзера, даёт add/remove с оптимистичным обновлением
 * локального состояния. Публичного слоя нет — только /api/me/media.
 */
export function useUserMedia(swimmerId: number | null | undefined) {
  const [media, setMedia] = useState<UserMediaDto[]>([]);
  const [loading, setLoading] = useState(true);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const load = useCallback(async () => {
    if (swimmerId == null) {
      if (mountedRef.current) { setMedia([]); setLoading(false); }
      return;
    }
    setLoading(true);
    try {
      const r = await fetch(`/api/me/media?swimmerId=${swimmerId}`, { credentials: 'include' });
      if (!r.ok) { if (mountedRef.current) { setMedia([]); setLoading(false); } return; }
      const list: UserMediaDto[] = await r.json();
      if (mountedRef.current) { setMedia(list); setLoading(false); }
    } catch {
      if (mountedRef.current) { setMedia([]); setLoading(false); }
    }
  }, [swimmerId]);

  useEffect(() => {
    load();
  }, [load]);

  const add = useCallback(async (input: AddUserMediaInput): Promise<UserMediaDto | null> => {
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

      const item: UserMediaDto = await r.json();
      if (mountedRef.current) {
        setMedia(prev => [item, ...prev]);
      }
      return item;
    } catch {
      invalidateTokenCache();
      return null;
    }
  }, []);

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

  return { media, loading, add, remove, reload: load };
}

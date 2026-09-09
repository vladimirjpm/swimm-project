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

/**
 * Куда подана публикация. Клуб и группа — два вида одного (коллектив пловцов), поэтому у
 * цели есть ТИП, а не отдельные поля под каждую сущность.
 */
export type PublishTargetType = 'group' | 'club';

/** Ключ цели: тип + id. Ходит парой везде — в подаче, отзыве и ключах селектов. */
export interface PublishTargetRef {
  type: PublishTargetType;
  id: number;
}

/** Ключ строкой — для `value` у `<select>` и для сравнения целей. */
export const targetKey = (t: { type: PublishTargetType; id: number }): string => `${t.type}:${t.id}`;

/** Разбор ключа обратно; мусор → null. */
export function parseTargetKey(raw: string): PublishTargetRef | null {
  const [type, id] = raw.split(':');
  const n = Number(id);
  return (type === 'group' || type === 'club') && Number.isFinite(n) && n > 0
    ? { type, id: n }
    : null;
}

export interface UserMediaPublicationDto {
  id: number;
  user_media_id: number;
  target_type: PublishTargetType;
  target_id: number;
  target_name: string;
  level: 'members' | 'public';
  status: 'pending' | 'approved' | 'rejected';
  created_at: string;
  decided_at?: string | null;
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

/**
 * Публикации моих медиа в группы (этап 3): статусы заявок + подать/отозвать.
 * Данные общие на все медиа юзера — фильтруй по user_media_id на месте использования.
 */
export function useMyMediaPublications() {
  const [publications, setPublications] = useState<UserMediaPublicationDto[]>([]);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const load = useCallback(async () => {
    try {
      const r = await fetch('/api/me/media/publications', { credentials: 'include' });
      if (!r.ok) { if (mountedRef.current) setPublications([]); return; }
      const list: UserMediaPublicationDto[] = await r.json();
      if (mountedRef.current) setPublications(list);
    } catch {
      if (mountedRef.current) setPublications([]);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const submit = useCallback(async (
    mediaId: number, target: PublishTargetRef, level: 'members' | 'public'
  ): Promise<{ ok: boolean; error?: string }> => {
    const token = await fetchAntiforgeryToken();
    if (!token) return { ok: false, error: 'no token' };
    try {
      const r = await fetch(`/api/me/media/${mediaId}/publications`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
        body: JSON.stringify({ target_type: target.type, target_id: target.id, level }),
      });
      if (!r.ok) {
        invalidateTokenCache();
        const data = await r.json().catch(() => ({}));
        return { ok: false, error: data.error };
      }
      await load();
      return { ok: true };
    } catch {
      invalidateTokenCache();
      return { ok: false };
    }
  }, [load]);

  const withdraw = useCallback(async (mediaId: number, target: PublishTargetRef): Promise<boolean> => {
    const token = await fetchAntiforgeryToken();
    if (!token) return false;
    try {
      const r = await fetch(`/api/me/media/${mediaId}/publications/${target.type}/${target.id}`, {
        method: 'DELETE',
        credentials: 'include',
        headers: { 'X-XSRF-TOKEN': token },
      });
      if (!r.ok) { invalidateTokenCache(); return false; }
      if (mountedRef.current) {
        setPublications(prev => prev.filter(
          p => !(p.user_media_id === mediaId && p.target_type === target.type && p.target_id === target.id)));
      }
      return true;
    } catch {
      invalidateTokenCache();
      return false;
    }
  }, []);

  return { publications, submit, withdraw, reload: load };
}

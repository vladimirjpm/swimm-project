import { useState, useEffect, useCallback, useRef } from 'react';

// ── Types ────────────────────────────────────────────────────────────────────

/** Строка сводного inbox'а модерации — GET /api/me/moderation/media (все статусы, pending первыми). */
export interface ModerationRowDto {
  id: number;
  /** group | club — от типа зависит и ручка решения, и кто вправе решать. */
  target_type: 'group' | 'club';
  target_id: number;
  target_name: string;
  owner_email: string;
  swimmer_name: string;
  result_label?: string | null;
  url: string;
  media_type: 'image' | 'video';
  source_type: 'youtube' | 'vimeo' | 'other';
  level: 'members' | 'public';
  status: 'pending' | 'approved' | 'rejected';
  created_at: string;
}

// ── Antiforgery token cache ──────────────────────────────────────────────────

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
 * Сводный inbox модерации по всему, что я модерирую, — таб «Moderation».
 *
 * Цель бывает двух видов, и ручка решения у них своя: у группы
 * POST /api/hub-groups/{id}/media/publications/{pubId}/decision (решает владелец/админ
 * группы), у клуба POST /api/clubs/{id}/media/publications/{pubId}/decision (управляющих у
 * клуба нет, решает админ сайта — план §3.10).
 */
export function useMyMediaModeration(enabled: boolean) {
  const [rows, setRows] = useState<ModerationRowDto[]>([]);
  const [loading, setLoading] = useState(true);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const load = useCallback(async () => {
    if (!enabled) { setRows([]); setLoading(false); return; }
    setLoading(true);
    try {
      const r = await fetch('/api/me/moderation/media', { credentials: 'include' });
      if (!r.ok) { if (mountedRef.current) { setRows([]); setLoading(false); } return; }
      const list: ModerationRowDto[] = await r.json();
      if (mountedRef.current) { setRows(list); setLoading(false); }
    } catch {
      if (mountedRef.current) { setRows([]); setLoading(false); }
    }
  }, [enabled]);

  useEffect(() => { load(); }, [load]);

  /** Publish/Reject/Unpublish — оптимистично убираем строку из текущего представления вызывающей стороны. */
  const decide = useCallback(async (
    targetType: 'group' | 'club', targetId: number, publicationId: number, approve: boolean,
  ): Promise<boolean> => {
    const token = await fetchAntiforgeryToken();
    if (!token) return false;
    const base = targetType === 'club' ? `/api/clubs/${targetId}` : `/api/hub-groups/${targetId}`;
    try {
      const r = await fetch(`${base}/media/publications/${publicationId}/decision`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
        body: JSON.stringify({ approve }),
      });
      if (!r.ok) { invalidateTokenCache(); return false; }
      if (mountedRef.current) {
        setRows(prev => prev.map(row =>
          row.id === publicationId ? { ...row, status: approve ? 'approved' : 'rejected' } : row
        ));
      }
      return true;
    } catch {
      invalidateTokenCache();
      return false;
    }
  }, []);

  return { rows, loading, decide, reload: load };
}

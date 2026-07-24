import { useState, useEffect, useCallback, useRef } from 'react';

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

// ── Извлечение ID из вставленной строки (анти-SSRF: на сервер уходит только число) ─

/**
 * Извлекает числовой loglig ID из ссылки на карточку игрока (`Players/Details/{id}`)
 * либо принимает голое число. Всё остальное — null (клиентская ошибка без запроса на сервер).
 */
export function extractLogligId(input: string): number | null {
  const trimmed = input.trim();
  if (!trimmed) return null;

  const linkMatch = trimmed.match(/Players\/Details\/(\d+)/);
  if (linkMatch) return Number(linkMatch[1]);

  if (/^\d+$/.test(trimmed)) return Number(trimmed);

  return null;
}

// ── Hook ─────────────────────────────────────────────────────────────────────

/**
 * Статус краудсорс-привязки Loglig ID (docs/loglig-id-plan.md, шаг 6, хвост) для пловца:
 * GET-статус при маунте + предложение привязки (suggest сам извлекает ID из строки).
 */
export function useLogligStatus(swimmerId: number | null | undefined) {
  const [status, setStatus] = useState<string | null>(null);
  // Ссылка на публичную карточку (с сезоном) приходит только при Verified — собирает сервер.
  const [profileUrl, setProfileUrl] = useState<string | null>(null);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const refresh = useCallback(async () => {
    if (swimmerId == null) {
      if (mountedRef.current) { setStatus(null); setProfileUrl(null); }
      return;
    }
    try {
      const r = await fetch(`/api/swimmers/${swimmerId}/loglig-status`, { credentials: 'include' });
      if (!r.ok) { if (mountedRef.current) { setStatus(null); setProfileUrl(null); } return; }
      const data = await r.json();
      if (mountedRef.current) {
        setStatus(data.status ?? null);
        setProfileUrl(typeof data.profileUrl === 'string' ? data.profileUrl : null);
      }
    } catch {
      if (mountedRef.current) { setStatus(null); setProfileUrl(null); }
    }
  }, [swimmerId]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const suggest = useCallback(async (input: string): Promise<{ ok: boolean; error?: string }> => {
    if (swimmerId == null) return { ok: false, error: 'Swimmer not found' };

    const logligId = extractLogligId(input);
    if (logligId == null) return { ok: false, error: "Doesn't look like a loglig player card link" };

    const token = await fetchAntiforgeryToken();
    if (!token) return { ok: false, error: 'Could not get a token — please try again' };

    try {
      const r = await fetch(`/api/swimmers/${swimmerId}/loglig-suggest`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
        body: JSON.stringify({ logligId }),
      });

      if (!r.ok) {
        invalidateTokenCache();
        const data = await r.json().catch(() => ({}));
        return { ok: false, error: data.error ?? 'Failed to submit suggestion' };
      }

      if (mountedRef.current) setStatus('Suggested');
      return { ok: true };
    } catch {
      invalidateTokenCache();
      return { ok: false, error: 'Failed to submit suggestion' };
    }
  }, [swimmerId]);

  return { status, profileUrl, refresh, suggest };
}

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

/**
 * «Группа только следит; публичное — только Trusted» (Р65, docs/data-integrity.md). У недоверенной
 * группы уровня «Everyone 🌐» нет — только Members. Один текст на все места, где выбирают или
 * одобряют уровень: подсказка у погашенного пункта, строка в inbox-е, карточка в табе Admin.
 */
export const EVERYONE_TRUSTED_ONLY =
  '“Everyone 🌐” is only for Trusted groups — here photos and videos are shared with group members.';

/** Статус доверенной группы — зелёная строка в табе Admin (Р56). */
export const TRUSTED_NOTE =
  '✓ Trusted — this group can share photos and videos with “Everyone 🌐”: they show in results and on swimmer pages.';

/** Подсказка «как получить Trusted» — для таба Admin группы. */
export const TRUSTED_HOW_TO =
  'Ask the site admin to mark the group Trusted. Your club’s official group is trusted automatically.';

/**
 * Можно ли выбрать «Everyone 🌐» для этой цели (Р65): клуб — всегда (решает админ сайта), группа —
 * только доверенная. Цель не выбрана или признак не пришёл — не гасим: окончательно решает сервер.
 */
export const canShareWithEveryone = (
  target: { type: PublishTargetType; trusted?: boolean } | null | undefined,
): boolean => target == null || target.type === 'club' || target.trusted !== false;

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

// ── Жалоба «Report» на чужое медиа (Р62) ─────────────────────────────────────

/** Коды причин — зеркало MediaReportRules.Reasons на сервере; подписи — здесь (UI на английском). */
export const MEDIA_REPORT_REASONS = [
  { code: 'wrong_swimmer', label: 'Wrong swimmer' },
  { code: 'inappropriate', label: 'Inappropriate content' },
  { code: 'spam', label: 'Spam or advertising' },
  { code: 'privacy', label: 'Shouldn’t be public (privacy)' },
  { code: 'other', label: 'Other' },
] as const;

export type MediaReportReason = (typeof MEDIA_REPORT_REASONS)[number]['code'];

/**
 * Строка для тренера / модератора группы: состояние медиа и открытые жалобы — только причины и
 * число, без имён (Р62: кто пожаловался, видит лишь админ сайта). null — сказать нечего.
 * Пример: «Hidden — under review · 3 reports: Wrong swimmer ×2, Other ×1».
 */
export function mediaReportsSummary(
  state: 'under_review' | 'removed' | null | undefined,
  openReports: Record<string, number> | null | undefined,
): string | null {
  const parts: string[] = [];
  if (state === 'under_review') parts.push('Hidden — under review');
  if (state === 'removed') parts.push('Removed by the site admin');
  const entries = Object.entries(openReports ?? {}).filter(([, n]) => n > 0);
  if (entries.length > 0) {
    const total = entries.reduce((sum, [, n]) => sum + n, 0);
    const label = (code: string) => MEDIA_REPORT_REASONS.find((r) => r.code === code)?.label ?? code;
    parts.push(`${total} report${total === 1 ? '' : 's'}: ${entries.map(([c, n]) => `${label(c)} ×${n}`).join(', ')}`);
  }
  return parts.length > 0 ? `⚑ ${parts.join(' · ')}` : null;
}

/** Предел текста «Other» — зеркало MediaReportRules.MaxCommentLength. */
export const MEDIA_REPORT_MAX_COMMENT = 500;

/**
 * POST /api/media/{id}/report. Ответ не говорит, спрятано ли медиа: «спасибо» одинаковое.
 * Ошибка — текст с сервера ({ error }) или общий.
 */
export async function reportMedia(
  mediaId: number, reason: MediaReportReason, comment: string,
): Promise<{ ok: true; alreadyReported: boolean } | { ok: false; error: string }> {
  const token = await fetchAntiforgeryToken();
  if (!token) return { ok: false, error: 'Sign in to report' };
  try {
    const r = await fetch(`/api/media/${mediaId}/report`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
      body: JSON.stringify({ reason, comment: comment.trim() || null }),
    });
    if (r.status === 401 || r.status === 403) invalidateTokenCache();
    if (r.status === 429) return { ok: false, error: 'Too many reports — try again in a minute' };
    const data = await r.json().catch(() => ({}));
    if (!r.ok) return { ok: false, error: data?.error ?? 'Could not send the report' };
    return { ok: true, alreadyReported: Boolean(data?.already_reported) };
  } catch {
    return { ok: false, error: 'Could not send the report' };
  }
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

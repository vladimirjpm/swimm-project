import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../hooks/useAuth';
import type {
  ClubOption,
  ClubRequest,
  CreateEligibility,
  HubGroupClubRequestInput,
  HubGroupEditData,
  HubGroupInput,
  HubGroupAdmin,
  HubGroupMediaInput,
  JoinedHubGroup,
  MyHubGroupRow,
  SaveResult,
  SwimmerResultOption,
  SwimmerSearchResult,
  TrainingOption,
} from './my-groups-types';
import type { HubGroupMediaItem } from '../../utils/interfaces/results';
import type { HubGroupMemberMediaItem } from './types';

// Тот же паттерн antiforgery-токена, что и в hooks/useFavorites.ts.
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

async function apiFetch(url: string, init?: RequestInit): Promise<Response> {
  const method = (init?.method ?? 'GET').toUpperCase();
  if (method === 'GET') return fetch(url, { credentials: 'include', ...init });

  const token = await fetchAntiforgeryToken();
  const headers: Record<string, string> = { ...(init?.headers as Record<string, string> | undefined) };
  if (token) headers['X-XSRF-TOKEN'] = token;
  if (init?.body) headers['Content-Type'] = 'application/json';

  const r = await fetch(url, { credentials: 'include', ...init, headers });
  if (!r.ok) invalidateTokenCache();
  return r;
}

async function saveResultFrom(r: Response): Promise<SaveResult> {
  if (r.ok) {
    if (r.status === 204) return { success: true };
    const data = await r.json().catch(() => ({}));
    return { success: true, id: data.id };
  }
  const data = await r.json().catch(() => ({}));
  return { success: false, error: data.error ?? `Ошибка (${r.status})` };
}

export interface CurrentIdentity {
  isAuthenticated: boolean;
  userId: number | null;
  isAdmin: boolean;
}

/** Тонкая обёртка над общим useAuth (hooks/useAuth.ts) — сохраняет прежний контракт. */
export function useCurrentIdentity(): CurrentIdentity & { loading: boolean } {
  const { isAuthenticated, userId, isAdmin, loading } = useAuth();
  return { isAuthenticated, userId, isAdmin, loading };
}

export function useMyHubGroups(enabled: boolean) {
  const [groups, setGroups] = useState<MyHubGroupRow[]>([]);
  const [eligibility, setEligibility] = useState<CreateEligibility | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    if (!enabled) { setLoading(false); return; }
    setLoading(true);
    try {
      const [gRes, eRes] = await Promise.all([
        fetch('/api/me/hub-groups', { credentials: 'include' }),
        fetch('/api/me/hub-groups/create-eligibility', { credentials: 'include' }),
      ]);
      setGroups(gRes.ok ? await gRes.json() : []);
      setEligibility(eRes.ok ? await eRes.json() : null);
    } finally {
      setLoading(false);
    }
  }, [enabled]);

  useEffect(() => { reload(); }, [reload]);

  const createGroup = useCallback(async (input: HubGroupInput): Promise<SaveResult> => {
    const r = await apiFetch('/api/me/hub-groups', { method: 'POST', body: JSON.stringify(input) });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [reload]);

  const deleteGroup = useCallback(async (id: number): Promise<SaveResult> => {
    const r = await apiFetch(`/api/me/hub-groups/${id}`, { method: 'DELETE' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [reload]);

  return { groups, eligibility, loading, reload, createGroup, deleteGroup };
}

/** Справочник клубов — для select в форме заявки на официальный статус. */
export function useClubOptions() {
  const [clubs, setClubs] = useState<ClubOption[]>([]);

  useEffect(() => {
    let cancelled = false;
    fetch('/api/me/hub-groups/clubs', { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : []))
      .then((data) => { if (!cancelled) setClubs(data); })
      .catch(() => {});
    return () => { cancelled = true; };
  }, []);

  return clubs;
}

export function useMyHubGroupEdit(id: number | null) {
  const [data, setData] = useState<HubGroupEditData | null>(null);
  const [admins, setAdmins] = useState<HubGroupAdmin[]>([]);
  const [clubRequest, setClubRequest] = useState<ClubRequest | null>(null);
  const [loading, setLoading] = useState(false);
  const [forbidden, setForbidden] = useState(false);

  const reload = useCallback(async () => {
    if (id == null) { setData(null); setAdmins([]); setClubRequest(null); return; }
    setLoading(true);
    setForbidden(false);
    try {
      const r = await fetch(`/api/me/hub-groups/${id}`, { credentials: 'include' });
      if (r.status === 403) { setForbidden(true); setData(null); return; }
      if (!r.ok) { setData(null); return; }
      setData(await r.json());

      const mRes = await fetch(`/api/me/hub-groups/${id}/admins`, { credentials: 'include' });
      setAdmins(mRes.ok ? await mRes.json() : []);

      const crRes = await fetch(`/api/me/hub-groups/${id}/club-request`, { credentials: 'include' });
      setClubRequest(crRes.ok && crRes.status !== 204 ? await crRes.json() : null);
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => { reload(); }, [reload]);

  const update = useCallback(async (input: HubGroupInput): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}`, { method: 'PUT', body: JSON.stringify(input) });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const searchSwimmers = useCallback(async (query: string): Promise<SwimmerSearchResult[]> => {
    const r = await fetch(`/api/me/hub-groups/search-swimmers?q=${encodeURIComponent(query)}`, { credentials: 'include' });
    return r.ok ? r.json() : [];
  }, []);

  const getClubSwimmers = useCallback(async (clubId: number): Promise<SwimmerSearchResult[]> => {
    const r = await fetch(`/api/me/hub-groups/club-swimmers?clubId=${clubId}`, { credentials: 'include' });
    return r.ok ? r.json() : [];
  }, []);

  const addMember = useCallback(async (swimmerId: number, role: string): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/members`, {
      method: 'POST', body: JSON.stringify({ swimmerId, role }),
    });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const updateMember = useCallback(async (memberId: number, role: string, sortOrder: number): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/members/${memberId}`, {
      method: 'PUT', body: JSON.stringify({ role, sortOrder }),
    });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const removeMember = useCallback(async (memberId: number): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/members/${memberId}`, { method: 'DELETE' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const addAdmin = useCallback(async (email: string): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/admins`, { method: 'POST', body: JSON.stringify({ email }) });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const removeAdmin = useCallback(async (userId: number): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/admins/${userId}`, { method: 'DELETE' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const submitClubRequest = useCallback(async (input: HubGroupClubRequestInput): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/club-request`, { method: 'POST', body: JSON.stringify(input) });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const addUserMember = useCallback(async (email: string, swimmerId?: number | null, note?: string | null): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/user-members`, {
      method: 'POST',
      body: JSON.stringify({ email, swimmerId: swimmerId ?? null, note: note ?? null }),
    });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const setUserMemberLabel = useCallback(async (userId: number, swimmerId: number | null, note: string | null): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/user-members/${userId}/label`, {
      method: 'PUT',
      body: JSON.stringify({ swimmerId, note }),
    });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const approveUserMember = useCallback(async (userId: number): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/user-members/${userId}/approve`, { method: 'POST' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const removeUserMember = useCallback(async (userId: number): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/user-members/${userId}`, { method: 'DELETE' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  return {
    data, admins, clubRequest, loading, forbidden,
    update, searchSwimmers, getClubSwimmers, addMember, updateMember, removeMember,
    addAdmin, removeAdmin, submitClubRequest, addUserMember, approveUserMember, removeUserMember, setUserMemberLabel,
  };
}

/**
 * Медиа группы (галерея + медиа тренировок) для панели самообслуживания. Читает публичную галерею
 * из /api/hub-groups/{slug} и список тренировок из /api/hub-groups/{slug}/trainings (для выбора
 * «привязать к тренировке» — отдельного CRUD тренировок в клиенте пока нет). Мутации — снейк-кейс
 * тело на /api/hub-groups/{id}/media, как ждёт HubGroupMediaInputDto на сервере.
 */
export function useHubGroupMedia(hubGroupId: number, slug: string) {
  const [gallery, setGallery] = useState<HubGroupMediaItem[]>([]);
  const [membersMedia, setMembersMedia] = useState<HubGroupMemberMediaItem[]>([]);
  const [trainings, setTrainings] = useState<TrainingOption[]>([]);
  // is_official + состав группы из деталей — редактору нужны для members-слоя
  // (переключатель видимости и select пловца-якоря).
  const [isOfficial, setIsOfficial] = useState(false);
  const [groupSwimmers, setGroupSwimmers] = useState<{ id: number; name: string }[]>([]);
  const [loading, setLoading] = useState(false);
  // Ошибка ЧТЕНИЯ списков (не мутаций): молча показывать пустую галерею нельзя —
  // выглядит как потеря данных (напр. группа скрыта настройкой HubGroupVisibility).
  const [loadError, setLoadError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const [gRes, tRes, mRes] = await Promise.all([
        fetch(`/api/hub-groups/${encodeURIComponent(slug)}`, { credentials: 'include' }),
        fetch(`/api/hub-groups/${encodeURIComponent(slug)}/trainings`, { credentials: 'include' }),
        fetch(`/api/hub-groups/${encodeURIComponent(slug)}/media/members`, { credentials: 'include' }),
      ]);

      const errors: string[] = [];
      if (!gRes.ok) errors.push(`gallery list unavailable (${gRes.status})`);
      if (!tRes.ok) errors.push(`trainings list unavailable (${tRes.status})`);
      if (!mRes.ok) errors.push(`members media unavailable (${mRes.status})`);
      setLoadError(errors.length ? `Failed to load: ${errors.join(', ')}` : null);

      const g = gRes.ok ? await gRes.json() : null;
      setGallery((g?.gallery ?? []) as HubGroupMediaItem[]);
      setIsOfficial(Boolean(g?.is_official));
      setGroupSwimmers(((g?.members ?? []) as { swimmer_id: number; name: string }[])
        .map((m) => ({ id: m.swimmer_id, name: m.name })));

      setMembersMedia(mRes.ok ? ((await mRes.json()) as HubGroupMemberMediaItem[]) : []);

      const t = tRes.ok ? await tRes.json() : null;
      const seen = new Map<number, TrainingOption>();
      for (const row of t?.results ?? []) {
        const sessionId = row?.training?.sessionId;
        if (sessionId && !seen.has(sessionId)) {
          seen.set(sessionId, {
            sessionId,
            label: `${row.training.trainingName || 'Training'} · ${row.date}`,
            media: (row.training.media ?? []) as HubGroupMediaItem[],
          });
        }
      }
      setTrainings(Array.from(seen.values()));
    } finally {
      setLoading(false);
    }
  }, [slug]);

  useEffect(() => { reload(); }, [reload]);

  const addMedia = useCallback(async (input: HubGroupMediaInput): Promise<SaveResult> => {
    const r = await apiFetch(`/api/hub-groups/${hubGroupId}/media`, {
      method: 'POST',
      body: JSON.stringify({
        media_type: input.mediaType,
        source_type: input.sourceType,
        url: input.url,
        caption: input.caption || null,
        training_id: input.trainingId ?? null,
        visibility: input.visibility ?? 'public',
        swimmer_id: input.swimmerId ?? null,
        result_id: input.resultId ?? null,
      }),
    });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [hubGroupId, reload]);

  const removeMedia = useCallback(async (mediaId: number): Promise<SaveResult> => {
    const r = await apiFetch(`/api/hub-groups/${hubGroupId}/media/${mediaId}`, { method: 'DELETE' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [hubGroupId, reload]);

  /** Последние заплывы пловца-якоря — для выбора «привязать разбор к заплыву» в редакторе. */
  const getSwimmerResults = useCallback(async (swimmerId: number): Promise<SwimmerResultOption[]> => {
    const r = await fetch(`/api/results?swimmerId=${swimmerId}&pageSize=20`, { credentials: 'include' });
    if (!r.ok) return [];
    const data = await r.json();
    return ((data?.data ?? []) as any[]).map((row) => ({
      id: row.id,
      competition: row.competition,
      date: row.date,
      styleName: row.event_style_name,
      distance: row.event_style_len,
      time: row.time,
    }));
  }, []);

  return {
    gallery, membersMedia, trainings, isOfficial, groupSwimmers, loading, loadError,
    addMedia, removeMedia, getSwimmerResults,
  };
}

/** Самозапись пользователя: вступить/выйти + список «участвую». */
export function useHubGroupMembership() {
  const [joined, setJoined] = useState<JoinedHubGroup[]>([]);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const r = await fetch('/api/me/hub-groups/joined', { credentials: 'include' });
      setJoined(r.ok ? await r.json() : []);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { reload(); }, [reload]);

  const join = useCallback(async (groupId: number): Promise<SaveResult> => {
    const r = await apiFetch(`/api/me/hub-groups/${groupId}/join`, { method: 'POST' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [reload]);

  const leave = useCallback(async (groupId: number): Promise<SaveResult> => {
    const r = await apiFetch(`/api/me/hub-groups/${groupId}/join`, { method: 'DELETE' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [reload]);

  return { joined, loading, join, leave, reload };
}

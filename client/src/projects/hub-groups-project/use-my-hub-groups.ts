import { useCallback, useEffect, useState } from 'react';
import type {
  CreateEligibility,
  HubGroupEditData,
  HubGroupInput,
  HubGroupManager,
  MyHubGroupRow,
  SaveResult,
  SwimmerSearchResult,
} from './my-groups-types';

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

export function useCurrentIdentity() {
  const [identity, setIdentity] = useState<CurrentIdentity>({ isAuthenticated: false, userId: null, isAdmin: false });
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const r = await fetch('/auth/me', { credentials: 'include' });
        if (cancelled) return;
        if (!r.ok) { setIdentity({ isAuthenticated: false, userId: null, isAdmin: false }); return; }
        const me = await r.json();
        setIdentity({
          isAuthenticated: !!me?.isAuthenticated,
          userId: me?.id ?? null,
          isAdmin: Array.isArray(me?.roles) && me.roles.includes('Admin'),
        });
      } catch {
        if (!cancelled) setIdentity({ isAuthenticated: false, userId: null, isAdmin: false });
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  return { ...identity, loading };
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

export function useMyHubGroupEdit(id: number | null) {
  const [data, setData] = useState<HubGroupEditData | null>(null);
  const [managers, setManagers] = useState<HubGroupManager[]>([]);
  const [loading, setLoading] = useState(false);
  const [forbidden, setForbidden] = useState(false);

  const reload = useCallback(async () => {
    if (id == null) { setData(null); setManagers([]); return; }
    setLoading(true);
    setForbidden(false);
    try {
      const r = await fetch(`/api/me/hub-groups/${id}`, { credentials: 'include' });
      if (r.status === 403) { setForbidden(true); setData(null); return; }
      if (!r.ok) { setData(null); return; }
      setData(await r.json());

      const mRes = await fetch(`/api/me/hub-groups/${id}/managers`, { credentials: 'include' });
      setManagers(mRes.ok ? await mRes.json() : []);
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

  const addManager = useCallback(async (email: string): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/managers`, { method: 'POST', body: JSON.stringify({ email }) });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  const removeManager = useCallback(async (userId: number): Promise<SaveResult> => {
    if (id == null) return { success: false, error: 'Нет группы' };
    const r = await apiFetch(`/api/me/hub-groups/${id}/managers/${userId}`, { method: 'DELETE' });
    const result = await saveResultFrom(r);
    if (result.success) await reload();
    return result;
  }, [id, reload]);

  return {
    data, managers, loading, forbidden,
    update, searchSwimmers, addMember, updateMember, removeMember, addManager, removeManager,
  };
}

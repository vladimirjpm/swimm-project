import { useState, useEffect, useCallback, useRef } from 'react';

// ── Types ────────────────────────────────────────────────────────────────────

export interface FavoriteDto {
  id: number;
  target_type: 'swimmer' | 'club';
  swimmer_id?: number;
  swimmer_name?: string;
  club_id?: number;
  club_name?: string;
  is_primary: boolean;
  sort_order: number;
  created_at: string;
}

interface FavoritesState {
  isAuthenticated: boolean;
  favorites: FavoriteDto[];
  /** ID пловца с is_primary=true, или null */
  primarySwimmerId: number | null;
  /** Множество ID пловцов в избранном */
  favoriteSwimmerIds: Set<number>;
  loading: boolean;
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

// ── Hook ─────────────────────────────────────────────────────────────────────

export function useFavorites() {
  const [state, setState] = useState<FavoritesState>({
    isAuthenticated: false,
    favorites: [],
    primarySwimmerId: null,
    favoriteSwimmerIds: new Set(),
    loading: true,
  });

  // Флаг, чтобы не делать запросы после размонтирования компонента
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  const applyFavorites = useCallback((favorites: FavoriteDto[], isAuthenticated: boolean) => {
    const swimmerFavs = favorites.filter(f => f.target_type === 'swimmer');
    const primarySwimmerId = swimmerFavs.find(f => f.is_primary)?.swimmer_id ?? null;
    const favoriteSwimmerIds = new Set(
      swimmerFavs.map(f => f.swimmer_id).filter((id): id is number => id != null)
    );
    if (mountedRef.current) {
      setState({ isAuthenticated, favorites, primarySwimmerId, favoriteSwimmerIds, loading: false });
    }
  }, []);

  // Начальная загрузка
  useEffect(() => {
    let cancelled = false;

    async function init() {
      try {
        // 1. Проверяем аутентификацию
        const meRes = await fetch('/auth/me', { credentials: 'include' });
        if (cancelled) return;
        if (!meRes.ok) { applyFavorites([], false); return; }

        const me = await meRes.json();
        if (!me?.isAuthenticated) { applyFavorites([], false); return; }

        // 2. Загружаем фавориты
        const favsRes = await fetch('/api/me/favorites', { credentials: 'include' });
        if (cancelled) return;
        if (!favsRes.ok) { applyFavorites([], true); return; }

        const favs: FavoriteDto[] = await favsRes.json();
        if (!cancelled) applyFavorites(favs, true);
      } catch {
        if (!cancelled) applyFavorites([], false);
      }
    }

    init();
    return () => { cancelled = true; };
  }, [applyFavorites]);

  // ── Mutations ───────────────────────────────────────────────────────────────

  const addFavoriteSwimmer = useCallback(async (swimmerId: number): Promise<boolean> => {
    const token = await fetchAntiforgeryToken();
    if (!token) return false;

    try {
      const r = await fetch('/api/me/favorites', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
        body: JSON.stringify({ target_type: 'swimmer', swimmer_id: swimmerId }),
      });

      if (r.status === 409) return false; // уже в избранном
      if (!r.ok) { invalidateTokenCache(); return false; }

      const fav: FavoriteDto = await r.json();
      if (mountedRef.current) {
        setState(prev => {
          const next = [...prev.favorites, fav];
          const ids = new Set(prev.favoriteSwimmerIds);
          if (fav.swimmer_id) ids.add(fav.swimmer_id);
          return { ...prev, favorites: next, favoriteSwimmerIds: ids };
        });
      }
      return true;
    } catch {
      invalidateTokenCache();
      return false;
    }
  }, []);

  const removeFavorite = useCallback(async (favoriteId: number): Promise<boolean> => {
    const token = await fetchAntiforgeryToken();
    if (!token) return false;

    try {
      const r = await fetch(`/api/me/favorites/${favoriteId}`, {
        method: 'DELETE',
        credentials: 'include',
        headers: { 'X-XSRF-TOKEN': token },
      });

      if (!r.ok) { invalidateTokenCache(); return false; }

      if (mountedRef.current) {
        setState(prev => {
          const removed = prev.favorites.find(f => f.id === favoriteId);
          const next = prev.favorites.filter(f => f.id !== favoriteId);
          const ids = new Set(prev.favoriteSwimmerIds);
          if (removed?.swimmer_id) ids.delete(removed.swimmer_id);
          const primarySwimmerId = next.find(f => f.is_primary && f.target_type === 'swimmer')?.swimmer_id ?? null;
          return { ...prev, favorites: next, favoriteSwimmerIds: ids, primarySwimmerId };
        });
      }
      return true;
    } catch {
      invalidateTokenCache();
      return false;
    }
  }, []);

  const unsetPrimary = useCallback(async (favoriteId: number): Promise<boolean> => {
    const token = await fetchAntiforgeryToken();
    if (!token) return false;

    try {
      const r = await fetch(`/api/me/favorites/${favoriteId}/primary`, {
        method: 'DELETE',
        credentials: 'include',
        headers: { 'X-XSRF-TOKEN': token },
      });

      if (!r.ok) { invalidateTokenCache(); return false; }

      if (mountedRef.current) {
        setState(prev => {
          const next = prev.favorites.map(f =>
            f.id === favoriteId ? { ...f, is_primary: false } : f
          );
          return { ...prev, favorites: next, primarySwimmerId: null };
        });
      }
      return true;
    } catch {
      invalidateTokenCache();
      return false;
    }
  }, []);

  const setPrimary = useCallback(async (favoriteId: number): Promise<boolean> => {
    const token = await fetchAntiforgeryToken();
    if (!token) return false;

    try {
      const r = await fetch(`/api/me/favorites/${favoriteId}/primary`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'X-XSRF-TOKEN': token },
      });

      if (!r.ok) { invalidateTokenCache(); return false; }

      if (mountedRef.current) {
        setState(prev => {
          const next = prev.favorites.map(f =>
            f.target_type === 'swimmer' ? { ...f, is_primary: f.id === favoriteId } : f
          );
          const primary = next.find(f => f.is_primary && f.target_type === 'swimmer');
          return { ...prev, favorites: next, primarySwimmerId: primary?.swimmer_id ?? null };
        });
      }
      return true;
    } catch {
      invalidateTokenCache();
      return false;
    }
  }, []);

  /** Переключение: добавить → избранное / убрать из избранного */
  const toggleFavoriteSwimmer = useCallback(async (swimmerId: number): Promise<void> => {
    if (!state.isAuthenticated) return;

    const existing = state.favorites.find(
      f => f.target_type === 'swimmer' && f.swimmer_id === swimmerId
    );
    if (existing) {
      await removeFavorite(existing.id);
    } else {
      await addFavoriteSwimmer(swimmerId);
    }
  }, [state.isAuthenticated, state.favorites, addFavoriteSwimmer, removeFavorite]);

  /** Переключение primary для пловца: не primary → primary → снять primary */
  const togglePrimarySwimmer = useCallback(async (swimmerId: number): Promise<void> => {
    if (!state.isAuthenticated) return;

    const fav = state.favorites.find(
      f => f.target_type === 'swimmer' && f.swimmer_id === swimmerId
    );
    if (!fav) return;

    if (fav.is_primary) {
      await unsetPrimary(fav.id);
    } else {
      await setPrimary(fav.id);
    }
  }, [state.isAuthenticated, state.favorites, unsetPrimary, setPrimary]);

  return {
    ...state,
    toggleFavoriteSwimmer,
    togglePrimarySwimmer,
    setPrimary,
    unsetPrimary,
    removeFavorite,
    addFavoriteSwimmer,
  };
}

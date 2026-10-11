import { useMemo } from 'react';
import type { useFavorites } from '../../hooks/useFavorites';
import { sortByFavoriteRank } from '../../utils/helpers/favorites-order';
import type { MySwimmerDto } from './use-my-swims';
import type { AllUserMediaDto } from './use-all-my-media';
import type { AddLinkSwimmerOption } from './components/add-link-modal';

/**
 * Пловцы профиля для панели фильтра и для пикера Add link: в порядке Me → семья →
 * остальные, а пикеру — ещё избранные без заплывов в сезоне и пловцы, у которых уже есть медиа.
 */
export function useProfileSwimmers(
  swimmers: MySwimmerDto[],
  favorites: ReturnType<typeof useFavorites>,
  allMedia: AllUserMediaDto[],
) {
  // Пловцы профиля в порядке Me → семья → остальные (family-favorites-plan.md) — так их
  // показывают и панель фильтра, и пикер Add link.
  const orderedSwimmers = useMemo(
    () => sortByFavoriteRank(swimmers, (s) => s.id, favorites.primarySwimmerId, favorites.familySwimmerIds),
    [swimmers, favorites.primarySwimmerId, favorites.familySwimmerIds],
  );

  // ── Add link: пловцы для пикера ───────────────────────────────────────────
  const addLinkSwimmers: AddLinkSwimmerOption[] = useMemo(() => {
    const byId = new Map<number, AddLinkSwimmerOption>();
    for (const s of orderedSwimmers) {
      byId.set(s.id, { id: s.id, name: s.name, hint: s.is_primary ? 'primary swimmer' : 'favorite' });
    }
    for (const f of favorites.favorites) {
      if (f.target_type !== 'swimmer' || f.swimmer_id == null || byId.has(f.swimmer_id)) continue;
      byId.set(f.swimmer_id, { id: f.swimmer_id, name: f.swimmer_name || `#${f.swimmer_id}`, hint: 'favorite' });
    }
    for (const m of allMedia) {
      if (!byId.has(m.swimmer_id)) byId.set(m.swimmer_id, { id: m.swimmer_id, name: m.swimmer_name, hint: 'has media' });
    }
    return Array.from(byId.values());
  }, [orderedSwimmers, favorites.favorites, allMedia]);

  return { orderedSwimmers, addLinkSwimmers };
}

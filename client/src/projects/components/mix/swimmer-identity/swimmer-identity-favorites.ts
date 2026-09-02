import { useFavoritesContext } from '../../../../hooks/favorites-context';
import { useLoginModal } from '../../login-modal/login-modal-context';

/**
 * Состояние действий ♡/★ для семейства `UI_SwimmerIdentity*`.
 *
 * Кнопки в вариантах выглядят ПО-РАЗНОМУ (у шапки страницы палитра Deep, у попапа —
 * results), а вот правила у них одни: гостю действий нет, без `swimmerId` их нет тоже
 * (строка протокола без привязки к базе), звезда «это я» — логическая метка и прав не даёт
 * (`rule-primary-favorite-untrusted`). До выноса эти правила были переписаны трижды.
 */
export function useIdentityFavorites(swimmerId?: number | null) {
  const {
    isAuthenticated, primarySwimmerId, favoriteSwimmerIds, setMeBySwimmer, toggleFavoriteSwimmer,
  } = useFavoritesContext();
  const { openLoginModal } = useLoginModal();

  const canMark = isAuthenticated && swimmerId != null;

  return {
    isAuthenticated,
    /** Можно ли показывать ♡/★ вообще. */
    canMark,
    isFavorite: canMark && favoriteSwimmerIds.has(swimmerId!),
    isMe: canMark && swimmerId === primarySwimmerId,
    /** Гость на пловце, которого МОЖНО было бы отметить, — ему показывают приглашение войти. */
    showGuestCta: !isAuthenticated && swimmerId != null,
    toggleFavorite: () => { if (swimmerId != null) toggleFavoriteSwimmer(swimmerId); },
    markAsMe: () => { if (swimmerId != null) setMeBySwimmer(swimmerId); },
    openLoginModal,
  };
}

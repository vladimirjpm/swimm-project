import { useFavoritesContext } from '../../../../hooks/favorites-context';
import { useLoginModal } from '../../login-modal/login-modal-context';

/**
 * Состояние действий ♡/★ для семейства `UI_SwimmerIdentity*`.
 *
 * Кнопки в вариантах выглядят ПО-РАЗНОМУ (у шапки страницы палитра Deep, у попапа —
 * results), а вот правила у них одни: гостю действий нет, без `swimmerId` их нет тоже
 * (строка протокола без привязки к базе), звезда «это я» — логическая метка и прав не даёт
 * (`rule-primary-favorite-untrusted`). До выноса эти правила были переписаны трижды.
 *
 * Лимит избранного (30 пловцов по умолчанию) гасит ОБЕ кнопки у пловца, которого в избранном
 * ещё нет: звезда «это я» добавляет его в избранное и тоже идёт в счёт.
 */
export function useIdentityFavorites(swimmerId?: number | null) {
  const {
    isAuthenticated, primarySwimmerId, favoriteSwimmerIds, fullHint, setMeBySwimmer, toggleFavoriteSwimmer,
  } = useFavoritesContext();
  const { openLoginModal } = useLoginModal();

  const canMark = isAuthenticated && swimmerId != null;
  const isFavorite = canMark && favoriteSwimmerIds.has(swimmerId!);

  return {
    isAuthenticated,
    /** Можно ли показывать ♡/★ вообще. */
    canMark,
    isFavorite,
    isMe: canMark && swimmerId === primarySwimmerId,
    /** Добавить нельзя — лимит выбран: кнопки погашены, это текст подсказки. null — можно. */
    addBlockedHint: canMark && !isFavorite ? fullHint('swimmer') : null,
    /** Гость на пловце, которого МОЖНО было бы отметить, — ему показывают приглашение войти. */
    showGuestCta: !isAuthenticated && swimmerId != null,
    toggleFavorite: () => { if (swimmerId != null) toggleFavoriteSwimmer(swimmerId); },
    markAsMe: () => { if (swimmerId != null) setMeBySwimmer(swimmerId); },
    openLoginModal,
  };
}

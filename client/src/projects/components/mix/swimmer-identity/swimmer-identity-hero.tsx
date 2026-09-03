import React from 'react';
import './swimmer-identity.css';
import UI_ClubIcon from '../club-icon/club-icon';
import UI_SwimmerAvatar from '../swimmer-avatar/swimmer-avatar';
import { useIdentityFavorites } from './swimmer-identity-favorites';
import { identityAgeLabel, type SwimmerIdentity } from './swimmer-identity.types';
import { routes } from '../../../../utils/routes';

/**
 * Идентичность пловца — вариант ШАПКИ СТРАНИЦЫ (`/swimmers/{id}`, палитра Deep).
 *
 * Показывает только «кто это»: аватар с флагом, действия ♡/★, имя, возраст, клуб.
 * Статистика приезжает слотом `aside` (на странице спортсмена это KPI-плитки), собственные
 * чипы страницы — слотом `chips`. Так третий вариант семейства (мини) не тащит за собой то,
 * что нужно только большой странице.
 *
 * Гость видит ВЕСЬ публичный контент — скрыты только действия, вместо них полоса «Sign in»
 * (§10 хендоффа): заглушек «войдите» внутри контентных блоков быть не должно.
 */
interface Props {
  identity: SwimmerIdentity;
  /** Чипы под именем: зачётная группа, «open water», «local» — специфика страницы. */
  chips?: React.ReactNode;
  /** Правая колонка шапки: KPI, медали, разряд. Без неё шапка схлопывается в две колонки. */
  aside?: React.ReactNode;
}

/** ♡ избранное + ★ «это я». Гостю — приглашение войти вместо кнопок. */
function Actions({ swimmerId }: { swimmerId: number }) {
  const { isAuthenticated, isFavorite, isMe, toggleFavorite, markAsMe, openLoginModal } =
    useIdentityFavorites(swimmerId);

  if (!isAuthenticated) {
    return (
      <button type="button" onClick={openLoginModal} className="deep-hero-cta">
        Sign in to save favorites
      </button>
    );
  }

  return (
    <div className="flex items-center gap-2">
      <button
        type="button"
        onClick={toggleFavorite}
        title={isFavorite ? 'Remove from favorites' : 'Add to favorites'}
        aria-pressed={isFavorite}
        className={`deep-hero-action${isFavorite ? ' deep-hero-action--fav' : ''}`}
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill={isFavorite ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="2">
          <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
        </svg>
      </button>
      <button
        type="button"
        onClick={markAsMe}
        title={isMe ? 'This is me — unmark' : 'Mark: this is me'}
        aria-pressed={isMe}
        className={`deep-hero-action${isMe ? ' deep-hero-action--me' : ''}`}
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill={isMe ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round">
          <path d="M12 2.6l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 17.45 6.2 20.5l1.1-6.45-4.7-4.6 6.5-.95z" />
        </svg>
      </button>
    </div>
  );
}

const UI_SwimmerIdentityHero: React.FC<Props> = ({ identity, chips, aside }) => (
  <div className="deep-hero">
    <div className="deep-hero__id">
      {/* Портрет — общий `UI_SwimmerAvatar`: он же в карточке-попапе и в мини-карточке H2H.
          Раньше здесь была буква вместо фото у всех, кто не загрузил своё, и флаг только
          при заданной стране. */}
      <UI_SwimmerAvatar
        avatarUrl={identity.avatarUrl}
        gender={identity.gender}
        countryCode={identity.countryCode}
        name={identity.name}
        size={96}
        className="deep-hero__avatar-box"
      />
      {identity.id != null && <Actions swimmerId={identity.id} />}
    </div>

    <div className="deep-hero__main">
      {/* Чип «🏆 N records» у имени снят: счётчик рекордов живёт в плитке достижений
          (docs/swimmer-achievements-tile.md), и одна и та же цифра в шапке дважды —
          это не акцент, а шум. */}
      <h1 dir="auto" className="deep-hero__name">{identity.name}</h1>

      <div className="deep-hero__age">{identityAgeLabel(identity)}</div>

      <div className="flex flex-wrap items-center gap-2">
        {identity.clubName && (
          identity.clubId ? (
            <a href={routes.club(identity.clubId)} className="deep-club-chip" dir="auto">
              <span className="deep-club-chip__logo">
                <UI_ClubIcon clubName={identity.clubName} iconWidth="22px" />
              </span>
              {identity.clubName}
            </a>
          ) : (
            <span className="deep-club-chip" dir="auto">
              <span className="deep-club-chip__logo">
                <UI_ClubIcon clubName={identity.clubName} iconWidth="22px" />
              </span>
              {identity.clubName}
            </span>
          )
        )}
        {chips}
      </div>
    </div>

    {aside}
  </div>
);

export default UI_SwimmerIdentityHero;

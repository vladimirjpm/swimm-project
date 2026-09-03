import React from 'react';
import UI_ClubIcon from '../club-icon/club-icon';
import UI_SwimmerAvatar from '../swimmer-avatar/swimmer-avatar';
import { useIdentityFavorites } from './swimmer-identity-favorites';
import { identityAgeLabel, identityDefaultAvatar, type SwimmerIdentity } from './swimmer-identity.types';
import { routes } from '../../../../utils/routes';

/**
 * Идентичность пловца — вариант КАРТОЧКИ-ПОПАПА (палитра results,
 * `design_handoff_athlete_card` §1). Аватар слева, справа RTL-колонка: имя, возраст,
 * ссылка на страницу, чип клуба.
 *
 * Отличия от варианта hero не косметические, поэтому это отдельный компонент, а не проп:
 * здесь дефолтная КАРТИНКА вместо буквы-заглушки, обратный порядок колонок и своя палитра
 * (`--theme-mode-*` вместо `--deep-*`) — попап живёт на странице результатов.
 *
 * Слоты: `meta` — то, что попап дорисовывает под аватаром (бейдж loglig), `extra` — под
 * чипом клуба. Ссылка «Open full profile →» появляется, когда пловец заведён в базе:
 * без `id` открывать нечего.
 */
interface Props {
  identity: SwimmerIdentity;
  /** Под аватаром, следом за ♡/★ — у попапа это бейдж loglig. */
  meta?: React.ReactNode;
  /** Под чипом клуба — свободный слот вызывающего экрана. */
  extra?: React.ReactNode;
  /** Показывать ссылку на страницу пловца (по умолчанию да, если есть id). */
  showProfileLink?: boolean;
}

/** ♡/★ карточки: белые круглые кнопки на градиенте шапки попапа. */
function CardActions({ swimmerId }: { swimmerId: number }) {
  const { canMark, isFavorite, isMe, showGuestCta, toggleFavorite, markAsMe, openLoginModal } =
    useIdentityFavorites(swimmerId);

  if (showGuestCta) {
    return (
      <button
        type="button"
        onClick={openLoginModal}
        title="Sign in to save favorites"
        className="w-8 h-8 rounded-full inline-flex items-center justify-center leading-none hover:scale-110 transition-transform"
        style={{ background: 'rgba(255,255,255,0.9)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#9aa3af" strokeWidth="2">
          <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
        </svg>
      </button>
    );
  }

  if (!canMark) return null;

  return (
    <div className="flex items-center gap-1.5">
      <button
        type="button"
        onClick={toggleFavorite}
        title={isFavorite ? 'Remove from favorites' : 'Add to favorites'}
        aria-pressed={isFavorite}
        className="w-8 h-8 rounded-full inline-flex items-center justify-center leading-none hover:scale-110 transition-transform"
        style={{ background: 'rgba(255,255,255,0.9)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill={isFavorite ? '#e23b5a' : 'none'} stroke={isFavorite ? '#e23b5a' : '#9aa3af'} strokeWidth="2">
          <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
        </svg>
      </button>
      <button
        type="button"
        onClick={markAsMe}
        title={isMe ? 'This is me — unmark' : 'Mark: this is me'}
        aria-pressed={isMe}
        className="w-8 h-8 rounded-full inline-flex items-center justify-center leading-none hover:scale-110 transition-transform"
        style={{ background: isMe ? '#fff6da' : 'rgba(255,255,255,0.75)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill={isMe ? '#f5b800' : 'none'} stroke={isMe ? '#d99a00' : '#9aa3af'} strokeWidth="1.8" strokeLinejoin="round">
          <path d="M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z" />
        </svg>
      </button>
    </div>
  );
}

const UI_SwimmerIdentityCard: React.FC<Props> = ({
  identity, meta, extra, showProfileLink = true,
}) => {
  const base = import.meta.env.BASE_URL;

  return (
    <div
      className="relative px-[18px] pt-4 pb-3.5 flex gap-3.5 items-center"
      style={{ background: 'var(--theme-mode-hero-grad)' }}
    >
      <div className="flex flex-col items-center gap-2 shrink-0">
        {/* Портрет с флагом — общий компонент; палитра попапа приходит токенами `--sa-*`. */}
        <UI_SwimmerAvatar
          avatarUrl={identity.avatarUrl}
          gender={identity.gender}
          countryCode={identity.countryCode}
          name={identity.name}
          size={76}
          className="[--sa-ring:var(--theme-primary)]"
        />

        {identity.id != null && <CardActions swimmerId={identity.id} />}
        {meta}
      </div>

      <div className="flex-1 min-w-0 flex flex-col gap-1.5 items-end">
        <div
          dir="rtl"
          className="text-xl font-extrabold leading-tight truncate max-w-full"
          style={{ color: 'var(--theme-mode-text)' }}
        >
          {identity.name}
        </div>

        <div
          className="flex items-center gap-2 text-[11px] font-semibold"
          style={{ color: 'var(--theme-mode-text-muted)' }}
        >
          {extra}
          <span>{identityAgeLabel(identity)}</span>
        </div>

        {/* Диплинк на самостоятельную страницу пловца. */}
        {showProfileLink && identity.id != null && (
          <a
            href={routes.swimmer(identity.id)}
            className="text-[11px] font-bold hover:underline"
            style={{ color: 'var(--theme-primary)' }}
          >
            Open full profile →
          </a>
        )}

        {identity.clubName && (
          <div
            dir="rtl"
            className="flex items-center gap-2 rounded-full py-1 pl-3.5 pr-[5px] max-w-full"
            style={{ background: 'var(--theme-mode-input-bg)' }}
          >
            <span
              className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full p-[2px]"
              style={{ background: '#ffffff', boxShadow: '0 0 0 2px color-mix(in srgb, var(--theme-primary) 40%, transparent)' }}
            >
              <UI_ClubIcon clubName={identity.clubName} clubId={identity.clubId ?? undefined} iconWidth="full" styleType="icon-notext" />
            </span>
            <span className="truncate text-xs font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
              {identity.clubName}
            </span>
          </div>
        )}
      </div>
    </div>
  );
};

export default UI_SwimmerIdentityCard;

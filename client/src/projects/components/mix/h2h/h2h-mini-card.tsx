import React from 'react';
import './h2h.css';
import { routes } from '../../../../utils/routes';
import UI_SwimmerAvatar from '../swimmer-avatar/swimmer-avatar';
import type { H2HSwimmer } from './h2h.types';

/**
 * Мини-карточка спортсмена в шапке сравнения (макет 1b, §1 `H2H-COMPONENTS.md`).
 *
 * Карточки ЗЕРКАЛЬНЫЕ: фото всегда смотрит в центр шапки (у левой — текст, потом фото),
 * а сердечко-фаворит уходит во внешний угол, чтобы не спорить с фото.
 *
 * Сама карточка — ссылка на страницу пловца; сердечко перехватывает клик и не даёт
 * переходу случиться (иначе «добавить в избранное» уводило бы со страницы).
 */
interface Props {
  swimmer: H2HSwimmer;
  /** Сторона в шапке: определяет и порядок фото/текста, и угол сердечка. */
  align: 'left' | 'right';
  /** null — избранное недоступно (гость), сердечко не рисуется вовсе. */
  isFavorite?: boolean | null;
  onToggleFavorite?: () => void;
  /**
   * Сброс стороны. Не задан — карточку сменить нельзя (в табе левый это хозяин профиля),
   * и кнопки нет вовсе: в макете её тоже нет, она появилась вместе со страницей `/h2h`,
   * где сменяемы обе стороны.
   */
  onClear?: (() => void) | null;
}

const UI_H2HMiniCard: React.FC<Props> = ({
  swimmer, align, isFavorite = null, onToggleFavorite, onClear = null,
}) => {
  // Портрет — общий `UI_SwimmerAvatar` (он же в шапке страницы пловца и в карточке-попапе):
  // раньше здесь была буква без флага, и один и тот же человек выглядел на двух экранах
  // по-разному.
  const avatar = (
    <UI_SwimmerAvatar
      avatarUrl={swimmer.avatarUrl}
      gender={swimmer.gender}
      countryCode={swimmer.countryCode}
      name={swimmer.name}
      size={72}
      className="h2h-mini__avatar"
    />
  );

  const text = (
    <span className="h2h-mini__text">
      <span dir="auto" className="h2h-mini__name">{swimmer.name}</span>
      {swimmer.club && <span dir="auto" className="h2h-mini__club">{swimmer.club}</span>}
      {swimmer.ageLabel && <span className="h2h-mini__age">{swimmer.ageLabel}</span>}
    </span>
  );

  return (
    <a className={`h2h-mini h2h-mini--${align}`} href={routes.swimmer(swimmer.id)}>
      {isFavorite !== null && (
        <button
          type="button"
          className={`h2h-mini__fav${isFavorite ? ' h2h-mini__fav--on' : ''}`}
          title={isFavorite ? 'Remove from favorites' : 'Add to favorites'}
          aria-pressed={isFavorite}
          onClick={(e) => { e.preventDefault(); e.stopPropagation(); onToggleFavorite?.(); }}
        >
          {isFavorite ? '♥' : '♡'}
        </button>
      )}
      {onClear && (
        <button
          type="button"
          className="h2h-mini__clear"
          title="Choose another swimmer"
          onClick={(e) => { e.preventDefault(); e.stopPropagation(); onClear(); }}
        >
          ✕
        </button>
      )}
      {align === 'left' ? <>{text}{avatar}</> : <>{avatar}{text}</>}
    </a>
  );
};

export default UI_H2HMiniCard;

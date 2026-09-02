import React from 'react';
import './h2h.css';
import { routes } from '../../../../utils/routes';
import { identityInitial } from '../swimmer-identity/swimmer-identity.types';
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
}

const UI_H2HMiniCard: React.FC<Props> = ({ swimmer, align, isFavorite = null, onToggleFavorite }) => {
  const avatar = (
    <span className="h2h-mini__avatar">
      {swimmer.avatarUrl
        ? <img src={swimmer.avatarUrl} alt="" />
        : identityInitial(swimmer.name)}
    </span>
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
      {align === 'left' ? <>{text}{avatar}</> : <>{avatar}{text}</>}
    </a>
  );
};

export default UI_H2HMiniCard;

import React from 'react';
import './h2h.css';
import { routes } from '../../../../utils/routes';
import UI_SwimmerAvatar from '../swimmer-avatar/swimmer-avatar';
import UI_H2HSideCard from './h2h-side-card';
import type { H2HSwimmer } from './h2h.types';

/**
 * Мини-карточка спортсмена в шапке сравнения (макет 1b, §1 `H2H-COMPONENTS.md`) — пловец
 * поверх общей стороны `UI_H2HSideCard`, той же, что несёт страну на `/records/compare`.
 *
 * ⚠ **Клик по карточке выбирает ДРУГОГО пловца, а не открывает профиль** (решение Влада
 * 23.09.2026). Раньше карточка была ссылкой в профиль, а сменить сторону предлагал ✕ в
 * углу — символ поверх фото, который плохо читался и требовал знать о нём заранее. Теперь
 * главный жест экрана занят главным действием экрана, а профиль стоит строкой ПОД
 * карточкой, где ссылка и выглядит ссылкой.
 *
 * `onSelect: null` — сторону сменить нельзя (в табе левый это хозяин профиля): карточка
 * тогда не кнопка. Сердечко-фаворит перехватывает клик и до карточки его не пускает.
 */
interface Props {
  swimmer: H2HSwimmer;
  /** Сторона в шапке: определяет и порядок фото/текста, и угол сердечка. */
  align: 'left' | 'right';
  /** null — избранное недоступно (гость), сердечко не рисуется вовсе. */
  isFavorite?: boolean | null;
  onToggleFavorite?: () => void;
  /**
   * Лимит избранного выбран: пустое сердечко погашено, текст — подсказка в title. Горящее
   * не гаснет: убрать из избранного можно всегда.
   */
  favoriteBlockedHint?: string | null;
  /**
   * Выбрать на эту сторону другого пловца. Не задан — сторона несменяема (в табе H2H это
   * хозяин страницы), и карточка перестаёт быть кнопкой.
   */
  onSelect?: (() => void) | null;
  /**
   * Эту сторону заполнит выбор в пикере — тонкая акцентная рамка. У ЗАНЯТОЙ карточки это
   * предупреждение: следующий выбор заменит стоящего здесь пловца.
   */
  active?: boolean;
}

const UI_H2HMiniCard: React.FC<Props> = ({
  swimmer, align, isFavorite = null, onToggleFavorite, favoriteBlockedHint = null,
  onSelect = null, active = false,
}) => {
  const favBlocked = !isFavorite && favoriteBlockedHint != null;

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

  const heart = isFavorite !== null ? (
    <button
      type="button"
      className={`h2h-mini__fav${isFavorite ? ' h2h-mini__fav--on' : ''}`}
      title={favBlocked ? favoriteBlockedHint! : isFavorite ? 'Remove from favorites' : 'Add to favorites'}
      aria-pressed={isFavorite}
      // `aria-disabled`, а не `disabled`: у выключенной кнопки title не всплывает, а клик
      // всё равно надо перехватить — иначе он уйдёт в карточку и сменит сторону.
      aria-disabled={favBlocked || undefined}
      onClick={(e) => { e.preventDefault(); e.stopPropagation(); if (!favBlocked) onToggleFavorite?.(); }}
    >
      {isFavorite ? '♥' : '♡'}
    </button>
  ) : null;

  return (
    <UI_H2HSideCard
      align={align}
      media={avatar}
      name={swimmer.name}
      sub={swimmer.club}
      chip={swimmer.ageLabel}
      corner={heart}
      onSelect={onSelect}
      selectHint={onSelect ? `${swimmer.name} — click to pick another swimmer` : undefined}
      active={active}
      // Профиль остаётся в одном клике, но уже своей строкой. Ссылка есть и у несменяемой
      // стороны: в табе она ведёт на ту же страницу, где человек стоит, и это нормально —
      // на `/h2h` та же карточка уводит в профиль хозяина сравнения.
      link={{ href: routes.swimmer(swimmer.id), label: 'profile →' }}
    />
  );
};

export default UI_H2HMiniCard;

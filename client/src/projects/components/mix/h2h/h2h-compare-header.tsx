import React from 'react';
import './h2h.css';
import UI_H2HMiniCard from './h2h-mini-card';
import UI_H2HStatRow from './h2h-stat-row';
import UI_H2HMedalTriple from './h2h-medal-triple';
import UI_H2HSwap from './h2h-swap';
import type { H2HMedals, H2HSwimmer, H2HWinner } from './h2h.types';

/**
 * Шапка сравнения (макет 1b, §1–2): две зеркальные мини-карточки со счётом между ними,
 * ниже — три строки статов на той же сетке, отбитые линиями.
 *
 * Счёт «3–2» — это НЕ очные встречи: он говорит, на скольких общих парах «дистанция ×
 * бассейн» чьё лучшее время быстрее. Подпись под ним обязана это сказать вслух.
 */
export interface H2HSide {
  swimmer: H2HSwimmer;
  seasonBests: number;
  medals: H2HMedals;
  bestPoints: number;
  /** null — избранное недоступно (гость): сердечко не рисуется. */
  isFavorite?: boolean | null;
  onToggleFavorite?: () => void;
  /** Сброс стороны; не задан — сменить нельзя (в табе левый это хозяин профиля). */
  onClear?: (() => void) | null;
}

interface Props {
  left: H2HSide;
  right: H2HSide;
  leftFaster: number;
  rightFaster: number;
  ties: number;
  /** Показывать ли строку season bests: за карьеру мест среди сверстников нет. */
  showSeasonBests?: boolean;
  /** Поменять стороны местами; не задан — кнопки нет. */
  onSwap?: () => void;
}

/** Победитель строки: больше — лучше; равенство подсветки не даёт. */
const winnerOf = (left: number, right: number): H2HWinner =>
  left === right ? null : (left > right ? 'left' : 'right');

const UI_H2HCompareHeader: React.FC<Props> = ({
  left, right, leftFaster, rightFaster, ties, showSeasonBests = true, onSwap,
}) => (
  <div className="h2h-compare">
    <div className="h2h-row">
      <UI_H2HMiniCard
        swimmer={left.swimmer}
        align="left"
        isFavorite={left.isFavorite ?? null}
        onToggleFavorite={left.onToggleFavorite}
        onClear={left.onClear ?? null}
      />
      <div className="h2h-score">
        <div className="h2h-score__value">{leftFaster}–{rightFaster}</div>
        <div className="h2h-score__cap">faster times{ties > 0 ? ` · ${ties} tied` : ''}</div>
        <UI_H2HSwap onSwap={onSwap} />
      </div>
      <UI_H2HMiniCard
        swimmer={right.swimmer}
        align="right"
        isFavorite={right.isFavorite ?? null}
        onToggleFavorite={right.onToggleFavorite}
        onClear={right.onClear ?? null}
      />
    </div>

    <div className="h2h-stats">
      {showSeasonBests && (
        <UI_H2HStatRow
          label="season bests"
          left={left.seasonBests}
          right={right.seasonBests}
          winner={winnerOf(left.seasonBests, right.seasonBests)}
        />
      )}
      <UI_H2HStatRow
        label="medals"
        left={<UI_H2HMedalTriple medals={left.medals} />}
        right={<UI_H2HMedalTriple medals={right.medals} />}
        raw
      />
      <UI_H2HStatRow
        label="best FINA pts"
        left={left.bestPoints}
        right={right.bestPoints}
        winner={winnerOf(left.bestPoints, right.bestPoints)}
      />
    </div>
  </div>
);

export default UI_H2HCompareHeader;

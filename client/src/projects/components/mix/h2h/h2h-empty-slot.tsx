import React from 'react';
import './h2h.css';

/**
 * Пустой слот «выбери соперника» (макет 1b, §4). Стоит на месте мини-карточки, пока
 * соперник не выбран, и открывает выбор по клику.
 *
 * Подпись двуязычная и пришла из макета как есть: иврит — язык пользователей витрины,
 * английский — правило интерфейса. Это не строка UI на иврите вместо английской, а обе
 * сразу, как в хендоффе.
 */
interface Props {
  onClick?: () => void;
  /** Подпись слота; по умолчанию — «выбери соперника» из макета. */
  label?: string;
  /** Слот, который сейчас заполняет пикер: акцентная рамка + `aria-current`. */
  active?: boolean;
}

const UI_H2HEmptySlot: React.FC<Props> = ({
  onClick, label = 'בחר יריב · choose a rival', active = false,
}) => (
  <button
    type="button"
    className={`h2h-slot${active ? ' h2h-slot--active' : ''}`}
    // Подпись пикера («choosing the LEFT swimmer») говорит то же словами; для читалки
    // экрана связь слота с выбором должна быть выражена и разметкой.
    aria-current={active ? 'true' : undefined}
    onClick={onClick}
  >
    <span className="h2h-slot__plus" aria-hidden="true">＋</span>
    <span dir="auto">{label}</span>
  </button>
);

export default UI_H2HEmptySlot;

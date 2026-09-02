import React from 'react';
import './h2h.css';
import type { H2HWinner } from './h2h.types';

/**
 * Строка сравнения статов (макет 1b, §2): значение · подпись · значение на той же сетке,
 * что шапка, поэтому цифры стоят ровно под своими карточками.
 *
 * Значения ПРИЖАТЫ К ЦЕНТРУ (левое вправо, правое влево) — так пара читается как одно
 * сравнение, а не как две колонки таблицы. Победитель красится cyan; у медалей победителя
 * нет (три числа сразу), поэтому там `winner` не передают.
 */
interface Props {
  label: string;
  left: React.ReactNode;
  right: React.ReactNode;
  winner?: H2HWinner;
  /** Значения — не числа, а собственная разметка (медали): выравнивание другим способом. */
  raw?: boolean;
}

const UI_H2HStatRow: React.FC<Props> = ({ label, left, right, winner = null, raw = false }) => (
  <div className="h2h-row">
    <span className={raw
      ? 'h2h-stat__medals h2h-stat__medals--left'
      : `h2h-stat__value h2h-stat__value--left${winner === 'left' ? ' h2h-stat__value--win' : ''}`}
    >
      {left}
    </span>
    <span className="h2h-stat__label">{label}</span>
    <span className={raw
      ? 'h2h-stat__medals h2h-stat__medals--right'
      : `h2h-stat__value h2h-stat__value--right${winner === 'right' ? ' h2h-stat__value--win' : ''}`}
    >
      {right}
    </span>
  </div>
);

export default UI_H2HStatRow;

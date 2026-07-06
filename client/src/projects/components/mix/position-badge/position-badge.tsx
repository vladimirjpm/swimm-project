import React from 'react';
import HelperMedal from '../../../../utils/helpers/helper-medal';

/**
 * Плоский цветной кружок позиции (как в прототипе): 1 золото / 2 серебро /
 * 3 бронза / дальше серый. Число белым. Это НЕ медаль-иконка (та осталась для
 * подсчёта медалей в деталях) — здесь плоский badge под макет.
 * Цвет медали — ТОЛЬКО если `isAward` (см. HelperMedal.getMedalTier): место без
 * награды красится нейтральным серым, даже если это 1/2/3 место.
 */
const TIER_COLORS: Record<'gold' | 'silver' | 'bronze', string> = {
  gold: '#F5B800',
  silver: '#B8BCC4',
  bronze: '#CD7F32',
};

interface UI_PositionBadgeProps {
  position?: string | number | null;
  fallbackIndex?: number; // если позиции нет — показать порядковый (index+1)
  size?: number;          // диаметр, px
  className?: string;
  /** Award-eligible ли соревнование/заплыв — без этого 1/2/3 место не красится медалью. */
  isAward: boolean;
}

const UI_PositionBadge: React.FC<UI_PositionBadgeProps> = ({
  position,
  fallbackIndex,
  size = 40,
  className = '',
  isAward,
}) => {
  const raw =
    position != null && position !== ''
      ? position
      : fallbackIndex != null
      ? fallbackIndex + 1
      : '';
  const tier = HelperMedal.getMedalTier(raw, isAward);

  // Медаль — заливка цветом, белая цифра. Обычное место (без награды) — заметно
  // светлее, с рамкой и приглушённым текстом, чтобы не путать с серебром.
  // Рамка через boxShadow (inset), а не border: свойство border занято внешним
  // className (напр. кольцо-разделитель у маленького бейджа пересчитанного места).
  const style = tier
    ? { width: size, height: size, background: TIER_COLORS[tier], fontSize: Math.round(size * 0.45) }
    : {
        width: size,
        height: size,
        background: 'var(--theme-mode-surface-alt)',
        boxShadow: 'inset 0 0 0 2.5px var(--theme-mode-text-muted)',
        fontSize: Math.round(size * 0.45),
      };
  const textColorClass = tier ? 'text-white' : 'text-[var(--theme-mode-text-muted)]';

  return (
    <div
      className={`rounded-full flex items-center justify-center font-bold leading-none ${textColorClass} ${className}`}
      style={style}
    >
      {raw !== '' ? raw : '—'}
    </div>
  );
};

export default UI_PositionBadge;

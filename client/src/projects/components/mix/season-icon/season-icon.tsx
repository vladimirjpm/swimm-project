import React from 'react';

export type Season = 'winter' | 'summer';

/** Годится ли значение (`kind` старта, `season` плитки) как сезон «зима / лето». */
export const isSeason = (value: string | null | undefined): value is Season => value === 'winter' || value === 'summer';

const LABEL: Record<Season, string> = { winter: 'Winter', summer: 'Summer' };

/**
 * Эмодзи с селектором эмодзи-вида U+FE0F: без него браузер вправе взять чёрно-белый текстовый
 * глиф («❄», «☀»), а нужен цветной.
 */
const EMOJI: Record<Season, string> = { winter: '❄️', summer: '☀️' };

/** Цветные эмодзи-шрифты систем — явно, чтобы значок не достался текстовому шрифту страницы. */
const EMOJI_FONT = '"Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", sans-serif';

export interface UI_SeasonIconProps {
  season: Season;
  /** Кегль значка: число — px. Не задан — значок берёт кегль окружающего текста (или класса). */
  size?: number | string;
  className?: string;
  style?: React.CSSProperties;
  /** Подпись для скринридера; не задана — значок декоративный (`aria-hidden`). */
  label?: string;
}

/**
 * Значок сезона «зима / лето» — эмодзи ❄️ и ☀️, ОДИН на весь проект (окончательное решение
 * Влада 13.11.2026: «символ-эмодзи ❄️ — на весь проект. как и солнце эмодзи»). Линейные
 * рисунки снежинки и солнца пробовали в тот же день и убрали: «нашей» Влад считает цветную
 * снежинку-эмодзи, которая всегда стояла в плитке соревнования. Рисунок не заводить.
 *
 * ⚠ Эмодзи рисует система: на разных устройствах и в скачанном PNG обложки и плаката он
 * выглядит по-разному — это принято осознанно. Цвет текста (`currentColor`) на цветной эмодзи
 * не действует; размер — кегль (`size` или класс).
 */
const UI_SeasonIcon: React.FC<UI_SeasonIconProps> = ({ season, size, className, style, label }) => (
  <span
    role={label ? 'img' : undefined}
    aria-label={label}
    aria-hidden={label ? undefined : true}
    className={className}
    style={{
      display: 'inline-block', flex: 'none', fontFamily: EMOJI_FONT, fontSize: size, fontStyle: 'normal',
      fontWeight: 400, lineHeight: 1, ...style,
    }}
  >
    {EMOJI[season]}
  </span>
);

/**
 * Значок роли старта в сезоне (`kind` из API): зима и лето — значок сезона, открытая вода —
 * волна 🌊 (она не сезон). Другое значение — ничего.
 */
export const UI_SeasonKindIcon: React.FC<{ kind: string | null | undefined; size?: number | string; labelled?: boolean }> = ({
  kind, size, labelled = false,
}) => {
  if (isSeason(kind)) return <UI_SeasonIcon season={kind} size={size} label={labelled ? LABEL[kind] : undefined} />;
  return kind === 'openwater' ? <span aria-hidden={labelled ? undefined : true}>🌊</span> : null;
};

export default UI_SeasonIcon;

import React from 'react';

export type Season = 'winter' | 'summer';

/** Годится ли значение (`kind` старта, `season` плитки) как сезон «зима / лето». */
export const isSeason = (value: string | null | undefined): value is Season => value === 'winter' || value === 'summer';

const LABEL: Record<Season, string> = { winter: 'Winter', summer: 'Summer' };

/** Снежинка: три оси и по «галочке» на концах вертикальной. */
const WINTER = (
  <>
    <path d="M12 3v18M4.2 7.5l15.6 9M4.2 16.5l15.6-9" />
    <path d="m9.5 4.5 2.5 2 2.5-2M9.5 19.5l2.5-2 2.5 2" />
  </>
);

/** Солнце: круг и восемь лучей. */
const SUMMER = (
  <>
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1" />
  </>
);

export interface UI_SeasonIconProps {
  season: Season;
  /** Сторона значка: число — px; по умолчанию `1em` — значок идёт в строке вместо глифа. */
  size?: number | string;
  className?: string;
  style?: React.CSSProperties;
  /** Подпись для скринридера; не задана — значок декоративный (`aria-hidden`). */
  label?: string;
}

/**
 * Значок сезона «зима / лето» — снежинка и солнце, ОДИН набор на весь проект (решение Влада
 * 13.11.2026: «наша снежинка и солнце это на весь проект и на все случаи. зачем плодить разные
 * картинки?»). Раньше сезон печатался глифом ❄ / ☀ или эмодзи, а они у каждой системы свои.
 *
 * Контурный рисунок 24×24, штрих `currentColor` — цвет задаёт родитель (акцент у зимы, золото
 * у лета, хексы макета на обложке и плакате). По умолчанию размер `1em`: в тексте значок
 * встаёт вместо буквы и растёт вместе с кеглем.
 */
const UI_SeasonIcon: React.FC<UI_SeasonIconProps> = ({ season, size = '1em', className, style, label }) => (
  <svg
    viewBox="0 0 24 24"
    width={size}
    height={size}
    fill="none"
    stroke="currentColor"
    strokeWidth={1.8}
    strokeLinecap="round"
    strokeLinejoin="round"
    role={label ? 'img' : undefined}
    aria-label={label}
    aria-hidden={label ? undefined : true}
    className={className}
    // В строке текста значок садится на базовую линию чуть ниже — как буква с выносным элементом.
    style={{ display: 'inline-block', flex: 'none', verticalAlign: '-0.125em', ...style }}
  >
    {season === 'winter' ? WINTER : SUMMER}
  </svg>
);

/**
 * Значок роли старта в сезоне (`kind` из API): зима и лето — рисунок сезона, открытая вода —
 * волна 🌊 (она не сезон и рисунка у неё нет). Другое значение — ничего.
 */
export const UI_SeasonKindIcon: React.FC<{ kind: string | null | undefined; size?: number | string; labelled?: boolean }> = ({
  kind, size, labelled = false,
}) => {
  if (isSeason(kind)) return <UI_SeasonIcon season={kind} size={size} label={labelled ? LABEL[kind] : undefined} />;
  return kind === 'openwater' ? <span aria-hidden={labelled ? undefined : true}>🌊</span> : null;
};

export default UI_SeasonIcon;

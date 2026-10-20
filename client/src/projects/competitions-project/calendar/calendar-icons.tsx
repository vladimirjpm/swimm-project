import React from 'react';

/*
 * Инлайн-SVG иконки календаря (без npm-зависимостей), нарисованы по мотивам Phosphor.
 * 24×24, штрих currentColor, без заливки — цвет задаёт родитель.
 */

interface IconProps {
  size?: number;
  className?: string;
}

function Svg({ size = 16, className, children }: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={className}
    >
      {children}
    </svg>
  );
}

export function IconCaretLeft(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M15 5 8 12l7 7" />
    </Svg>
  );
}

export function IconCaretRight(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="m9 5 7 7-7 7" />
    </Svg>
  );
}

/** All — четыре квадрата. */
export function IconSquaresFour(props: IconProps) {
  return (
    <Svg {...props}>
      <rect x="4" y="4" width="6.5" height="6.5" rx="1.5" />
      <rect x="13.5" y="4" width="6.5" height="6.5" rx="1.5" />
      <rect x="4" y="13.5" width="6.5" height="6.5" rx="1.5" />
      <rect x="13.5" y="13.5" width="6.5" height="6.5" rx="1.5" />
    </Svg>
  );
}

/** Pool — пловец: голова, рука вперёд и волна под ним. */
export function IconSwim(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx="16.5" cy="6" r="2" />
      <path d="m5 12 5-3 3.5 2.5 3-1.5" />
      <path d="M2.5 16c1.2 0 1.8.9 3 .9s1.8-.9 3-.9 1.8.9 3 .9 1.8-.9 3-.9 1.8.9 3 .9 1.8-.9 3-.9" />
      <path d="M2.5 20.5c1.2 0 1.8.8 3 .8s1.8-.8 3-.8 1.8.8 3 .8 1.8-.8 3-.8 1.8.8 3 .8 1.8-.8 3-.8" />
    </Svg>
  );
}

/** Open water — три волны. */
export function IconWaves(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M2.5 7c1.5 0 2.2 1.2 3.7 1.2S8.4 7 9.9 7s2.2 1.2 3.7 1.2S15.8 7 17.3 7s2.2 1.2 3.7 1.2" />
      <path d="M2.5 12c1.5 0 2.2 1.2 3.7 1.2S8.4 12 9.9 12s2.2 1.2 3.7 1.2S15.8 12 17.3 12s2.2 1.2 3.7 1.2" />
      <path d="M2.5 17c1.5 0 2.2 1.2 3.7 1.2S8.4 17 9.9 17s2.2 1.2 3.7 1.2S15.8 17 17.3 17s2.2 1.2 3.7 1.2" />
    </Svg>
  );
}

/** Artistic — цветок лотоса: центральный лепесток, два боковых и дуга чаши. */
export function IconLotus(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 4c2.6 2.4 3.6 5.2 3.6 8s-1.4 5.4-3.6 7.2C9.8 17.4 8.4 14.8 8.4 12S9.4 6.4 12 4Z" />
      <path d="M8.6 10.2C6 10 3.9 10.8 2.5 12.5c.9 3.6 3.6 6 7.5 6.6" />
      <path d="M15.4 10.2c2.6-.2 4.7.6 6.1 2.3-.9 3.6-3.6 6-7.5 6.6" />
    </Svg>
  );
}

/** Зима — снежинка (иконка сезона на бейдже группы). */
export function IconSnowflake(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 3v18M4.2 7.5l15.6 9M4.2 16.5l15.6-9" />
      <path d="m9.5 4.5 2.5 2 2.5-2M9.5 19.5l2.5-2 2.5 2" />
    </Svg>
  );
}

/** Лето — солнце: круг и восемь лучей. */
export function IconSun(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1" />
    </Svg>
  );
}

/**
 * Кубок — ЗАЛИТЫЙ (в 6–9px контур не читается): чаша с ручками, ножка и основание.
 * Остальные иконки файла — контурные, эта единственная с fill.
 */
export function IconTrophy({ size = 16, className }: IconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="currentColor"
      aria-hidden="true"
      className={className}
    >
      <path d="M6 3h12v6.5a6 6 0 0 1-12 0V3Z" />
      <path d="M6 5H2.5v2.2c0 2.2 1.6 3.8 4 4l.3-2c-1.1-.2-1.8-.9-1.8-2V7H6V5Zm12 0h3.5v2.2c0 2.2-1.6 3.8-4 4l-.3-2c1.1-.2 1.8-.9 1.8-2V7H18V5Z" />
      <path d="M10.8 15h2.4v3h-2.4zM7.5 18.5h9V21h-9z" />
    </svg>
  );
}

/** Раскрытие вниз (подпись месяца на телефоне). */
export function IconCaretDown(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="m5 9 7 7 7-7" />
    </Svg>
  );
}

/** Сворачивание вверх. */
export function IconCaretUp(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="m5 15 7-7 7 7" />
    </Svg>
  );
}

/** Воронка — кнопка Type на телефоне. */
export function IconFunnel(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M3.5 5h17l-6.5 7.8V19l-4 2v-8.2L3.5 5Z" />
    </Svg>
  );
}

/** Национальные рекорды — флаг на древке. */
export function IconFlag(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M5 21V4" />
      <path d="M5 4.5c3-1.6 5 1.6 8 0s4.5-.6 6 0v9c-1.5-.6-3-1.6-6 0s-5-1.6-8 0" />
    </Svg>
  );
}

/** Мировые рекорды — глобус. */
export function IconGlobe(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18" />
      <path d="M12 3c2.5 2.5 3.8 5.5 3.8 9S14.5 18.5 12 21c-2.5-2.5-3.8-5.5-3.8-9S9.5 5.5 12 3Z" />
    </Svg>
  );
}

/** Юниорские рекорды — звезда. */
export function IconStar(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="m12 3.5 2.6 5.4 5.9.8-4.3 4.1 1 5.8L12 16.8l-5.2 2.8 1-5.8L3.5 9.7l5.9-.8L12 3.5Z" />
    </Svg>
  );
}

/** Сравнение двух стран — две стрелки в разные стороны. */
export function IconArrowsLeftRight(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M4 8h15m-3.5-3.5L19 8l-3.5 3.5" />
      <path d="M20 16H5m3.5-3.5L5 16l3.5 3.5" />
    </Svg>
  );
}

/** Нормативы — линейка. */
export function IconRuler(props: IconProps) {
  return (
    <Svg {...props}>
      <rect x="2.5" y="8" width="19" height="8" rx="1.5" />
      <path d="M7 8v3M11 8v4M15 8v3M19 8v4" />
    </Svg>
  );
}

/** Результаты — клетчатый финишный флаг, ЗАЛИТЫЙ (как Phosphor `ph-fill ph-flag-checkered`). */
export function IconFlagCheckered({ size = 16, className }: IconProps) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor" aria-hidden="true" className={className}>
      <path d="M4 3h1.8v18H4z" />
      <path
        fillRule="evenodd"
        d="M6.5 4h14v10h-14V4Zm0 0h3.5v2.5H6.5V4Zm7 0H17v2.5h-3.5V4Zm-3.5 2.5h3.5V9H10V6.5Zm7 0h3.5V9H17V6.5ZM6.5 9H10v2.5H6.5V9Zm7 0H17v2.5h-3.5V9ZM10 11.5h3.5V14H10v-2.5Zm7 0h3.5V14H17v-2.5Z"
      />
    </svg>
  );
}

/** Стартлист — нумерованный список. */
export function IconListNumbers(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M10 6h10M10 12h10M10 18h10" />
      <path d="M4 5l1.5-1V9" />
      <path d="M3.8 13.2c.3-.8 2.7-.9 2.4.6-.2.8-2.4 2.2-2.4 2.2h2.6" />
    </Svg>
  );
}

/** Результаты скоро — песочные часы. */
export function IconHourglass(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M6 3h12M6 21h12" />
      <path d="M7 3h10l-5 9 5 9H7l5-9-5-9Z" />
    </Svg>
  );
}

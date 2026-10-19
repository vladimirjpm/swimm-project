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

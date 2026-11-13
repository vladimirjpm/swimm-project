import React, { useLayoutEffect, useRef } from 'react';

/** Пределы подгонки кегля, px. */
export interface FitSize {
  max: number;
  min: number;
}

/**
 * Строка, которая обязана поместиться: кегль уменьшается от `max` до `min`, пока текст не
 * влезет в отведённую ширину; если не влез и на `min` — при `wrap` переносится на вторую
 * строку. В макетах стоят короткие образцы («אליפות אילת הפתוחה», `1:37.05`, `AGE RECORD`),
 * а настоящие названия, время на 1500 м и `NATIONAL RECORD` длиннее.
 *
 * Кегль ставится в DOM напрямую, а не состоянием: это замер вёрстки, лишний рендер ему не
 * нужен. Картинка появляется только после загрузки шрифтов (`ensurePrintFonts`), поэтому замер
 * идёт настоящим шрифтом.
 */
export default function FitText({ text, size, wrap = false, dir, className, style }: {
  text: string;
  size: FitSize;
  wrap?: boolean;
  dir?: 'rtl' | 'ltr' | 'auto';
  className?: string;
  style?: React.CSSProperties;
}) {
  const ref = useRef<HTMLSpanElement>(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const overflows = () => el.scrollWidth > el.clientWidth + 1;
    let current = size.max;
    el.style.whiteSpace = 'nowrap';
    el.style.fontSize = `${current}px`;
    while (current > size.min && overflows()) {
      current -= 1;
      el.style.fontSize = `${current}px`;
    }
    if (wrap && overflows()) el.style.whiteSpace = 'normal';
  });
  return (
    <span
      ref={ref}
      dir={dir}
      className={className}
      style={{ display: 'block', maxWidth: '100%', overflow: 'hidden', ...style }}
    >
      {text}
    </span>
  );
}

import React from 'react';

/**
 * UI_BrandMark — знак SwimHub (логотип: шапочка, очки, две фигуры) квадратом со скруглёнными
 * углами. Единственный источник знака в интерфейсе: шапка страниц (`AppTopbar`) и шапка главной
 * (`SwimHubLogo`). До 15.10.2026 на этом месте была буква «S» в цветном квадрате.
 *
 * Картинка — `public/images/brand/swimhub-mark-96.png` (96 px, прозрачные углы: хватает на
 * экраны с плотностью до ×2.5 при знаке до 36 px). Исходник 1254 px и как пересобрать набор —
 * `!design_handoff/brand/`. Знак цветной и одинаковый в обеих темах: тёмно-синий квадрат читается
 * и на светлой, и на тёмной шапке, перекрашивать его ролями `--t-*` не нужно.
 *
 * Размер — Tailwind-классами в `className` (`h-[26px] w-[26px]`), как у остальных иконок. Рядом
 * всегда стоит слово SWIMHUB, поэтому картинка декоративная: `alt=""`.
 */
interface UI_BrandMarkProps {
  className?: string;
}

function UI_BrandMark({ className = 'h-[26px] w-[26px]' }: UI_BrandMarkProps) {
  return (
    <img
      src="/images/brand/swimhub-mark-96.png"
      alt=""
      aria-hidden="true"
      width={96}
      height={96}
      draggable={false}
      className={`shrink-0 select-none ${className}`}
    />
  );
}

export default UI_BrandMark;

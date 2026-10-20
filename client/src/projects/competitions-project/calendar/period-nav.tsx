import React from 'react';
import { IconCaretLeft, IconCaretRight } from './calendar-icons';

/*
 * Навигация по периоду: ‹ подпись ›. Один блок на месяц, сезон и неделю — подпись и шаг решает
 * вызывающий. Подпись можно заменить своим узлом (`children`): на телефоне в виде Month это
 * кнопка-тоггл разворота месяца.
 */

export function NavButton({
  label,
  disabled,
  onClick,
  size = 'md',
  children,
}: {
  label: string;
  disabled: boolean;
  onClick: () => void;
  size?: 'sm' | 'md';
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      aria-disabled={disabled || undefined}
      onClick={() => {
        if (!disabled) onClick();
      }}
      className={`cc-focus flex shrink-0 items-center justify-center border-0 bg-transparent p-0 text-[var(--t-accent-text)] ${
        size === 'sm' ? 'h-6 w-6 rounded-[6px]' : 'h-8 w-8 rounded-[8px]'
      } ${disabled ? 'cursor-not-allowed opacity-[.45]' : 'cursor-pointer'}`}
    >
      {children}
    </button>
  );
}

export interface PeriodNavProps {
  /** «October 2026» / «Season 2026/27» / «Week · 18–24 Oct». */
  title: string;
  onPrev: () => void;
  onNext: () => void;
  prevDisabled: boolean;
  nextDisabled: boolean;
  /** Что листаем — для подписей кнопок: «Previous month». */
  unit?: string;
  /** spread — подпись по центру, стрелки по краям (сайдбар, неделя); compact — всё рядом. */
  layout?: 'spread' | 'compact';
  size?: 'sm' | 'md';
  titleClassName?: string;
  className?: string;
  /** Своя подпись вместо `title` (кнопка-тоггл и т.п.). */
  children?: React.ReactNode;
}

function PeriodNav({
  title,
  onPrev,
  onNext,
  prevDisabled,
  nextDisabled,
  unit,
  layout = 'spread',
  size = 'md',
  titleClassName,
  className,
  children,
}: PeriodNavProps) {
  const icon = size === 'sm' ? 16 : 20;
  const suffix = unit ? ` ${unit}` : '';
  return (
    <div
      className={`flex items-center ${layout === 'spread' ? 'justify-between' : 'gap-1'} ${className ?? ''}`}
    >
      <NavButton label={`Previous${suffix}`} disabled={prevDisabled} onClick={onPrev} size={size}>
        <IconCaretLeft size={icon} />
      </NavButton>
      {children ?? (
        <span aria-live="polite" className={titleClassName ?? 'text-[15px] font-medium text-[var(--t-text)]'}>
          {title}
        </span>
      )}
      <NavButton label={`Next${suffix}`} disabled={nextDisabled} onClick={onNext} size={size}>
        <IconCaretRight size={icon} />
      </NavButton>
    </div>
  );
}

export default PeriodNav;

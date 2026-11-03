import React from 'react';
import { useMode } from '../../../../hooks/useMode';

/**
 * Переключатель Light/Dark — switch ☀/🌙 без текста, живёт в ШАПКЕ (решение Влада 03.11.2026),
 * а не плавающим кружком внизу справа: тот перекрывал оверлеи (шторку, модалки, лайтбокс), и
 * каждая страница с оверлеем обязана была прятать его сама.
 *
 * Где стоит: у гостя — рядом с Login, у вошедшего — строкой в меню аккаунта. Компонент один,
 * место выбирает шапка.
 *
 * Цвета задаёт ШАПКА: `className` — дорожка (рамка, фон), `knobClassName` — бегунок. У AppTopbar
 * это `--theme-topbar-*`, у шапки главной — роли `--t-*`. Бегунок несёт значок ТЕКУЩЕГО режима,
 * на дорожке бледно виден второй. Состояние — из useMode (localStorage + prefers-color-scheme).
 */
const UI_ModeToggle: React.FC<{ className?: string; knobClassName?: string }> = ({ className = '', knobClassName = '' }) => {
  const { mode, toggleMode } = useMode();
  const isDark = mode === 'dark';

  return (
    <button
      type="button"
      role="switch"
      aria-checked={isDark}
      aria-label="Dark theme"
      title={isDark ? 'Light theme' : 'Dark theme'}
      onClick={toggleMode}
      className={`relative flex h-[26px] w-[50px] shrink-0 items-center justify-between rounded-full border px-[6px] text-[11px] leading-none transition-colors ${className}`}
    >
      <span aria-hidden="true" className={isDark ? 'opacity-50' : 'invisible'}>☀️</span>
      <span aria-hidden="true" className={isDark ? 'invisible' : 'opacity-50'}>🌙</span>
      <span
        aria-hidden="true"
        className={`absolute left-[2px] top-1/2 flex h-[20px] w-[20px] -translate-y-1/2 items-center justify-center rounded-full text-[11px] shadow transition-transform duration-200 ${
          isDark ? 'translate-x-[24px]' : 'translate-x-0'
        } ${knobClassName}`}
      >
        {isDark ? '🌙' : '☀️'}
      </span>
    </button>
  );
};

export default UI_ModeToggle;

import React from 'react';
import { useMode } from '../../../../hooks/useMode';

/**
 * Минимальный переключатель Light/Dark (☀/🌙). Fixed внизу-справа.
 * Состояние берёт из useMode (localStorage + prefers-color-scheme), без Redux.
 *
 * ⚠ Отступ снизу — переменная `--mode-toggle-bottom` (умолчание 16px), а не класс:
 * у витрины низ экрана занят прибитой лентой рекордов, и кнопка ложилась прямо на неё.
 * Поднимает кнопку СТРАНИЦА, объявив переменную на своём корне (см. `home.css`), —
 * так значение живёт рядом с тем, что кнопка обходит, а не константой в JSX.
 * Инлайн-стиль, а не утилита: у Tailwind обе `bottom-*` одной специфичности, и кто
 * победит, решает порядок в бандле, а не порядок в строке класса.
 */
const UI_ModeToggle: React.FC = () => {
  const { mode, toggleMode } = useMode();
  const isDark = mode === 'dark';

  return (
    <button
      type="button"
      onClick={toggleMode}
      aria-label={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
      title={isDark ? 'Light theme' : 'Dark theme'}
      className="fixed right-4 z-[120] flex h-10 w-10 items-center justify-center rounded-full border text-lg shadow-lg backdrop-blur transition-colors bg-[var(--theme-mode-surface)] border-[var(--theme-mode-border)] text-[var(--theme-mode-text)] hover:brightness-95"
      style={{ bottom: 'var(--mode-toggle-bottom, 16px)' }}
    >
      {isDark ? '☀️' : '🌙'}
    </button>
  );
};

export default UI_ModeToggle;

import React from 'react';
import { useMode } from '../../../../hooks/useMode';

/**
 * Минимальный переключатель Light/Dark (☀/🌙). Fixed внизу-справа.
 * Состояние берёт из useMode (localStorage + prefers-color-scheme), без Redux.
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
      className="fixed bottom-4 right-4 z-[120] flex h-10 w-10 items-center justify-center rounded-full border text-lg shadow-lg backdrop-blur transition-colors bg-[var(--theme-mode-surface)] border-[var(--theme-mode-border)] text-[var(--theme-mode-text)] hover:brightness-95"
    >
      {isDark ? '☀️' : '🌙'}
    </button>
  );
};

export default UI_ModeToggle;

import { useCallback, useSyncExternalStore } from 'react';

/**
 * Ось режима Light/Dark — независима от data-theme (Training/Competition).
 * Ставит атрибут data-mode на <html>. Источник истины — localStorage['swimm-mode'];
 * при первом визите (нет сохранённого) — авто по системной prefers-color-scheme.
 *
 * Реализован как модуль-синглтон с подпиской (useSyncExternalStore), чтобы
 * переключатель (mode-toggle) и корневой useMode() всегда были синхронны.
 */

export type Mode = 'light' | 'dark';

const STORAGE_KEY = 'swimm-mode';

function detectInitialMode(): Mode {
  if (typeof window === 'undefined') return 'light';
  const saved = window.localStorage.getItem(STORAGE_KEY);
  if (saved === 'light' || saved === 'dark') return saved;
  // Первый визит: авто по системной теме
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

let currentMode: Mode = detectInitialMode();
const listeners = new Set<() => void>();

function applyMode(mode: Mode): void {
  if (typeof document !== 'undefined') {
    document.documentElement.dataset.mode = mode;
  }
}

// Применяем сразу при импорте модуля — до первого рендера, чтобы не было вспышки
applyMode(currentMode);

function setMode(mode: Mode): void {
  if (mode === currentMode) return;
  currentMode = mode;
  if (typeof window !== 'undefined') {
    window.localStorage.setItem(STORAGE_KEY, mode);
  }
  applyMode(mode);
  listeners.forEach((listener) => listener());
}

function subscribe(callback: () => void): () => void {
  listeners.add(callback);
  return () => {
    listeners.delete(callback);
  };
}

function getSnapshot(): Mode {
  return currentMode;
}

export const useMode = () => {
  const mode = useSyncExternalStore(subscribe, getSnapshot, () => 'light' as Mode);

  const toggleMode = useCallback(() => {
    setMode(currentMode === 'dark' ? 'light' : 'dark');
  }, []);

  return { mode, toggleMode, setMode };
};

export default useMode;

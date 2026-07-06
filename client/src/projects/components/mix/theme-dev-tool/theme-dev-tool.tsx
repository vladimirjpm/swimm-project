import React, { useState } from 'react';
import { useMode } from '../../../../hooks/useMode';

/**
 * DEV-инструмент «Themes» — плавающая панель для быстрого переключения тем.
 * Показывается ТОЛЬКО если в URL есть параметр ?themes (иначе рендерит null).
 * Это инструмент для разработки/превью, не для прода.
 *
 * MODE   — data-mode (light/dark), через useMode (persist в localStorage).
 * CONTEXT — data-theme на <html> напрямую (accent-ось). Держится до смены
 *   activity_type/фильтров (useTheme перечитывает data-theme от activity_type).
 */

const TRAINING = ['training-dashboard', 'training-nexaverse', 'training-ocean'];
const COMPETITION = ['competition-emerald', 'competition-blue', 'competition-warm', 'competition-dark'];

function isEnabled(): boolean {
  if (typeof window === 'undefined') return false;
  return new URLSearchParams(window.location.search).has('themes');
}

const UI_ThemeDevTool: React.FC = () => {
  const { mode, setMode } = useMode();
  const [open, setOpen] = useState(true);
  const [theme, setTheme] = useState<string>(
    () => (typeof document !== 'undefined' ? document.documentElement.dataset.theme || 'competition-emerald' : '')
  );

  if (!isEnabled()) return null;

  const applyTheme = (t: string) => {
    document.documentElement.dataset.theme = t;
    setTheme(t);
  };

  const label = 'text-[11px] font-extrabold uppercase tracking-[0.08em] text-[var(--theme-mode-text-muted)]';
  const bigSeg = (active: boolean) =>
    `flex-1 px-3 py-1.5 rounded-lg text-sm font-bold border transition-colors ${
      active
        ? 'bg-[var(--theme-primary)] text-white border-transparent'
        : 'bg-[var(--theme-mode-input-bg)] text-[var(--theme-mode-text)] border-[var(--theme-mode-border)]'
    }`;
  const chip = (active: boolean) =>
    `px-2 py-1 rounded-md text-[11px] font-semibold border transition-colors ${
      active
        ? 'bg-[var(--theme-primary)] text-white border-transparent'
        : 'bg-[var(--theme-mode-input-bg)] text-[var(--theme-mode-text)] border-[var(--theme-mode-border)]'
    }`;

  return (
    <div className="fixed bottom-4 left-4 z-[200]">
      {open ? (
        <div className="w-64 rounded-2xl p-4 shadow-2xl bg-[var(--theme-mode-drawer-bg)] border border-[var(--theme-mode-border)] text-[var(--theme-mode-text)]">
          <div className="flex items-center justify-between mb-3">
            <span className={label}>Theme tool</span>
            <button onClick={() => setOpen(false)} className="text-[var(--theme-mode-text-muted)] hover:text-[var(--theme-mode-text)] text-lg leading-none" aria-label="Collapse">×</button>
          </div>

          <div className="mb-3">
            <div className={`${label} mb-1.5`}>Mode</div>
            <div className="flex gap-2">
              <button className={bigSeg(mode === 'light')} onClick={() => setMode('light')}>Light</button>
              <button className={bigSeg(mode === 'dark')} onClick={() => setMode('dark')}>Dark</button>
            </div>
          </div>

          <div>
            <div className={`${label} mb-1.5`}>Context · accent</div>
            <div className="flex flex-wrap gap-1.5 mb-1.5">
              {TRAINING.map((t) => (
                <button key={t} className={chip(theme === t)} onClick={() => applyTheme(t)}>
                  {t.replace('training-', 'tr·')}
                </button>
              ))}
            </div>
            <div className="flex flex-wrap gap-1.5">
              {COMPETITION.map((t) => (
                <button key={t} className={chip(theme === t)} onClick={() => applyTheme(t)}>
                  {t.replace('competition-', 'co·')}
                </button>
              ))}
            </div>
          </div>

          <div className="mt-3 text-[10px] leading-snug text-[var(--theme-mode-text-muted)]">
            Mode → light/dark поверхности; Context → только акцент. Dev-инструмент (?themes).
          </div>
        </div>
      ) : (
        <button
          onClick={() => setOpen(true)}
          className="rounded-full px-4 py-2 shadow-lg bg-[var(--theme-mode-drawer-bg)] border border-[var(--theme-mode-border)] text-[var(--theme-mode-text)] text-sm font-semibold"
        >
          🎨 Themes
        </button>
      )}
    </div>
  );
};

export default UI_ThemeDevTool;

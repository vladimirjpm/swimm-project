import React from 'react';
import { buildResultsGridTemplate } from './types';

interface ResultsHeaderProps {
  view: 'mobile' | 'desktop' | '2xl';
  showClub: boolean;
  showEvent: boolean;
  showPoolType: boolean;
  showDate: boolean;
  hasInternationalPoints: boolean;
  /** Мобильный вид: развёрнуты ли все строки по умолчанию (level/pts/date) */
  showAllOpen?: boolean;
  onToggleShowAllOpen?: () => void;
  /** Режим «добавление видео к заплыву» (тумблер в шапке — только для залогиненных) */
  addVideoMode?: boolean;
  onToggleAddVideoMode?: () => void;
}

const ResultsHeader: React.FC<ResultsHeaderProps> = ({
  view,
  showClub,
  showEvent,
  showPoolType,
  showDate,
  hasInternationalPoints,
  showAllOpen,
  onToggleShowAllOpen,
  addVideoMode,
  onToggleAddVideoMode,
}) => {
  if (view === 'mobile') {
    return (
      <div className="w-full flex items-center justify-center gap-3 relative px-3 py-2">
        <div className="text-xl font-bold">Results</div>
        {/* Скрыто по просьбе — может понадобиться снова, не удалять.
        {onToggleShowAllOpen && (
          <label className="absolute right-3 flex items-center gap-1.5 text-xs font-semibold text-[var(--theme-mode-text-muted)] cursor-pointer">
            <input
              type="checkbox"
              checked={!!showAllOpen}
              onChange={onToggleShowAllOpen}
              className="w-3.5 h-3.5"
            />
            Show all open
          </label>
        )}
        */}
        {/* Тумблер «Add video mode» — рендерим только когда колбэк передан (признак «залогинен») */}
        {onToggleAddVideoMode && (
          <button
            type="button"
            title="Add video mode"
            onClick={onToggleAddVideoMode}
            className={`absolute right-3 flex items-center justify-center w-7 h-7 rounded-full text-sm transition-colors ${
              addVideoMode
                ? 'bg-[var(--theme-primary)] text-[var(--theme-mode-accent-text)]'
                : 'bg-[var(--theme-mode-surface)] text-[var(--theme-mode-text-muted)]'
            }`}
          >
            🎥
          </button>
        )}
      </div>
    );
  }

  if (view === 'desktop') {
    const gridTemplate = buildResultsGridTemplate({ showClub, showEvent, showPoolType, showDate, hasInternationalPoints });
    const lbl = 'text-[10px] font-extrabold uppercase tracking-[0.08em] text-[var(--theme-mode-text-muted)]';
    return (
      <div className="grid gap-[14px] px-6 py-[13px] items-center bg-[var(--theme-mode-surface-alt)] border-b border-[var(--theme-mode-border)] border-l-4 border-l-transparent" style={{ gridTemplateColumns: gridTemplate }}>
        {onToggleAddVideoMode ? (
          <button
            type="button"
            title="Add video mode"
            onClick={onToggleAddVideoMode}
            className={`flex items-center justify-center w-6 h-6 rounded-full text-xs transition-colors ${
              addVideoMode
                ? 'bg-[var(--theme-primary)] text-[var(--theme-mode-accent-text)]'
                : 'bg-transparent text-[var(--theme-mode-text-muted)]'
            }`}
          >
            🎥
          </button>
        ) : (
          <div aria-hidden />
        )}
        <div className={`${lbl} text-center`}>Pos</div>
        <div className={lbl}>Swimmer</div>
        {showClub && <div className={`${lbl} text-center`}>Club</div>}
        {(showEvent || showPoolType) && <div className={lbl}>Style</div>}
        <div className={`${lbl} text-right`}>Time</div>
        <div className={`${lbl} text-center`}>Level</div>
        {hasInternationalPoints && <div className={`${lbl} text-right`}>Pts</div>}
      </div>
    );
  }

  // 2xl view
  return (
    <div className="grid grid-cols-12 gap-2 px-4 py-2 font-bold items-center">
      <div className="col-span-1">Pos</div>
      <div className="col-span-3">Name</div>
      {showClub && <div className="col-span-1">Club</div>}
      {showEvent && <div className="col-span-2">Event</div>}
      <div className="col-span-1">Time</div>
      {hasInternationalPoints && <div className="col-span-1">Points</div>}
      <div className="col-span-1 text-center">Level</div>
      {showDate && <div className="col-span-1 text-center">Date</div>}
    </div>
  );
};

export default ResultsHeader;

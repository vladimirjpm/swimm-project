import React from 'react';
import { buildResultsGridTemplate } from './types';

interface ResultsHeaderProps {
  view: 'mobile' | 'desktop' | '2xl';
  showClub: boolean;
  showEvent: boolean;
  showPoolType: boolean;
  showDate: boolean;
  hasInternationalPoints: boolean;
}

const ResultsHeader: React.FC<ResultsHeaderProps> = ({
  view,
  showClub,
  showEvent,
  showPoolType,
  showDate,
  hasInternationalPoints,
}) => {
  if (view === 'mobile') {
    return (
      <div className="w-full text-center text-xl font-bold">Results</div>
    );
  }

  if (view === 'desktop') {
    const gridTemplate = buildResultsGridTemplate({ showClub, showEvent, showPoolType, showDate, hasInternationalPoints });
    const lbl = 'text-[10px] font-extrabold uppercase tracking-wider text-[var(--theme-mode-text-muted)]';
    return (
      <div className="grid gap-3 px-6 py-3 items-center" style={{ gridTemplateColumns: gridTemplate }}>
        <div className={`${lbl} text-center`}>Pos</div>
        <div className={lbl}>Swimmer</div>
        {showClub && <div className={lbl}>Club</div>}
        {(showEvent || showPoolType) && <div className={lbl}>Style</div>}
        <div className={`${lbl} text-right`}>Time</div>
        <div className={`${lbl} text-center`}>Level</div>
        {showDate && <div className={`${lbl} text-center`}>Date</div>}
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

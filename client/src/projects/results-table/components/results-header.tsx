import React from 'react';

interface ResultsHeaderProps {
  view: 'mobile' | 'desktop' | '2xl';
  showClub: boolean;
  showEvent: boolean;
  showDate: boolean;
  hasInternationalPoints: boolean;
}

const ResultsHeader: React.FC<ResultsHeaderProps> = ({
  view,
  showClub,
  showEvent,
  showDate,
  hasInternationalPoints,
}) => {
  if (view === 'mobile') {
    return (
      <div className="w-full text-center text-xl font-bold">Results</div>
    );
  }

  if (view === 'desktop') {
    return (
      <div className="grid grid-cols-12 gap-2 px-4 py-2 font-bold items-center">
        <div className="col-span-1">Pos</div>
        <div className="col-span-4">Name</div>
        {showClub && <div className="col-span-1">Club</div>}
        {showEvent && <div className="col-span-2">Event</div>}
        <div className="col-span-2">Time</div>
        <div className="col-span-2">Level</div>
        {showDate && <div className="col-span-1 text-center">Date</div>}
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
      <div className="col-span-1">Level</div>
      <div className="col-span-1">Process</div>
      {showDate && <div className="col-span-1 text-center">Date</div>}
    </div>
  );
};

export default ResultsHeader;

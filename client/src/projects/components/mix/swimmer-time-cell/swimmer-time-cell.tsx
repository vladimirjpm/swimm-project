import React, { useState } from 'react';
import '../text-effect/text-effect.css';

const TIME_SPLIT_SEPARATOR = '›';

interface UI_SwimmerTimeCellProps {
  time: string;
  time_split: string;
  time_fail: boolean;
  time_fail_note: string | null;
  firstLineClassName?: string;
  secondLineClassName?: string;
  className?: string;
  isRecordHolder?: boolean;
}

const UI_SwimmerTimeCell: React.FC<UI_SwimmerTimeCellProps> = ({
  time,
  time_split,
  time_fail,
  time_fail_note,
  firstLineClassName = 'text-xl font-bold',
  secondLineClassName = 'text-xs',
  className = '',
  isRecordHolder = false,
}) => {
  const [splitOpen, setSplitOpen] = useState(false);

  const formattedTimeSplit = time_split
    ? time_split
        .split(';')
        .map((s) => s.trim())
        .filter(Boolean)
        .join(` ${TIME_SPLIT_SEPARATOR} `)
    : '';

  const handleToggle = (e: React.MouseEvent) => {
    e.stopPropagation();
    setSplitOpen((prev) => !prev);
  };

  return (
    <div className={className}>
      {isRecordHolder && (
        <div className="mb-1 flex">
          <span
            className="inline-flex items-center gap-1 rounded-[7px] px-1.5 py-0.5 text-[8.5px] font-extrabold tracking-wide text-white"
            style={{ background: 'linear-gradient(135deg,#f0b429,#c8860a)', boxShadow: '0 2px 8px rgba(200,134,10,0.45)' }}
          >
            ★ NEW RECORD
          </span>
        </div>
      )}
      {formattedTimeSplit ? (
        <div
          className={`${firstLineClassName} flex items-center gap-1 cursor-pointer select-none`}
          onClick={handleToggle}
        >
          {time}
          {time_fail && <span className="text-red-500 ml-1">*</span>}
          <span
            className={`theme-text-muted transition-transform duration-200 ${splitOpen ? 'rotate-180' : ''}`}
            style={{ fontSize: '10px', lineHeight: 1 }}
          >▾</span>
        </div>
      ) : (
        <div className={firstLineClassName}>
          {time}
          {time_fail && <span className="text-red-500 ml-1">*</span>}
        </div>
      )}
      {time_fail_note && (
        <div className={`text-red-500 ${secondLineClassName}`}>{time_fail_note}</div>
      )}
      {formattedTimeSplit && splitOpen && (
        <div className={`theme-text-muted flex whitespace-nowrap ${secondLineClassName}`}>{formattedTimeSplit}</div>
      )}
    </div>
  );
};

export default UI_SwimmerTimeCell;

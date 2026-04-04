import React from 'react';
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
  const formattedTimeSplit = time_split
    ? time_split
        .split(';')
        .map((s) => s.trim())
        .filter(Boolean)
        .join(` ${TIME_SPLIT_SEPARATOR} `)
    : '';

  return (
    <div className={`${isRecordHolder ? 'glow-div flex justify-center' : ''}`}>
    <div className={`${isRecordHolder ? `${className} px-2 rounded-lg` : className}`}>
      {isRecordHolder && <div className="text-xs text-center font-semibold mt-0.5">Record</div>}
      <div className={firstLineClassName}>
        {time}
        {time_fail && <span className="text-red-500 ml-1">*</span>}
      </div>
      {time_fail_note && (
        <div className={`text-red-500 ${secondLineClassName}`}>{time_fail_note}</div>
      )}
      {formattedTimeSplit && (
        <div className={`theme-text-muted flex whitespace-nowrap ${secondLineClassName}`}>{formattedTimeSplit}</div>
      )}
    </div>
    </div>
  );
};

export default UI_SwimmerTimeCell;

import React from 'react';

const TIME_SPLIT_SEPARATOR = '›';

interface UI_SwimmerTimeCellProps {
  time: string;
  time_split: string;
  time_fail: boolean;
  time_fail_note: string | null;
  firstLineClassName?: string;
  secondLineClassName?: string;
  className?: string;
}

const UI_SwimmerTimeCell: React.FC<UI_SwimmerTimeCellProps> = ({
  time,
  time_split,
  time_fail,
  time_fail_note,
  firstLineClassName = 'text-xl font-bold',
  secondLineClassName = 'text-xs',
  className = '',
}) => {
  const formattedTimeSplit = time_split
    ? time_split
        .split(';')
        .map((s) => s.trim())
        .filter(Boolean)
        .join(` ${TIME_SPLIT_SEPARATOR} `)
    : '';

  return (
    <div className={className}>
      <div className={firstLineClassName}>
        {time}
        {time_fail && <span className="text-red-500 ml-1">*</span>}
      </div>
      {time_fail_note && (
        <div className={`text-red-500 ${secondLineClassName}`}>{time_fail_note}</div>
      )}
      {formattedTimeSplit && (
        <div className={`text-red-500 flex ${secondLineClassName}`}>{formattedTimeSplit}</div>
      )}
    </div>
  );
};

export default UI_SwimmerTimeCell;

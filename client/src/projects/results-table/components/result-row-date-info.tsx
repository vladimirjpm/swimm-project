import React from 'react';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';

interface ResultRowDateInfoProps {
  date: string;
  competition?: string;
  /** Название соревнования показываем только если загружено больше одного соревнования */
  showCompetition?: boolean;
  className?: string;
}

/** Дата (+ название соревнования, если их несколько в текущей выборке) под именем пловца в строке результата. */
const ResultRowDateInfo: React.FC<ResultRowDateInfoProps> = ({
  date,
  competition,
  showCompetition,
  className = '',
}) => (
  <div className={`flex items-center gap-1 min-w-0 ${className}`}>
    <UI_DateIcon
      styleType="row-style-1"
      date={date}
      fontClassName="text-[var(--theme-mode-text-muted)] text-xs"
    />
    {showCompetition && competition && (
      <span className="truncate text-xs text-[var(--theme-mode-text-muted)]">· {competition}</span>
    )}
  </div>
);

export default ResultRowDateInfo;

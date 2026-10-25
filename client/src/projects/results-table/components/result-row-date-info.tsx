import React from 'react';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_CompetitionName from '../../components/mix/competition-name/competition-name';

interface ResultRowDateInfoProps {
  date: string;
  competition?: string;
  /** Чемпионат — 🏆 у названия (`UI_CompetitionName`). */
  isChampionship?: boolean;
  /** Название соревнования показываем только если загружено больше одного соревнования */
  showCompetition?: boolean;
  className?: string;
}

/** Дата (+ название соревнования, если их несколько в текущей выборке) под именем пловца в строке результата. */
const ResultRowDateInfo: React.FC<ResultRowDateInfoProps> = ({
  date,
  competition,
  isChampionship = false,
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
      <>
        <span className="shrink-0 text-xs text-[var(--theme-mode-text-muted)]">·</span>
        {/* Обрезает сам компонент: многоточие в конце ивритского названия, 🏆 не срезается. */}
        <UI_CompetitionName
          className="min-w-0 truncate text-xs text-[var(--theme-mode-text-muted)]"
          name={competition}
          isChampionship={isChampionship}
        />
      </>
    )}
  </div>
);

export default ResultRowDateInfo;

import React from 'react';
import UI_SwimTime from '../../components/mix/swim-time/swim-time';
import { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import type { RecordsRankingWorld } from '../../../hooks/useRecordsRanking';
import { disciplineLabel, type RkFilters } from '../rk-disciplines';

/**
 * Мировой рекорд дисциплины — эталон, от которого считается весь рейтинг.
 *
 * Отдельной карточкой, а не первой строкой таблицы: мир это не участник рейтинга стран, и
 * строкой наравне с ними он бы означал, что «мир» где-то занял первое место. Заодно видно,
 * от чего считается «+0.08» у каждой страны.
 */

interface Props {
  world?: RecordsRankingWorld | null;
  filters: RkFilters;
}

const RkWorldCard: React.FC<Props> = ({ world, filters }) => {
  if (!world) {
    return (
      <div className="rk-world rk-world--none">
        <div className="rk-world__label">World record</div>
        <div className="rk-world__empty">
          not kept for this event — World Aquatics has no world record here, so no gaps are shown
        </div>
      </div>
    );
  }

  const quality = world.issue_reason ? { kind: 'record' as const, reason: world.issue_reason } : null;

  return (
    <div className="rk-world" {...swimFlaggedRowProps(quality)}>
      <div className="rk-world__label">World record · {disciplineLabel(filters)}</div>
      <div className="rk-world__body">
        <UI_SwimTime
          time={world.time}
          quality={quality}
          marker="chip"
          chipSize="md"
          className="rk-world__time src-rk-world-card"
        />
        <div className="rk-world__who">
          {world.holder_name?.trim() || <span className="rk-dash">—</span>}
          {world.record_date && <span className="rk-world__date">{world.record_date}</span>}
        </div>
      </div>
    </div>
  );
};

export default RkWorldCard;

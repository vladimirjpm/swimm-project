import React from 'react';
import { champMark } from './calendar-model';
import type { CalendarMeet } from './calendar-types';
import GroupBadge from './group-badge';

/*
 * Метки meets дня на телефоне (ячейка недели/месяца и тайл ленты Meet): до 4 в строку высотой
 * 18px. Обычный meet — точка 5px; чемпионат бассейна с группой — мини-бейдж 14×18;
 * чемпионат без бейджа (море, артистика) — точка 7px золотой рамки.
 */
function DayMarks({ meets }: { meets: CalendarMeet[] }) {
  return (
    <span className="flex h-[18px] items-center justify-center gap-[2px]" aria-hidden="true">
      {meets.slice(0, 4).map((meet) => {
        const mark = meet.is_championship ? champMark(meet) : null;
        if (mark?.kind === 'badge') return <GroupBadge key={meet.key} group={mark.group} size="dot" />;
        if (mark) {
          return <span key={meet.key} className="h-[7px] w-[7px] rounded-full bg-[var(--t-champ-border)]" />;
        }
        return <span key={meet.key} className="h-[5px] w-[5px] rounded-full bg-[var(--t-text-3)]" />;
      })}
    </span>
  );
}

export default DayMarks;

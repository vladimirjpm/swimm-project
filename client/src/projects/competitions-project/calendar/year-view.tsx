import React from 'react';
import { IconTrophy } from './calendar-icons';
import { champMark, isCurrentMonth, isPastMonth, meetInMonth, monthKey, monthLabel, monthShort, seasonMonths } from './calendar-model';
import type { CalendarMeet, CalendarMonth, MeetType } from './calendar-types';
import GroupBadge from './group-badge';

/*
 * Вид Full year: 12 карточек-месяцев сезона (Sep→Aug). `meets` — meets сезона уже после
 * фильтра по типу. Правило «meet в месяце» общее со счётчиками (calendar-model.meetInMonth).
 */

const TYPE_ROWS: { type: MeetType; label: string }[] = [
  { type: 'pool', label: 'Swimming' },
  { type: 'open_water', label: 'Open water' },
  { type: 'artistic', label: 'Artistic' },
];

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

function MonthCard({
  month,
  meets,
  today,
  onPick,
}: {
  month: CalendarMonth;
  meets: CalendarMeet[];
  today: string;
  onPick: (m: CalendarMonth) => void;
}) {
  const current = isCurrentMonth(month, today);
  const past = isPastMonth(month, today);
  const champs = meets.filter((m) => m.is_championship);

  const label = [
    `${monthLabel(month)} — ${plural(meets.length, 'meet', 'meets')}`,
    ...(champs.length > 0 ? [plural(champs.length, 'championship', 'championships')] : []),
  ].join(', ');

  return (
    <button
      type="button"
      aria-label={label}
      onClick={() => onPick(month)}
      className={`cc-focus flex cursor-pointer flex-col gap-2 rounded-[14px] border px-[14px] py-3 text-left shadow-[var(--t-shadow)] backdrop-blur-[14px] hover:bg-[var(--t-nested)] ${
        current
          ? 'border-[var(--t-accent)] bg-[var(--t-nested)]'
          : 'border-[var(--t-border-2)] bg-[var(--t-surface)]'
      } ${past ? 'opacity-60' : ''}`}
    >
      <span className="flex items-baseline justify-between">
        <span className="flex items-baseline gap-[6px]">
          <span className="text-[17px] font-semibold text-[var(--t-text)]">{monthShort(month)}</span>
          <span className="text-[11px] text-[var(--t-text-2)]">{month.year}</span>
        </span>
        <span className="text-[18px] font-bold text-[var(--t-accent-text)]">{meets.length}</span>
      </span>

      <span className="flex flex-col gap-[3px]">
        {TYPE_ROWS.map(({ type, label: rowLabel }) => {
          const n = meets.filter((m) => m.type === type).length;
          const hasChamp = champs.some((m) => m.type === type);
          return (
            <span
              key={type}
              className={`grid grid-cols-[minmax(0,1fr)_auto] items-center gap-2 text-[12px] ${n === 0 ? 'opacity-[.35]' : ''}`}
            >
              <span className="flex min-w-0 items-center gap-1 text-[var(--t-text-2)]">
                <span className="truncate">{rowLabel}</span>
                {hasChamp && <IconTrophy size={13} className="shrink-0 text-[var(--t-champ-ink)]" />}
              </span>
              <span className="text-[var(--t-text)]">{n}</span>
            </span>
          );
        })}
      </span>

      {champs.length > 0 ? (
        <span className="flex min-h-[29px] flex-wrap items-center gap-1 rounded-[6px] border border-[var(--t-champ-strip-border)] bg-[var(--t-champ-strip-bg)] px-[6px] py-[5px]">
          {champs.map((meet) => {
            const mark = champMark(meet);
            return mark.kind === 'badge' ? (
              <GroupBadge key={meet.key} group={mark.group} size="mini" />
            ) : (
              <span
                key={meet.key}
                className="flex min-w-0 items-center gap-1 text-[11px] font-medium text-[var(--t-champ-ink)]"
              >
                <IconTrophy size={11} className="shrink-0" />
                {mark.text}
              </span>
            );
          })}
        </span>
      ) : (
        <span className="min-h-[29px] border-t border-[var(--t-border-2)] pt-[6px] text-[11px] text-[var(--t-text-3)]">
          No championships
        </span>
      )}
    </button>
  );
}

function YearView({
  season,
  meets,
  today,
  onPickMonth,
}: {
  season: number;
  meets: CalendarMeet[];
  today: string;
  onPickMonth: (m: CalendarMonth) => void;
}) {
  return (
    <div className="grid grid-cols-2 gap-[10px] lg:grid-cols-4">
      {seasonMonths(season).map((m) => (
        <MonthCard
          key={monthKey(m)}
          month={m}
          meets={meets.filter((meet) => meetInMonth(meet, m))}
          today={today}
          onPick={onPickMonth}
        />
      ))}
    </div>
  );
}

export default YearView;

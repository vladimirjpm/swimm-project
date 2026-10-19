import React from 'react';
import { IconTrophy } from './calendar-icons';
import { actionLabel, champMark, dateRangeLabel, dateTile, meetHref, statusTag, type StatusTone } from './calendar-model';
import type { CalendarMeet } from './calendar-types';
import GroupBadge from './group-badge';

/*
 * Куб соревнования (база, Б4): дата-плитка + имя + мета + статус + действие. Весь куб — ссылка.
 * `today` пока не используется — понадобится Б5 (полоса дней многодневки: сегодня/прошёл).
 * Полоса дней многодневки, 2 колонки и вид Cancelled/Postponed добавляет Б5 — тег статуса
 * для них уже отдаётся `statusTag`.
 */

const TAG_CLASS: Record<StatusTone, string> = {
  live: 'border border-[var(--t-live)] text-[var(--t-live)]',
  startlist: 'bg-[var(--t-accent-soft)] text-[var(--t-accent-text)]',
  progress: 'border border-[var(--t-accent)] text-[var(--t-accent-text)]',
  complete: 'bg-[var(--t-nested)] text-[var(--t-text-2)]',
  cancelled: 'border border-[var(--t-danger)] text-[var(--t-danger)]',
  postponed: 'border border-[var(--t-border)] text-[var(--t-text-2)]',
};

function MeetCube({ meet }: { meet: CalendarMeet; today?: string }) {
  const tile = dateTile(meet);
  const mark = meet.is_championship ? champMark(meet) : null;
  const tag = statusTag(meet);
  const action = actionLabel(meet);
  const meta = [meet.pool, meet.venue, meet.group?.label].filter(Boolean) as string[];

  return (
    <a
      href={meetHref(meet)}
      aria-label={`${meet.name}, ${dateRangeLabel(meet)}`}
      className={`cc-focus flex flex-col overflow-hidden rounded-[14px] border border-[var(--t-border-2)] text-inherit no-underline shadow-[var(--t-shadow)] backdrop-blur-[14px] lg:min-h-[180px] ${
        mark ? 'cc-cube--champ' : 'bg-[var(--t-surface)] hover:bg-[var(--t-nested)]'
      }`}
    >
      <span className="flex items-start justify-between">
        <span className="flex min-w-[64px] flex-col items-center gap-px rounded-br-[10px] border-b border-r border-[var(--t-border-2)] px-[14px] py-2">
          <span className="text-[10px] uppercase tracking-[.08em] text-[var(--t-text-2)]">{tile.wd}</span>
          <span className="text-[20px] font-semibold leading-tight text-[var(--t-text)]">{tile.day}</span>
          <span className="text-[11px] text-[var(--t-accent-text)]">{tile.mon}</span>
        </span>
        {mark && (
          <span className="flex max-w-[60%] justify-end pr-[10px] pt-[10px]">
            {mark.kind === 'badge' ? (
              <GroupBadge group={mark.group} size="full" />
            ) : (
              <span className="flex items-center gap-1 text-[11px] font-medium text-[var(--t-champ-ink)]">
                <IconTrophy size={11} className="shrink-0" />
                {mark.text}
              </span>
            )}
          </span>
        )}
      </span>

      <span className="flex flex-1 flex-col gap-[6px] px-[14px] pb-[14px] pt-3">
        <span
          dir="auto"
          className="cc-clamp2 text-left text-[14px] font-semibold leading-[1.35] text-[var(--t-text)]"
        >
          {meet.name}
        </span>
        {meta.length > 0 && (
          <span className="text-[12px] text-[var(--t-text-2)]">
            {meta.map((part, i) => (
              <React.Fragment key={i}>
                {i > 0 && ' · '}
                {part === meet.venue ? <span dir="auto">{part}</span> : part}
              </React.Fragment>
            ))}
          </span>
        )}
        <span className="mt-auto flex flex-wrap items-center justify-between gap-[6px] pt-[6px]">
          {tag ? (
            <span className={`rounded-[6px] px-2 py-[3px] text-[11px] font-semibold ${TAG_CLASS[tag.tone]}`}>
              {tag.text}
            </span>
          ) : (
            <span />
          )}
          {action && <span className="text-[12px] font-semibold text-[var(--t-accent-text)]">{action}</span>}
        </span>
      </span>
    </a>
  );
}

export default MeetCube;

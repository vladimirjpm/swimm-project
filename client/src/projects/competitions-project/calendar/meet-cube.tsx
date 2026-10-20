import React from 'react';
import { IconTrophy } from './calendar-icons';
import {
  actionLabel,
  champMark,
  cubeAction,
  dateRangeLabel,
  dateTile,
  isWideCube,
  meetLink,
  shortDate,
  statusTag,
  type StatusTone,
} from './calendar-model';
import type { CalendarMeet } from './calendar-types';
import GroupBadge from './group-badge';
import { StatusButton } from './status-mark';

/*
 * Куб соревнования: дата-плитка + имя + мета + кнопка статуса (10c: Results / Startlist / Results
 * soon / Startlist not yet — `cubeAction`, `StatusButton`). Весь куб — ссылка, кнопка — её визуал.
 * Варианты: обычный; чемпионат (золото, бейдж/метка справа); многодневка в процессе —
 * на 2 колонки с полосой дней; Cancelled — приглушён и не ссылка; Postponed — ссылка на
 * СТАРОЙ дате с тегом переноса.
 */

const TAG_CLASS: Record<StatusTone, string> = {
  live: 'border border-[var(--t-live)] text-[var(--t-live)]',
  cancelled: 'bg-[var(--t-nested)] text-[var(--t-text-2)]',
  postponed: 'border border-[var(--t-accent)] text-[var(--t-accent-text)]',
};

/** Сколько дней рисуем сегментами; больше — первые 6 и хвост «+N days». */
const STRIP_MAX = 7;

/** Полоса дней многодневки: сегмент на день (загружен — акцент) и подпись под ним. */
function MeetDayStrip({ meet }: { meet: CalendarMeet }) {
  const overflow = meet.days.length > STRIP_MAX;
  const shown = overflow ? meet.days.slice(0, STRIP_MAX - 1) : meet.days;
  const hidden = meet.days.length - shown.length;
  const cols = shown.length + (overflow ? 1 : 0);

  return (
    <span
      role="group"
      aria-label={`${meet.days_loaded} of ${meet.days_total} days loaded`}
      className="grid gap-1 pt-[2px]"
      style={{ gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))` }}
    >
      {shown.map((day, i) => (
        <span key={day.date} className="flex min-w-0 flex-col gap-1">
          <span
            className={`h-1 rounded-[2px] ${day.loaded ? 'bg-[var(--t-accent)]' : 'bg-[var(--t-border)]'}`}
          />
          <span
            className={`truncate text-[10px] ${day.loaded ? 'text-[var(--t-text)]' : 'text-[var(--t-text-2)]'}`}
          >
            {`Day ${i + 1} · ${shortDate(day.date)}${day.loaded ? ' ✓' : ''}`}
          </span>
        </span>
      ))}
      {overflow && (
        <span className="flex min-w-0 flex-col gap-1">
          <span className="h-1 rounded-[2px] bg-[var(--t-border)]" />
          <span className="truncate text-[10px] text-[var(--t-text-2)]">{`+${hidden} days`}</span>
        </span>
      )}
    </span>
  );
}

function MeetCube({ meet, today }: { meet: CalendarMeet; today: string }) {
  const tile = dateTile(meet);
  const mark = meet.is_championship ? champMark(meet) : null;
  const tag = statusTag(meet);
  const status = cubeAction(meet, today);
  // Текстовое действие — только без кнопки статуса (перенос: «Details ↗»).
  const action = status ? null : actionLabel(meet);
  const meta = [meet.pool, meet.venue, meet.group?.label].filter(Boolean) as string[];
  const cancelled = meet.status === 'cancelled';
  const link = meetLink(meet);
  const wide = isWideCube(meet);

  const className = `cc-focus flex flex-col overflow-hidden rounded-[14px] border border-[var(--t-border-2)] text-inherit no-underline shadow-[var(--t-shadow)] backdrop-blur-[14px] lg:min-h-[180px] ${
    mark ? 'cc-cube--champ' : link ? 'bg-[var(--t-surface)] hover:bg-[var(--t-nested)]' : 'bg-[var(--t-surface)]'
  } ${wide ? 'sm:col-span-2' : ''} ${cancelled ? 'opacity-50' : ''}`;

  const body = (
    <>
      <span className="flex items-start justify-between">
        <span className="flex min-w-[54px] flex-col items-center gap-px rounded-br-[10px] border-b border-r border-[var(--t-border-2)] lg:min-w-[64px] px-[10px] py-[7px] lg:px-[14px] lg:py-2">
          <span className="text-[9px] uppercase tracking-[.08em] text-[var(--t-text-2)] lg:text-[10px]">{tile.wd}</span>
          <span className="text-[17px] font-semibold leading-tight text-[var(--t-text)] lg:text-[20px]">{tile.day}</span>
          <span className="text-[10px] text-[var(--t-accent-text)] lg:text-[11px]">{tile.mon}</span>
        </span>
        {mark && (
          <span className="flex max-w-[60%] justify-end pr-[10px] pt-[10px]">
            {mark.kind === 'badge' ? (
              <>
                <GroupBadge group={mark.group} size="full-sm" className="lg:hidden" />
                <GroupBadge group={mark.group} size="full" className="hidden lg:flex" />
              </>
            ) : (
              <span className="flex items-center gap-1 text-[11px] font-medium text-[var(--t-champ-ink)]">
                <IconTrophy size={11} className="shrink-0" />
                {mark.text}
              </span>
            )}
          </span>
        )}
      </span>

      <span className="flex flex-1 flex-col gap-[6px] px-[10px] pb-3 pt-[10px] lg:px-[14px] lg:pb-[14px] lg:pt-3">
        <span
          dir="auto"
          className="cc-clamp2 text-left text-[13px] font-semibold leading-[1.35] lg:text-[14px] text-[var(--t-text)]"
        >
          {meet.name}
        </span>
        {meta.length > 0 && (
          <span className="text-[11px] text-[var(--t-text-2)] lg:text-[12px]">
            {meta.map((part, i) => (
              <React.Fragment key={i}>
                {i > 0 && ' · '}
                {part === meet.venue ? <span dir="auto">{part}</span> : part}
              </React.Fragment>
            ))}
          </span>
        )}
        {wide && meet.days.length > 1 && <MeetDayStrip meet={meet} />}
        <span className="mt-auto flex flex-col gap-[6px] pt-1">
          {(tag || action) && (
            <span className="flex flex-wrap items-center justify-between gap-[6px]">
              {tag ? (
                <span className={`rounded-[6px] px-2 py-[3px] text-[11px] font-semibold ${TAG_CLASS[tag.tone]}`}>
                  {tag.text}
                </span>
              ) : (
                <span />
              )}
              {action && <span className="text-[12px] font-semibold text-[var(--t-accent-text)]">{action}</span>}
            </span>
          )}
          {status && <StatusButton kind={status.kind} label={status.label} />}
        </span>
      </span>
    </>
  );

  // Без ссылки (cancelled, перенос без страницы федерации) — не <a>: открывать нечего.
  if (!link) {
    return (
      <div
        aria-disabled="true"
        aria-label={`${meet.name}, ${dateRangeLabel(meet)}${cancelled ? ' — cancelled' : ''}`}
        className={className}
      >
        {body}
      </div>
    );
  }

  // Внешняя ссылка (страница федерации у незатянутых без заявок) — в новой вкладке.
  if (link.external) {
    return (
      <a
        href={link.href}
        target="_blank"
        rel="noopener noreferrer"
        aria-label={`${meet.name}, ${dateRangeLabel(meet)} (opens isr.org.il)`}
        className={className}
      >
        {body}
      </a>
    );
  }

  return (
    <a href={link.href} aria-label={`${meet.name}, ${dateRangeLabel(meet)}`} className={className}>
      {body}
    </a>
  );
}

export default MeetCube;

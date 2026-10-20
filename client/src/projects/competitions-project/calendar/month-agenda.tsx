import React from 'react';
import { scrollUnderTopbar } from './calendar-layout-hooks';
import { dayHeading, isDayInMonth, meetsOnDay, monthKey, weekGroups } from './calendar-model';
import type { CalendarMeet, CalendarMonth } from './calendar-types';
import CubeGrid from './cube-grid';

/*
 * Правая колонка лейаута 9a (десктоп): meets месяца кубами, сгруппированные по неделям
 * («Week · 18–24 Oct · 6 meets»), недели без meets не показываются. Выбран день в мини-месяце —
 * режим дня: «Sat · 24 Oct» + «← Whole month», только кубы этого дня.
 *
 * Прокрутка: при открытии текущего месяца — к неделе «сегодня» (не к началу месяца); при смене
 * дня/месяца — к началу колонки, но только если она ушла под топбар (иначе страница не дёргается).
 */

const plural = (n: number) => `${n} ${n === 1 ? 'meet' : 'meets'}`;

function SectionHead({ title, children }: { title: string; children?: React.ReactNode }) {
  return (
    <div className="flex items-baseline gap-[10px] border-b border-[var(--t-border-2)] pb-[6px]">
      <h3 className="m-0 whitespace-nowrap text-[15px] font-medium text-[var(--t-text)]">{title}</h3>
      {children}
    </div>
  );
}

function MonthAgenda({
  month,
  meets,
  today,
  selectedDay,
  onWholeMonth,
}: {
  month: CalendarMonth;
  /** Meets месяца после фильтра по типу. */
  meets: CalendarMeet[];
  today: string;
  selectedDay: string | null;
  onWholeMonth: () => void;
}) {
  const groups = React.useMemo(() => weekGroups(meets, month), [meets, month]);
  const dayMeets = selectedDay ? meetsOnDay(meets, selectedDay) : [];

  const rootRef = React.useRef<HTMLDivElement>(null);
  const todayRef = React.useRef<HTMLElement>(null);

  // Неделя «сегодня»: первая группа, в которой есть сегодня или что-то после него.
  const todayGroup = isDayInMonth(today, month)
    ? groups.findIndex((g) => g.week.some((d) => d !== null && d >= today))
    : -1;

  // Открытие страницы на текущем месяце без выбранного дня → к неделе «сегодня», один раз.
  const initialScrollDone = React.useRef(false);
  React.useEffect(() => {
    if (initialScrollDone.current) return;
    initialScrollDone.current = true;
    if (selectedDay || todayGroup <= 0 || !todayRef.current) return;
    scrollUnderTopbar(todayRef.current);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Смена дня или месяца → начало колонки, если она ушла вверх.
  const viewId = `${monthKey(month)}|${selectedDay ?? ''}`;
  const lastViewId = React.useRef(viewId);
  React.useEffect(() => {
    if (lastViewId.current === viewId) return;
    lastViewId.current = viewId;
    if (rootRef.current) scrollUnderTopbar(rootRef.current, { onlyIfAbove: true });
  }, [viewId]);

  if (selectedDay) {
    return (
      <div ref={rootRef} aria-live="polite" className="flex flex-col gap-[10px]">
        <SectionHead title={dayHeading(selectedDay)}>
          <span className="whitespace-nowrap text-[12px] text-[var(--t-text-2)]">{plural(dayMeets.length)}</span>
          <button
            type="button"
            onClick={onWholeMonth}
            className="cc-focus ml-auto cursor-pointer border-0 bg-transparent p-0 text-[12px] font-semibold text-[var(--t-accent-text)] hover:underline"
          >
            ← Whole month
          </button>
        </SectionHead>
        {dayMeets.length > 0 ? (
          <CubeGrid meets={dayMeets} today={today} columns="sidebar" />
        ) : (
          <p className="m-0 text-[13px] text-[var(--t-text-2)]">No meets on this day.</p>
        )}
      </div>
    );
  }

  return (
    <div ref={rootRef} aria-live="polite" className="flex flex-col gap-[18px]">
      {groups.length === 0 && <p className="m-0 text-[13px] text-[var(--t-text-2)]">No meets this month</p>}
      {groups.map((g, i) => (
        <section
          key={g.label}
          ref={i === todayGroup ? todayRef : undefined}
          aria-label={`Week ${g.label}`}
          className="flex flex-col gap-[10px]"
        >
          <SectionHead title={`Week · ${g.label}`}>
            <span className="whitespace-nowrap text-[12px] text-[var(--t-text-2)]">{plural(g.meets.length)}</span>
          </SectionHead>
          <CubeGrid meets={g.meets} today={today} columns="sidebar" />
        </section>
      ))}
    </div>
  );
}

export default MonthAgenda;

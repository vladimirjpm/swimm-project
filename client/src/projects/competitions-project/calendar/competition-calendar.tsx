import React from 'react';
import './competition-calendar.css';
import CalendarControls from './calendar-controls';
import {
  addMonths,
  applyTypeFilter,
  dayLabel,
  defaultSelectedDay,
  israelToday,
  isDayInMonth,
  meetInMonth,
  meetOnDay,
  monthIndex,
  monthKey,
  monthLabel,
  monthShort,
  navBounds,
  seasonMonths,
  seasonOfMonth,
  seasonTitle,
  typeCounts,
} from './calendar-model';
import { readCalendarQuery, writeCalendarQuery, type CalendarState, type CalendarView } from './calendar-state';
import type { CalendarMeet, CalendarMonth, MeetType } from './calendar-types';
import { useCompetitionCalendar } from './use-competition-calendar';

/*
 * Календарь сезона на /competitions — оркестратор: состояние вида (синхронизировано с query),
 * загрузка сезона месяца, область/счётчики/недоступные виды, строка управления и тело вида.
 * Тела видов в Б2 — временные заглушки (YearStub / MonthStub / MeetsStub), их заменят Б3–Б5.
 */

const MUTED = 'text-[14px] text-[var(--t-text-2)]';

function YearStub({
  season,
  meets,
  onPickMonth,
}: {
  season: number;
  meets: CalendarMeet[];
  onPickMonth: (m: CalendarMonth) => void;
}) {
  return (
    <div className="flex flex-col items-start gap-1">
      {seasonMonths(season).map((m) => {
        const n = meets.filter((meet) => meetInMonth(meet, m)).length;
        return (
          <button
            key={monthKey(m)}
            type="button"
            onClick={() => onPickMonth(m)}
            className="cc-focus cursor-pointer text-[14px] text-[var(--t-text)]"
          >
            {`${monthShort(m)} ${m.year} — ${n} meets`}
          </button>
        );
      })}
    </div>
  );
}

function MonthStub({
  month,
  meets,
  selectedDay,
  onPickDay,
}: {
  month: CalendarMonth;
  meets: CalendarMeet[];
  selectedDay: string | null;
  onPickDay: (iso: string) => void;
}) {
  const byDay = new Map<string, CalendarMeet[]>();
  for (const meet of meets) {
    for (const d of meet.days) {
      if (!isDayInMonth(d.date, month)) continue;
      byDay.set(d.date, [...(byDay.get(d.date) ?? []), meet]);
    }
  }
  const days = Array.from(byDay.keys()).sort();
  if (days.length === 0) return <p className={MUTED}>No meets this month</p>;

  const onSelected = selectedDay ? meets.filter((meet) => meetOnDay(meet, selectedDay)) : [];

  return (
    <div className="flex flex-col items-start gap-1">
      {days.map((iso) => (
        <button
          key={iso}
          type="button"
          onClick={() => onPickDay(iso)}
          className="cc-focus cursor-pointer text-left text-[14px] text-[var(--t-text)]"
        >
          {`${dayLabel(iso)} — `}
          <span dir="auto">{byDay.get(iso)!.map((meet) => meet.name).join(', ')}</span>
        </button>
      ))}
      <p className={`mt-2 ${MUTED}`}>
        {selectedDay && onSelected.length > 0
          ? `Selected: ${selectedDay} · ${onSelected.length} meets`
          : 'No meets on this day.'}
      </p>
    </div>
  );
}

function MeetsStub({ month, meets }: { month: CalendarMonth; meets: CalendarMeet[] }) {
  const inMonth = meets.filter((meet) => meetInMonth(meet, month));
  if (inMonth.length === 0) return <p className={MUTED}>No meets this month</p>;
  return (
    <ul className="m-0 flex list-none flex-col gap-1 p-0">
      {inMonth.map((meet) => (
        <li key={meet.key} className="text-[14px] text-[var(--t-text)]">
          {`${meet.start} — `}
          <span dir="auto">{meet.name}</span>
        </li>
      ))}
    </ul>
  );
}

function CompetitionCalendar() {
  const [fallbackToday] = React.useState(israelToday);
  const [state, setState] = React.useState<CalendarState>(() =>
    readCalendarQuery(window.location.search, fallbackToday),
  );
  // Последние известные сезоны: чтобы границы ‹ › не мигали, пока грузится соседний сезон.
  const [knownSeasons, setKnownSeasons] = React.useState<number[]>([]);

  const season = seasonOfMonth(state.month);
  const { data, error } = useCompetitionCalendar(season);
  const today = data?.today ?? fallbackToday;

  React.useEffect(() => {
    if (data) setKnownSeasons(data.seasons);
  }, [data]);

  // Область вида: сезон (Year) или месяц (Month, Meet days). Счётчики чипов — без фильтра типа.
  const seasonMeets = React.useMemo(() => data?.meets ?? [], [data]);
  const monthMeets = React.useMemo(
    () => seasonMeets.filter((meet) => meetInMonth(meet, state.month)),
    [seasonMeets, state.month],
  );
  const areaMeets = state.view === 'year' ? seasonMeets : monthMeets;
  const counts = React.useMemo(() => typeCounts(areaMeets), [areaMeets]);

  const filteredSeason = React.useMemo(
    () => applyTypeFilter(seasonMeets, state.typeFilter),
    [seasonMeets, state.typeFilter],
  );
  const filteredMonth = React.useMemo(
    () => applyTypeFilter(monthMeets, state.typeFilter),
    [monthMeets, state.typeFilter],
  );

  const disabledViews: CalendarView[] = [];
  if (data && filteredSeason.length === 0) disabledViews.push('year');
  if (data && filteredMonth.length === 0) disabledViews.push('meets');

  // День по умолчанию (правило §4) — для записи в адрес и для эффекта ниже.
  const defaultDay = React.useMemo(
    () => (data ? defaultSelectedDay(filteredMonth, state.month, today) : null),
    [data, filteredMonth, state.month, today],
  );

  // Выбранный день ставим при смене месяца, смене фильтра типа (выбранный день мог из него
  // выпасть) и когда пришли данные сезона. `?day=` из адреса важнее правила, если он в месяце;
  // его берём один раз — на первых пришедших данных.
  const pendingDay = React.useRef<string | null>(state.selectedDay);
  const defaultDayRef = React.useRef<string | null>(null);
  defaultDayRef.current = defaultDay;
  const monthId = monthKey(state.month);
  const filterId = state.typeFilter.join(',');
  React.useEffect(() => {
    if (!data) return;
    const pending = pendingDay.current;
    pendingDay.current = null;
    const day = pending && isDayInMonth(pending, state.month) ? pending : defaultDayRef.current;
    setState((s) => ({ ...s, selectedDay: day }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, monthId, filterId]);

  // Выбранный день, не лежащий в текущем месяце (месяц только что сменили), не показываем.
  const selectedDay = state.selectedDay && isDayInMonth(state.selectedDay, state.month) ? state.selectedDay : null;

  // Адрес — только эффектом на изменение состояния, не на каждый рендер.
  React.useEffect(() => {
    writeCalendarQuery({ ...state, selectedDay }, today, defaultDay);
  }, [state, selectedDay, today, defaultDay]);

  // Навигация ‹ ›: Year — на сезон (тот же календарный месяц), остальное — на месяц.
  const step = state.view === 'year' ? 12 : 1;
  const bounds = navBounds(data?.seasons ?? knownSeasons);
  const prevDisabled = !bounds || monthIndex(addMonths(state.month, -step)) < monthIndex(bounds.min);
  const nextDisabled = !bounds || monthIndex(addMonths(state.month, step)) > monthIndex(bounds.max);

  const goMonth = (m: CalendarMonth) => setState((s) => ({ ...s, month: m }));

  const title = state.view === 'year' ? seasonTitle(season) : monthLabel(state.month);

  return (
    <section className="relative px-4 pt-[26px] lg:px-16 lg:pt-12" aria-label="Competition calendar">
      <CalendarControls
        title={title}
        onPrev={() => goMonth(addMonths(state.month, -step))}
        onNext={() => goMonth(addMonths(state.month, step))}
        prevDisabled={prevDisabled}
        nextDisabled={nextDisabled}
        view={state.view}
        onView={(view) => setState((s) => ({ ...s, view }))}
        disabledViews={disabledViews}
        typeFilter={state.typeFilter}
        onTypeFilter={(typeFilter: MeetType[]) => setState((s) => ({ ...s, typeFilter }))}
        counts={counts}
      />

      <div className="mt-6">
        {error ? (
          <p className={MUTED}>Could not load the calendar.</p>
        ) : !data ? null : state.view === 'year' ? (
          <YearStub
            season={season}
            meets={filteredSeason}
            onPickMonth={(m) => setState((s) => ({ ...s, view: 'month', month: m }))}
          />
        ) : state.view === 'month' ? (
          <MonthStub
            month={state.month}
            meets={filteredMonth}
            selectedDay={selectedDay}
            onPickDay={(iso) => setState((s) => ({ ...s, selectedDay: iso }))}
          />
        ) : (
          <MeetsStub month={state.month} meets={filteredMonth} />
        )}
      </div>
    </section>
  );
}

export default CompetitionCalendar;

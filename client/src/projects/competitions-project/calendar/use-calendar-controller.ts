import React from 'react';
import { isCalendarDemo, withDemoMeets } from './calendar-demo';
import {
  addMonths,
  applyTypeFilter,
  defaultSelectedDay,
  israelToday,
  isDayInMonth,
  meetInMonth,
  monthIndex,
  monthKey,
  monthLabel,
  navBounds,
  seasonOfMonth,
  seasonTitle,
  typeCounts,
} from './calendar-model';
import { readCalendarQuery, writeCalendarQuery, type CalendarState, type CalendarView } from './calendar-state';
import type { CalendarMeet, CalendarMonth, CompetitionCalendar, MeetType } from './calendar-types';
import { useCompetitionCalendar } from './use-competition-calendar';

/*
 * Контроллер календаря: всё, что не вёрстка, — состояние вида (синхронизировано с query), загрузка
 * сезона, отфильтрованные meets, счётчики, недоступные виды, границы ‹ ›, выбранный день.
 * Раскладки (`calendar-desktop.tsx`, `calendar-mobile.tsx`) получают готовый объект и только
 * рисуют — третью раскладку или другой экран можно собрать на том же контроллере.
 *
 * `desktop` меняет одно правило: день по умолчанию. На телефоне день выбран всегда (под неделей
 * кубы дня), на десктопе 9a — нет: справа весь месяц по неделям, клик по дню — режим дня.
 */

export interface CalendarController {
  /** null — сезон ещё грузится или упал. */
  data: CompetitionCalendar | null;
  error: boolean;
  retry: () => void;
  demoOn: boolean;

  today: string;
  season: number;
  view: CalendarView;
  month: CalendarMonth;
  typeFilter: MeetType[];
  /** Выбранный день в текущем месяце или null. */
  selectedDay: string | null;
  /** «October 2026» / «Season 2026/27». */
  title: string;

  /** Meets сезона / месяца после фильтра по типу. */
  seasonMeets: CalendarMeet[];
  monthMeets: CalendarMeet[];
  /** Счётчики чипов типа: по сезону (Year) или месяцу, без учёта самого фильтра. */
  counts: Record<MeetType | 'all', number>;
  disabledViews: CalendarView[];

  /** ‹ › строки управления: Year — шаг сезон, иначе месяц. */
  prevDisabled: boolean;
  nextDisabled: boolean;
  goPrev: () => void;
  goNext: () => void;
  /** Соседний месяц (неделя телефона переходит через границу). */
  goMonth: (m: CalendarMonth) => void;

  setView: (view: CalendarView) => void;
  setTypeFilter: (next: MeetType[]) => void;
  selectDay: (iso: string | null) => void;
  /** Карточка месяца в Year → Month этого месяца. */
  openMonth: (m: CalendarMonth) => void;
}

export function useCalendarController({ desktop }: { desktop: boolean }): CalendarController {
  const [fallbackToday] = React.useState(israelToday);
  const [state, setState] = React.useState<CalendarState>(() =>
    readCalendarQuery(window.location.search, fallbackToday),
  );
  // Последние известные сезоны: чтобы границы ‹ › не мигали, пока грузится соседний сезон.
  const [knownSeasons, setKnownSeasons] = React.useState<number[]>([]);

  const season = seasonOfMonth(state.month);
  const { data, error, retry } = useCompetitionCalendar(season);
  const today = data?.today ?? fallbackToday;

  React.useEffect(() => {
    if (data) setKnownSeasons(data.seasons);
  }, [data]);

  // Dev-демо (?calendarDemo=1, только dev-сборка): в показанный месяц добавляются выдуманные meets.
  const demoOn = React.useMemo(isCalendarDemo, []);
  const allSeason = React.useMemo(
    () => (data ? (demoOn ? withDemoMeets(data.meets, state.month, data.today) : data.meets) : []),
    [data, demoOn, state.month],
  );
  const allMonth = React.useMemo(
    () => allSeason.filter((meet) => meetInMonth(meet, state.month)),
    [allSeason, state.month],
  );
  const counts = React.useMemo(
    () => typeCounts(state.view === 'year' ? allSeason : allMonth),
    [state.view, allSeason, allMonth],
  );
  const seasonMeets = React.useMemo(() => applyTypeFilter(allSeason, state.typeFilter), [allSeason, state.typeFilter]);
  const monthMeets = React.useMemo(() => applyTypeFilter(allMonth, state.typeFilter), [allMonth, state.typeFilter]);

  const disabledViews: CalendarView[] = [];
  if (data && seasonMeets.length === 0) disabledViews.push('year');
  if (data && monthMeets.length === 0) disabledViews.push('meets');

  // День по умолчанию: телефон — правило §4 (ближайший день с meets), десктоп — нет дня.
  const defaultDay = React.useMemo(
    () => (data && !desktop ? defaultSelectedDay(monthMeets, state.month, today) : null),
    [data, desktop, monthMeets, state.month, today],
  );

  // Выбранный день ставим при смене месяца, фильтра типа (день мог из него выпасть), лейаута и
  // когда пришли данные сезона. `?day=` из адреса важнее правила, если он в месяце; его берём
  // один раз — на первых пришедших данных.
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
  }, [data, monthId, filterId, desktop]);

  // Meet days на десктопе нет (его роль — список по неделям 9a): `?view=meets` там = Month.
  React.useEffect(() => {
    if (desktop && state.view === 'meets') setState((s) => ({ ...s, view: 'month' }));
  }, [desktop, state.view]);

  // Выбранный день, не лежащий в текущем месяце (месяц только что сменили), не показываем.
  const selectedDay = state.selectedDay && isDayInMonth(state.selectedDay, state.month) ? state.selectedDay : null;

  // Адрес — только эффектом на изменение состояния, не на каждый рендер.
  React.useEffect(() => {
    writeCalendarQuery({ ...state, selectedDay }, today, defaultDay);
  }, [state, selectedDay, today, defaultDay]);

  // Навигация ‹ ›: Year — на сезон (тот же календарный месяц), остальное — на месяц.
  const step = state.view === 'year' ? 12 : 1;
  const bounds = navBounds(data?.seasons ?? knownSeasons);
  // Пока сезон грузится, ‹ › недоступны; после ошибки — нет (иначе из неё не выйти ничем, кроме Try again).
  const loadingSeason = !data && !error;
  const prevDisabled = loadingSeason || !bounds || monthIndex(addMonths(state.month, -step)) < monthIndex(bounds.min);
  const nextDisabled = loadingSeason || !bounds || monthIndex(addMonths(state.month, step)) > monthIndex(bounds.max);

  const goMonth = React.useCallback((m: CalendarMonth) => setState((s) => ({ ...s, month: m })), []);

  return {
    data,
    error,
    retry,
    demoOn,
    today,
    season,
    view: state.view,
    month: state.month,
    typeFilter: state.typeFilter,
    selectedDay,
    title: state.view === 'year' ? seasonTitle(season) : monthLabel(state.month),
    seasonMeets,
    monthMeets,
    counts,
    disabledViews,
    prevDisabled,
    nextDisabled,
    goPrev: () => goMonth(addMonths(state.month, -step)),
    goNext: () => goMonth(addMonths(state.month, step)),
    goMonth,
    setView: (view) => setState((s) => ({ ...s, view })),
    setTypeFilter: (typeFilter) => setState((s) => ({ ...s, typeFilter })),
    selectDay: (iso) => setState((s) => ({ ...s, selectedDay: iso })),
    openMonth: (m) => setState((s) => ({ ...s, view: 'month', month: m })),
  };
}

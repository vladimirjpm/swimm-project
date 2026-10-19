import { monthKey, parseMonthKey } from './calendar-model';
import type { CalendarMonth, MeetType } from './calendar-types';

/*
 * Состояние вида календаря и его синхронизация с адресом.
 * Правило client/CLAUDE.md: в путь — только идентичность, вид — в query. Пишем
 * history.replaceState (не pushState), умолчания и пустые значения в адрес не попадают,
 * неверные значения при чтении откатываются к умолчанию.
 */

export type CalendarView = 'year' | 'month' | 'meets';

export const MEET_TYPES: MeetType[] = ['pool', 'open_water', 'artistic'];

export interface CalendarState {
  view: CalendarView;
  month: CalendarMonth;
  /** yyyy-MM-dd или null — дня нет (в месяце нет meets). */
  selectedDay: string | null;
  /** Пусто = All. */
  typeFilter: MeetType[];
}

const DEFAULT_VIEW: CalendarView = 'month';
const VIEWS: CalendarView[] = ['year', 'month', 'meets'];
const DAY_RE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Разбор query в состояние. `today` (yyyy-MM-dd по Израилю) даёт умолчание месяца.
 * `selectedDay` здесь — только то, что стоит в `?day=`; правило «день по умолчанию» применяет
 * оркестратор, когда придут данные.
 */
export function readCalendarQuery(search: string, today: string): CalendarState {
  const params = new URLSearchParams(search);

  const rawView = params.get('view') as CalendarView | null;
  const view = rawView && VIEWS.includes(rawView) ? rawView : DEFAULT_VIEW;

  const month = parseMonthKey(params.get('month'))
    ?? parseMonthKey(today.slice(0, 7))
    ?? { year: new Date().getFullYear(), month: new Date().getMonth() + 1 };

  const rawDay = params.get('day');
  const selectedDay = rawDay && DAY_RE.test(rawDay) ? rawDay : null;

  const typeFilter = MEET_TYPES.filter((t) => (params.get('type') ?? '').split(',').includes(t));

  return { view, month, selectedDay, typeFilter };
}

/**
 * Запись состояния в адрес (replaceState), с сохранением прочих параметров и hash.
 * Не пишутся: view=month, месяц «сегодня», день по умолчанию (`defaultDay`), пустой фильтр.
 * День пишем только в виде Month — в остальных он не нужен.
 */
export function writeCalendarQuery(state: CalendarState, today: string, defaultDay: string | null): void {
  const url = new URL(window.location.href);
  const params = url.searchParams;

  if (state.view !== DEFAULT_VIEW) params.set('view', state.view);
  else params.delete('view');

  if (monthKey(state.month) !== today.slice(0, 7)) params.set('month', monthKey(state.month));
  else params.delete('month');

  if (state.view === 'month' && state.selectedDay && state.selectedDay !== defaultDay) {
    params.set('day', state.selectedDay);
  } else {
    params.delete('day');
  }

  if (state.typeFilter.length > 0) params.set('type', state.typeFilter.join(','));
  else params.delete('type');

  // Запятая в `type=a,b` читабельнее, чем %2C; читаем мы оба написания одинаково.
  const query = params.toString().replace(/%2C/gi, ',');
  const next = `${url.pathname}${query ? `?${query}` : ''}${url.hash}`;
  const current = `${window.location.pathname}${window.location.search}${window.location.hash}`;
  if (next !== current) window.history.replaceState(null, '', next);
}

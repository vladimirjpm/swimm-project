import type { CalendarMeet, CalendarMonth } from './calendar-types';
import { monthKey } from './calendar-model';

/*
 * Фейковые meets для проверки вариантов куба на любом месяце, даже без таких стартов в базе:
 * призрак переноса (`postponed`), `cancelled` и будущий старт без заявок (`Details ↗` на сайт
 * федерации) — в том же виде, в каком их отдаёт сервер (`kind: 'upcoming'` + `source_url`).
 * Включается ТОЛЬКО в dev-сборке и параметром
 * `?calendarDemo=1`; в прод-сборке `import.meta.env.DEV` ложно, и query даже не читается.
 */

/** Демо-режим включён: dev-сборка + `?calendarDemo=1`. */
export function isCalendarDemo(): boolean {
  if (!import.meta.env.DEV) return false;
  return new URLSearchParams(window.location.search).get('calendarDemo') === '1';
}

const pad2 = (n: number) => String(n).padStart(2, '0');

/** Фейковый compID / id: уникален в пределах страницы, не пересекается с настоящими. */
let demoIdSeq = 1;

const DEMO_SOURCE = 'https://isr.org.il/';

type DemoPatch = Partial<CalendarMeet> & Pick<CalendarMeet, 'key' | 'name'>;

/** Демо-meet на днях `from..to` (числа внутри месяца); остальные поля — «обычное событие бассейна». */
function demoMeet(month: CalendarMonth, from: number, to: number, patch: DemoPatch): CalendarMeet {
  const iso = (d: number) => `${monthKey(month)}-${pad2(d)}`;
  const days = Array.from({ length: to - from + 1 }, (_, i) => ({ date: iso(from + i), loaded: false }));
  return {
    kind: 'competition',
    id: 900000 + demoIdSeq++,
    start: iso(from),
    end: iso(to),
    days,
    days_loaded: 0,
    days_total: days.length,
    pool: '25m',
    venue: null,
    type: 'pool',
    is_championship: false,
    group: null,
    status: 'upcoming',
    postponed_to: null,
    has_start_list: false,
    source_url: null,
    ...patch,
  };
}

/** Добавляет в показанный месяц 5 демо-meets и сортирует всё по старту и имени. */
export function withDemoMeets(meets: CalendarMeet[], month: CalendarMonth, _today: string): CalendarMeet[] {
  demoIdSeq = 1;
  // Перенос — через 4 недели от 15-го числа.
  const moved = new Date(Date.UTC(month.year, month.month - 1, 15 + 28));
  const postponedTo = `${moved.getUTCFullYear()}-${pad2(moved.getUTCMonth() + 1)}-${pad2(moved.getUTCDate())}`;

  const multi = demoMeet(month, 10, 12, {
    key: 'demo:multi',
    name: 'אליפות ישראל לנוער — דמו',
    kind: 'event',
    pool: '50m',
    venue: 'מכון וינגייט',
    is_championship: true,
    group: { letter: 'J', label: 'Juniors', ages: '14–17', season: 'winter' },
    status: 'in_progress',
    days_loaded: 1,
  });
  multi.days = multi.days.map((d, i) => ({ ...d, loaded: i === 0 }));

  const demo: CalendarMeet[] = [
    multi,
    demoMeet(month, 14, 14, {
      key: 'demo:cancelled',
      pool: '',
      name: 'ליגה אזורית — מבוטל (דמו)',
      kind: 'upcoming',
      status: 'cancelled',
      source_url: DEMO_SOURCE,
      venue: 'בריכת וייסגל רחובות',
    }),
    demoMeet(month, 15, 15, {
      key: 'demo:postponed',
      pool: '',
      name: 'גביע הנוער — נדחה (דמו)',
      kind: 'upcoming',
      status: 'postponed',
      source_url: DEMO_SOURCE,
      postponed_to: postponedTo,
      venue: 'בריכת עמידר',
    }),
    demoMeet(month, 21, 21, {
      key: 'demo:openwater',
      name: 'אליפות ישראל מים פתוחים — דמו',
      kind: 'upcoming',
      pool: '',
      type: 'open_water',
      is_championship: true,
      status: 'upcoming',
      has_start_list: true,
      venue: 'חוף הים, אילת',
    }),
    // Будущий старт из входящих без заявок: ведёт на сайт федерации (Details ↗).
    demoMeet(month, 25, 25, {
      key: 'demo:details',
      pool: '',
      name: 'ליגת נוער — פרטים באתר הפדרציה (דמו)',
      kind: 'upcoming',
      status: 'upcoming',
      source_url: DEMO_SOURCE,
      venue: 'בריכת גבעתיים',
    }),
  ];

  return [...meets, ...demo].sort((a, b) => a.start.localeCompare(b.start) || a.name.localeCompare(b.name));
}

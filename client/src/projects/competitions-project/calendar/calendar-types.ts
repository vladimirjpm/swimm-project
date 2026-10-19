/**
 * TS-зеркало ответа `GET /api/competitions/calendar` (сервер: `CompetitionCalendarDtos.cs`).
 * Имена полей — snake_case, как в JSON. Контракт и смысл полей —
 * docs/tasks/competitions-home-calendar-spec.md §2.
 */

/** Тип соревнования: бассейн / открытая вода / артистика. */
export type MeetType = 'pool' | 'open_water' | 'artistic';

/** Откуда карточка: событие (многодневка), одно соревнование или предстоящий старт из федерации. */
export type MeetKind = 'event' | 'competition' | 'upcoming';

export type MeetStatus =
  | 'upcoming'
  | 'live'
  | 'in_progress'
  | 'complete'
  | 'done'
  | 'cancelled'
  | 'postponed';

/** Возрастная группа соревнования (бейдж K/Y/J/A/M). */
export interface CalendarGroup {
  letter: string;
  label: string;
  /** Возраст для подписи на бейдже: «14–17», «17+», «25+». */
  ages: string;
  season: string;
}

/** День соревнования: дни НЕ обязательно идут подряд, `loaded` — результаты уже в базе. */
export interface CalendarMeetDay {
  /** yyyy-MM-dd */
  date: string;
  loaded: boolean;
}

export interface CalendarMeet {
  /** Уникален: `event:{id}` | `competition:{id}` | `upcoming:{orgCompId}`. */
  key: string;
  kind: MeetKind;
  id: number;
  name: string;
  /** yyyy-MM-dd */
  start: string;
  /** yyyy-MM-dd */
  end: string;
  days: CalendarMeetDay[];
  days_loaded: number;
  days_total: number;
  /** «25m» | «50m» | «» (нет данных / море / артистика). */
  pool: '25m' | '50m' | '';
  /** Площадка (на иврите), null — нет. */
  venue: string | null;
  type: MeetType;
  is_championship: boolean;
  group: CalendarGroup | null;
  status: MeetStatus;
  /** ISO-дата переноса при `postponed`. */
  postponed_to: string | null;
  has_start_list: boolean;
}

export interface CompetitionCalendar {
  /** Год начала сезона. */
  season: number;
  /** «2026/27» */
  season_label: string;
  /** «Сегодня» по Asia/Jerusalem, yyyy-MM-dd. */
  today: string;
  /** Сезоны, в которых у нас есть старты, по возрастанию — границы листания. */
  seasons: number[];
  /** По дате начала, затем по имени. */
  meets: CalendarMeet[];
}

/** Месяц в состоянии календаря: month 1–12. */
export interface CalendarMonth {
  year: number;
  month: number;
}

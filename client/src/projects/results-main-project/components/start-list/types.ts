// Типы стартового протокола (таб Start list, docs/plans/start-list-plan.md §4).
// Зеркалят server/Swimm.Application/Dtos/StartListDtos.cs (snake_case).

/** Одна строка стартового протокола — заявка пловца на конкретный заплыв/дорожку. */
export interface StartListSwim {
  id: number;
  /** compID соревнования этого заплыва — нужен вне контекста одного соревнования
   *  (напр. `GET /api/start-list/upcoming` по нескольким избранным). */
  org_comp_id: number;
  comp_name: string;
  org_discipline_id: number;
  event_number: number | null;
  distance: string;
  style_name: string;
  gender: string;
  event_category: string | null;
  age_band: string | null;
  is_relay: boolean;
  heat: number;
  lane: number;
  /** Календарный день заплыва — отдельно от heat_start_at: времени может не быть вовсе,
   *  а «в какой день» — главный ответ поиска по составному соревнованию. */
  comp_date: string;
  /** UTC, может быть null — заплыву ещё не назначили время (решение 3, «≈»). */
  heat_start_at: string | null;
  round: string | null;
  /** null = «NT», пловец эту дистанцию ещё не плыл. */
  seed_time: string | null;
  /** Всегда 'seed' — личный рекорд С ДРУГОГО старта (И11, решение 4). */
  quality: string;
  swimmer_id: number;
  swimmer_name: string;
  birth_year: number | null;
  club_id: number;
  club_name: string;
  result_id: number | null;
  status: 'entered' | 'swum' | 'no-show' | string;
}

export interface StartListEvent {
  org_discipline_id: number;
  event_number: number | null;
  distance: string;
  style_name: string;
  gender: string;
  event_category: string | null;
  age_band: string | null;
  is_relay: boolean;
  start_at: string | null;
  entries: number;
  heats: number;
}

export interface StartListDay {
  date: string;
  events: StartListEvent[];
}

export interface StartListProgramme {
  org_comp_id: number;
  comp_name: string;
  days: StartListDay[];
  entries: number;
  updated_at: string | null;
}

export interface StartListHeat {
  heat: number;
  start_at: string | null;
  round: string | null;
  lanes: StartListSwim[];
}

export interface StartListEventHeats {
  org_comp_id: number;
  comp_name: string;
  event: StartListEvent;
  heats: StartListHeat[];
  updated_at: string | null;
}

export interface StartListSwimmer {
  org_comp_id: number;
  comp_name: string;
  swimmer_id: number;
  swimmer_name: string;
  birth_year: number;
  club_name: string;
  first_start_at: string | null;
  swims: StartListSwim[];
  updated_at: string | null;
}

/** Строка выдачи поиска по имени внутри соревнования (все источники сразу). */
export interface StartListSwimmerHit {
  swimmer_id: number;
  swimmer_name: string;
  birth_year: number | null;
  club_name: string;
  swims: number;
  /** Дни, в которые он плывёт. */
  days: string[];
  first_start_at: string | null;
}

/** Предстоящее соревнование в общем списке `/competitions` (С7б, решение В9). */
export interface UpcomingCompetition {
  org_comp_id: number;
  comp_name: string;
  date_start: string;
  date_end: string;
  days: number;
  entries: number;
  swimmers: number;
  updated_at: string | null;
}

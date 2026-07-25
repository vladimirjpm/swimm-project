// Типы модульной шапки соревнования (design_handoff_competition_overview, вариант 1b).
// DTO зеркалят server/Swimm.Application/Dtos/CompetitionOverviewDto.cs (snake_case).

export type CompetitionTab = 'overview' | 'swims' | 'clubs' | 'records' | 'media';

export interface OverviewSummary {
  result_count: number;
  day_count: number;
  swimmer_count: number;
  club_count: number;
}

export interface OverviewDay {
  competition_id: number;
  /** dd/MM/yyyy — формат Competition.Date. */
  date: string;
  day_number: number | null;
  sub_name: string | null;
  result_count: number;
}

export interface OverviewBestSwim {
  result_id: number;
  swimmer_id: number;
  first_name: string;
  last_name: string;
  first_name_en: string;
  last_name_en: string;
  club: string;
  style_name: string;
  distance: string;
  gender: string;
  time: string;
  international_points: number;
  is_relay: boolean;
  relay_team_name: string | null;
  day_number: number | null;
  competition_id: number;
}

/** Строка клубного зачёта — контракт /api/club-summary (useClubSummary). */
export interface OverviewClub {
  club: string;
  points: number;
  swimmerCount: number;
  successfulCount: number;
  gold: number;
  silver: number;
  bronze: number;
}

export interface OverviewMedalist {
  swimmer_id: number;
  first_name: string;
  last_name: string;
  first_name_en: string;
  last_name_en: string;
  club: string;
  gold: number;
  silver: number;
  bronze: number;
}

/** Одна награда High Point Award (лучший по сумме очков в возраст × пол). */
export interface OverviewHighPoint {
  age: number;
  /** Masters: возрастная группа как в фильтрах ("25-29"); пусто для не-masters. */
  age_group: string;
  /** "male" | "female" */
  gender: string;
  swimmer_id: number;
  first_name: string;
  last_name: string;
  first_name_en: string;
  last_name_en: string;
  club: string;
  points: number;
  is_tie: boolean;
}

/** Зарезервированный контракт карточки рекорда (v1 сервер отдаёт пусто). */
export interface OverviewRecord {
  kind: string;
  style_name: string;
  distance: string;
  gender: string;
  time: string;
  holder_name: string;
  swimmer_id: number;
  /** Возрастная группа держателя ("25-29"); пусто, если нет в данных. */
  age_group: string;
  club: string | null;
  day_number: number | null;
  result_id: number | null;
}

export interface CompetitionOverview {
  summary: OverviewSummary;
  days: OverviewDay[];
  best_swim: OverviewBestSwim | null;
  best_swim_male: OverviewBestSwim | null;
  best_swim_female: OverviewBestSwim | null;
  top_clubs: OverviewClub[];
  top_clubs_men: OverviewClub[];
  top_clubs_women: OverviewClub[];
  top_medalist: OverviewMedalist | null;
  top_medalist_male: OverviewMedalist | null;
  top_medalist_female: OverviewMedalist | null;
  high_point_awards: OverviewHighPoint[];
  records: OverviewRecord[];
}

export interface CompetitionHeaderProps {
  /** Заголовок источника (title из dataSourceSelected). */
  title: string;
  /** { competitionId } | { eventId } источника — для overview-API и Add media. */
  sourceParams: Record<string, string>;
  overview: CompetitionOverview | null;
  activeTab: CompetitionTab;
  onTabChange(tab: CompetitionTab): void;
  /** Число публичных медиа соревнования (бейдж таба Media); null — ещё не известно. */
  mediaCount: number | null;
}

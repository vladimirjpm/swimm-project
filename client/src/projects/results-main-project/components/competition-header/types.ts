// Типы модульной шапки соревнования (design_handoff_competition_overview, вариант 1b).
// DTO зеркалят server/Swimm.Application/Dtos/CompetitionOverviewDto.cs (snake_case).

// 'overview' — мастер-детейл с цветными модулями (design_handoff_competition_overview2, 9d/9f).
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
  /** Ошибка протокола (И11). null — заплыв в порядке. */
  suspect_reason?: string | null;
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

/** Строка шкалы правила клубных очков: место → очки. */
export interface ClubPointsRuleEntry {
  place: number;
  points: number;
}

/** Объяснение расхождения с официальным зачётом: тексты по языкам + табличка расхождения. */
export interface CompetitionMismatchNote {
  /** Язык ('en'|'ru'|'he') → текст. Языка нет — вкладка в попапе выключена. */
  texts: Record<string, string>;
  /** Строки «место / по регламенту / начислено официально»; пусто — только проза. */
  scale_diff: { place: number; expected: number; actual: number }[];
}

/** Правило клубных очков, применённое к этому зачёту (overview.club_points_rules). */
export interface ClubPointsRule {
  version: string;
  description: string | null;
  /** 'all' | 'masters' | 'non-masters'. */
  scope: string;
  /** 'yyyy-MM-dd'. */
  effective_from: string;
  default_points: number;
  max_scoring_place: number | null;
  relay_multiplier: number;
  points_by_place: ClubPointsRuleEntry[];
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
  /** Сколько из медалей эстафетные (медаль эстафеты идёт каждому участнику). */
  relay_medals: number;
  /** true — такой же набор медалей ещё у кого-то: награда делится. */
  is_tie: boolean;
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
  /** Правило требует «только финалы», но признака типа заплыва в данных нет —
   *  посчитано по всем заплывам (сноска в карточке). */
  finals_only_unavailable?: boolean;
}

/** Зарезервированный контракт карточки рекорда (v1 сервер отдаёт пусто). */
export interface OverviewRecord {
  kind: string;
  style_name: string;
  distance: string;
  gender: string;
  time: string;
  /** Ошибка протокола (И11). У рекорда почти всегда null: помеченные заплывы рекордов не бьют. */
  suspect_reason?: string | null;
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
  /**
   * Наградное ли соревнование (Competition.IsAward). У ненаградных (лиги, отборы) места в
   * протоколе есть, а медалей нет — Overview прячет всё медальное: Most decorated, медали
   * в клубном зачёте, High Point. Сами расчёты сервер отдаёт всегда, флаг влияет на показ.
   */
  has_awards: boolean;
  days: OverviewDay[];
  best_swim: OverviewBestSwim | null;
  best_swim_male: OverviewBestSwim | null;
  best_swim_female: OverviewBestSwim | null;
  top_clubs: OverviewClub[];
  /** Правила, по которым посчитан клубный зачёт (обычно одно) — попап «How points are scored». */
  club_points_rules: ClubPointsRule[];
  /** Итог ручной проверки очков: 'official' | 'accepted' | 'mismatch' | null (не проверялось
   *  либо в выборке смешаны разные итоги). */
  club_points_verified: string | null;
  /** Чем именно наши очки расходятся с официальными: проза на трёх языках + табличка
   *  «место / по регламенту / начислено». Показывается в попапе «Points system».
   *  Приходит только вместе с 'mismatch'. */
  club_points_mismatch_note: CompetitionMismatchNote | null;
  top_clubs_men: OverviewClub[];
  top_clubs_women: OverviewClub[];
  /** Самые титулованные: при равном наборе медалей — все, а не первый попавшийся. */
  top_medalists: OverviewMedalist[];
  top_medalists_male: OverviewMedalist[];
  top_medalists_female: OverviewMedalist[];
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

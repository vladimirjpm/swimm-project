// DTO публичного API групп (/api/hub-groups*) — ключи snake_case, как отдаёт сервер.

export interface HubGroupListItem {
  slug: string;
  name: string;
  name_en?: string | null;
  description?: string | null;
  icon_url?: string | null;
  location?: string | null;
  /** Alpha-3 код страны группы (ISR…), null — не задана. Флаг — через UI_FlagEmoji. */
  country?: string | null;
  club_name?: string | null;
  /** Официальная группа клуба (одобрена админом) — не путать с составом-watchlist. */
  is_official: boolean;
  member_count: number;
}

export interface HubGroupLink {
  kind: string; // whatsapp | telegram | instagram | site
  url: string;
}

export interface HubGroupMember {
  swimmer_id: number;
  name: string;
  name_en: string;
  birth_year: number;
  club_name?: string | null;
  role: 'member' | 'captain' | 'coach';
  /** Админ группы, чей аккаунт привязан к этому пловцу (сервер: HubGroupRosterOrder) — чип «admin». */
  is_admin?: boolean;
}

export interface HubGroupBest {
  style_name: string;
  distance: string;
  pool_type?: string | null;
  gender: string;
  time_original: string;
  /** Ошибка протокола (И11). null — заплыв в порядке. */
  suspect_reason?: string | null;
  time_millisecond?: number | null;
  swimmer_id: number;
  swimmer_name: string;
  swimmer_name_en: string;
  /** День старта, где поставлен рекорд (Competitions.Id). */
  competition_id?: number;
  competition_name: string;
  date: string; // dd/MM/yyyy
  points: number;
}

/** Поля ResultDto, которые использует страница группы (полный контракт — server/ResultDto). */
export interface HubGroupRecentResult {
  id: number;
  competition: string;
  date: string; // dd/MM/yyyy
  event_style_name: string;
  event_style_len: string;
  pool_type?: string | null;
  position?: number | null;
  last_name: string;
  first_name: string;
  last_name_en: string;
  first_name_en: string;
  time: string;
  time_ms?: number | null;
  time_fail: boolean;
  /** Причина снятия («DQ / SW 4.4», «NS») — без неё у времени остаётся одна красная «*». */
  time_fail_note?: string | null;
  /** Ошибка протокола (И11): время, показанное без этого признака, выдаёт себя за чистое. */
  suspect_reason?: string | null;
  international_points: number;
  /** Входы единого правила медали (`HelperResults.isMedalPlace`). */
  is_award: boolean;
  heat_type?: string | null;
  round?: string | null;
  is_relay: boolean;
  relay_team_name?: string | null;
  /** Состав эстафеты текстом: строка принадлежит одной ноге, а плыла команда (docs/relays.md). */
  relay_swimmers_name?: string | null;
}

/**
 * Последний старт ростера ЦЕЛИКОМ — считает сервер (`HubGroupLastStartDto`). Резать его из
 * `recent_results` нельзя: лента обрезана до 25 строк, и на чемпионате её не хватает даже на
 * два дня из трёх.
 */
export interface HubGroupLastStart {
  competition_id: number;
  event_id?: number | null;
  name: string;
  date_from: string; // dd/MM/yyyy
  date_to: string;
  swims: number;
  golds: number;
  silvers: number;
  bronzes: number;
  /** Лучшие заплывы старта, уже отсортированы сервером: медали, места, снятые в конце. */
  rows: HubGroupRecentResult[];
}

/**
 * Строка списка стартов группы (таб Results, чип «Results»): турнир целиком — дни
 * многодневки сложены сервером, эстафеты по членству. Свежие сверху.
 */
export interface HubGroupCompetition {
  /** Последний день старта, в который плыл ростер. */
  competition_id: number;
  event_id?: number | null;
  name: string;
  date_from: string; // dd/MM/yyyy
  date_to: string;
  swimmers: number;
  swims: number;
  golds: number;
  silvers: number;
  bronzes: number;
  /** Действующие рекорды группы, поставленные на этом старте. */
  records: number;
}

/**
 * Карточка ленты хайлайтов шапки группы (design_handoff_group_header).
 * Дискриминированный union по type; состав и порядок задаёт сервер
 * (HubGroupHighlightsBuilder) — клиент рендерит массив как есть.
 * Новый тип карточки = новый вариант union + ветка в HighlightCard.
 */
export type HubGroupHighlight =
  | { type: 'record'; badge: string; title: string; detail: string; url: string }
  | { type: 'medals'; badge: string; place: string; place_label: string;
      gold: number; silver: number; bronze: number; url: string }
  | { type: 'video'; label: string; duration?: string | null; thumb_url?: string | null; url: string }
  | { type: 'photo'; label: string; extra?: string | null; thumb_url?: string | null; url: string };

// Канонический тип медиа объявлен один раз в utils/interfaces/results.ts (HubGroupMediaItem).
import type { HubGroupMediaItem } from '../../utils/interfaces/results';
export type { HubGroupMediaItem };

/** Медиа members-слоя (тренерские разборы, 2B′) — GET /api/hub-groups/{slug}/media/members. */
export interface HubGroupMemberMediaItem {
  id: number;
  media_type: HubGroupMediaItem['media_type'];
  source_type: HubGroupMediaItem['source_type'];
  url: string;
  caption?: string | null;
  created_at: string;
  swimmer_id?: number | null;
  swimmer_name?: string | null;
  swimmer_name_en?: string | null;
  result_id?: number | null;
  /** «freestyle 100 · 01/07/2026 · Competition» — контекст заплыва-якоря. */
  result_label?: string | null;
  /** Соревнование заплыва-якоря — без него подпись некликабельна (routes.competitionSwims). */
  competition_id?: number | null;
}

/** Одно регулярное занятие недели: день ISO (1 = Mon … 7 = Sun), часы «HH:mm». */
export interface GroupTrainingSlot {
  day: number;
  start: string;
  end?: string | null;
}

/** Регулярное расписание группы (HubGroups.TrainingSchedule). */
export interface GroupTrainingSchedule {
  slots: GroupTrainingSlot[];
  place?: string | null;
  pool_type?: string | null;
  note?: string | null;
  /** Сколько дорожек обычно (1..12) — вид по дорожкам без плана (Ш3). */
  usual_lanes?: number | null;
  /** auto — план, иначе раскладка на лету; plan — только план; off — вида нет. */
  lane_view?: LaneViewMode | null;
  /** members — имена «кто идёт» видят все участники; coach — только управляющие. */
  who_is_coming?: WhoIsComing | null;
  /** Режим «сверху» (Ш4): не ответившему участнику «Are you coming?» над фото. */
  rsvp_top?: boolean | null;
}

export type LaneViewMode = 'auto' | 'plan' | 'off';
export type WhoIsComing = 'members' | 'coach';

/** Ближайшее занятие — СЧИТАЕТ СЕРВЕР в поясе Израиля, клиент только рисует. */
export interface NextTraining {
  /** Ключ занятия `yyyy-MM-dd-HHmm` — адрес ответов (`/api/hub-groups/{id}/rsvp/{id}`). */
  id?: string;
  /** yyyy-MM-dd */
  date: string;
  start: string;
  end?: string | null;
  place?: string | null;
  pool_type?: string | null;
}

/**
 * Строка inbox-а модерации (GroupPublicationInboxItemDto, snake_case):
 * GET /api/hub-groups/{id}/media/publications. ⚠ Только модераторам — в ней владелец медиа.
 * Ленты зрителя (GET .../media/published?level=...) отдают {@link PublishedMediaItem}.
 */
export interface GroupPublicationItem {
  id: number;
  level: 'public' | 'members';
  status: 'pending' | 'approved' | 'rejected';
  created_at: string;
  media_type: HubGroupMediaItem['media_type'];
  source_type: HubGroupMediaItem['source_type'];
  url: string;
  owner_user_id: number;
  owner_email: string;
  /** Id медиа (Sys_UserMedia). */
  media_id?: number;
  /** Жалобы «Report» (Р62): null | under_review (спрятано до решения админа сайта) | removed. */
  moderation_state?: 'under_review' | 'removed' | null;
  /** Открытые жалобы: причина → сколько. Без имён и текста — их видит только админ сайта. */
  open_reports?: Record<string, number>;
  swimmer_id?: number | null;
  swimmer_name?: string | null;
  result_id?: number | null;
  result_label?: string | null;
  /** Соревнование медиа: день заплыва, либо само соревнование у медиа без заплыва. */
  competition_id?: number | null;
}

/**
 * Одобренная публикация в ленте зрителя (PublishedMediaItemDto): GET .../media/published
 * (public и members). Без владельца медиа — кто подал, знают только модераторы.
 */
export interface PublishedMediaItem {
  /** Id ПУБЛИКАЦИИ (не медиа). */
  id: number;
  /** Id медиа (Sys_UserMedia) — для жалобы «Report» (Р62). */
  media_id?: number;
  media_type: HubGroupMediaItem['media_type'];
  source_type: HubGroupMediaItem['source_type'];
  url: string;
  swimmer_id?: number | null;
  swimmer_name?: string | null;
  result_id?: number | null;
  result_label?: string | null;
  /** Соревнование медиа: день заплыва, либо само соревнование у медиа без заплыва. */
  competition_id?: number | null;
}

export interface HubGroupStanding {
  swimmer_id: number;
  name: string;
  name_en: string;
  role: 'member' | 'captain' | 'coach';
  swims: number;
  golds: number;
  silvers: number;
  bronzes: number;
  club_points: number;
  best_fina: number;
}

export interface HubGroupDetails {
  /** Числовой id — для самозаписи; 0 у виртуального «избранного». */
  id: number;
  slug: string;
  name: string;
  name_en?: string | null;
  description?: string | null;
  icon_url?: string | null;
  cover_image_url?: string | null;
  /** Фото шапки, УЖЕ разрешённое сервером (указатель hero.mediaId → обложка). */
  hero_image_url?: string | null;
  /** Фото шапки для телефона (4:3), УЖЕ разрешённое сервером; null — полоса из десктопного. */
  hero_image_mobile_url?: string | null;
  /** Сырой url мобильного фото (колонка) — его правит форма Admin. */
  cover_image_mobile_url?: string | null;
  /** Какое медиа помечено мобильным фото шапки; null — берётся колонка. */
  hero_mobile_media_id?: number | null;
  /** Показывать блок фото (настройка hero.show). */
  show_hero_image?: boolean;
  /** Какое медиа помечено фото шапки; null — берётся обложка. */
  hero_media_id?: number | null;
  location?: string | null;
  /** Alpha-3 код страны группы (ISR…), null — не задана. Флаг — через UI_FlagEmoji. */
  country?: string | null;
  club_name?: string | null;
  /** Официальная группа клуба (одобрена админом) — не путать с составом-watchlist. */
  is_official: boolean;
  /**
   * Доверенная группа (Р56: флаг «Trusted» или официальная): её public-медиа видны всем и в
   * протоколе, и на карточке пловца. false — таб Admin показывает управляющим сообщение Р58.
   */
  is_trusted?: boolean;
  /** open | approval — политика самозаписи (кнопка «Вступить» vs «Подать заявку»). */
  join_policy?: 'open' | 'approval';
  /** Группа только для участников (§6-6). Участник видит её целиком, с пометкой. */
  is_private?: boolean;
  /**
   * Заглушка для НЕ-участника приватной группы: только имя, иконка и как вступить (заявкой) —
   * ни состава, ни результатов, ни медиа. Массивы в ней пустые.
   */
  members_only?: boolean;
  /** Клуб, на который подписана группа (состав из клуба); null — подписки нет. */
  followed_club_id?: number | null;
  followed_club_name?: string | null;
  /**
   * Официальная группа клуба подписки, если это не эта группа: копию клуба открыли по ссылке
   * мимо каталога — шапка показывает, где «лицо клуба» (П4 плана подписки).
   */
  official_group_slug?: string | null;
  official_group_name?: string | null;
  links: HubGroupLink[];
  is_virtual: boolean;
  members: HubGroupMember[];
  recent_results: HubGroupRecentResult[];
  /** Последний старт целиком; null — ростер ещё не плыл. */
  last_start?: HubGroupLastStart | null;
  bests: HubGroupBest[];
  /** Лучшее по той же оси, но за текущий сезон (`season_label`). Старый сервер не шлёт. */
  season_bests?: HubGroupBest[];
  /** Все старты ростера, свежие сверху. Старый сервер поля не шлёт — отсюда `?`. */
  competitions?: HubGroupCompetition[];
  season_label: string;
  standings: HubGroupStanding[];
  /** Публичная галерея группы (HubGroupMedia с TrainingId == null). */
  gallery: HubGroupMediaItem[];
  /** Лента хайлайтов шапки; пустая/отсутствует — модуль скрыт (старый вид шапки). */
  highlights?: HubGroupHighlight[];
  /** Регулярное расписание; null/отсутствует — не заведено, слоты шапки скрыты. */
  training_schedule?: GroupTrainingSchedule | null;
  /** Ближайшее занятие по расписанию (считает сервер). */
  next_training?: NextTraining | null;
}

// ── Уровни пловцов группы (docs/plans/lane-plans-plan.md, L1) ─────────────────
// GET/PUT /api/me/hub-groups/{id}/levels — только управляющим; camelCase, как весь /api/me.

export interface HubGroupLevel {
  id: number;
  /** 1 — сильнейший; сервер нумерует по порядку списка. */
  rank: number;
  name: string;
  description?: string | null;
  /** «#rrggbb»; null — цвет по рангу (`levelColor`). */
  color?: string | null;
  /** Пловцов состава на этом уровне. */
  swimmerCount: number;
  /** Аккаунтов-участников на этом уровне (Ш3.1). */
  accountCount?: number;
}

export interface HubGroupLevelSwimmer {
  swimmerId: number;
  /** Иврит по умолчанию, EN — фоллбек. */
  name: string;
  nameEn: string;
  birthYear: number;
  gender?: string | null;
  clubName?: string | null;
  levelId: number | null;
}

/** Активный участник-аккаунт со своим уровнем — действует, когда у него нет пловца на дорожке. */
export interface HubGroupLevelAccount {
  userId: number;
  name: string;
  /** Метка тренера (за какого пловца аккаунт); null — не привязан. */
  swimmerId: number | null;
  levelId: number | null;
}

export interface HubGroupLevels {
  levels: HubGroupLevel[];
  swimmers: HubGroupLevelSwimmer[];
  accounts?: HubGroupLevelAccount[];
}

// ── План дорожек (docs/plans/lane-plans-plan.md, L2–L3) ──────────────────────
// /api/hub-groups/{id}/lane-plans — snake_case (его читают и участники).

export type LanePlanStatus = 'draft' | 'published';

export interface LanePlanSummary {
  /** yyyy-MM-dd */
  date: string;
  status: LanePlanStatus;
  lane_count: number;
}

export interface LanePlanLevel {
  id: number;
  rank: number;
  name: string;
  color?: string | null;
}

export interface LanePlanSwimmer {
  swimmer_id: number;
  /** Иврит по умолчанию, EN — фоллбек. */
  name: string;
  name_en: string;
  birth_year: number;
  /** ТЕКУЩИЙ уровень пловца в группе (не снимок). */
  level_id?: number | null;
  /** Ушёл из состава — в плане остался (план — снимок). */
  left_group?: boolean;
  /** На перерыве в день плана (Ш3.1): в Unassigned новой раскладки не кладётся. */
  on_break?: boolean;
}

export interface LanePlanLane {
  lane_no: number;
  level?: LanePlanLevel | null;
  workout?: string | null;
  /** В порядке дорожки: первый ведёт. */
  swimmers: LanePlanSwimmer[];
}

export interface LanePlan {
  date: string;
  status: LanePlanStatus;
  lane_count: number;
  note?: string | null;
  updated_at: string;
  lanes: LanePlanLane[];
  unassigned: LanePlanSwimmer[];
  /** Только управляющему; участнику — пустой. */
  not_today: LanePlanSwimmer[];
  can_edit: boolean;
  /**
   * «Мои» пловцы этого плана — карточка «Your lane». Только подсветка, прав не даёт:
   * `me` — сам зритель, `family` — за кого он смотрит. Сперва `me`.
   */
  my_swimmers: { swimmer_id: number; kind: 'me' | 'family' }[];
}

export interface LanePlanInput {
  lane_count: number;
  note: string | null;
  lanes: { lane_no: number; level_id: number | null; workout: string | null }[];
  /** Порядок в массиве = порядок внутри дорожки; кого нет — «Not today». */
  swimmers: { swimmer_id: number; lane_no: number | null }[];
}

// ── Ответы «иду / не приду» (docs/plans/entity-hero-roles-plan.md, Ш2) ──────────
// GET/PUT /api/hub-groups/{id}/rsvp/{session} — личный ответ, только участникам и управляющим.

export type RsvpAnswer = 'yes' | 'maybe' | 'no';
export type RsvpNote = 'late' | 'first-hour' | 'leaving-early';

export interface TrainingRsvpPerson {
  user_id: number;
  name: string;
  gender?: 'male' | 'female' | null;
  /** null — не ответил. */
  answer: RsvpAnswer | null;
  note?: RsvpNote | null;
  set_by_coach: boolean;
  /** На перерыве в день занятия (Ш3.1). */
  on_break?: boolean;
  /** Сам вернулся с перерыва недавно — сколько дней был на нём. */
  back_after_days?: number | null;
}

/** Человек в бассейне вида по дорожкам (Ш3.2). Поля null — имя скрыто («Who's coming: coach»). */
export interface TrainingLanePerson {
  user_id: number | null;
  swimmer_id: number | null;
  name: string | null;
  gender: 'male' | 'female' | null;
  /** «не уверен» тоже занимает место — рисуется пунктиром. */
  answer: 'yes' | 'maybe';
  note?: RsvpNote | null;
  is_me: boolean;
  /** Сколько аккаунтов называют себя этим пловцом; 2+ — «2 claim». */
  claims: number;
}

export interface TrainingLane {
  lane_no: number;
  level: LanePlanLevel | null;
  workout?: string | null;
  people: TrainingLanePerson[];
}

export interface TrainingLaneView {
  /** plan — план тренера; auto — раскладка на лету; water — дорожек не знаем, одна «вода». */
  source: 'plan' | 'auto' | 'water';
  lane_count: number;
  lanes: TrainingLane[];
  no_lane: TrainingLanePerson[];
  names_hidden: boolean;
}

export interface TrainingRsvp {
  session_id: string;
  /** yyyy-MM-dd */
  date: string;
  start: string;
  end?: string | null;
  yes: number;
  maybe: number;
  no: number;
  /** Активные участники-аккаунты — знаменатель полосы. */
  total: number;
  mine: { answer: RsvpAnswer; note?: RsvpNote | null; set_by_coach: boolean } | null;
  is_member: boolean;
  can_manage: boolean;
  can_answer: boolean;
  /** Только управляющему. */
  people: TrainingRsvpPerson[] | null;
  /** Зритель на перерыве в день занятия: «Going» его снимет. */
  on_break?: boolean;
  /** yyyy-MM-dd — последний день перерыва зрителя; null — бессрочно. */
  break_until?: string | null;
  /** Вид по дорожкам (Ш3.2); null — выключен у группы или «только план», а плана нет. */
  lane_view?: TrainingLaneView | null;
}

// ── «On break» (Ш3.1) — GET/PUT /api/hub-groups/{id}/breaks, личное ──────────────

export interface HubGroupBreak {
  user_id: number | null;
  swimmer_id: number | null;
  name: string;
  /** yyyy-MM-dd */
  since: string;
  /** yyyy-MM-dd — последний день; null — бессрочно. */
  until: string | null;
  set_by_coach: boolean;
  back_after_days?: number | null;
}

export interface HubGroupBreaks {
  mine: HubGroupBreak | null;
  /** Только управляющему. */
  breaks: HubGroupBreak[] | null;
  returns: HubGroupBreak[] | null;
  can_manage: boolean;
}

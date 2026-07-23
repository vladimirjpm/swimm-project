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
}

export interface HubGroupBest {
  style_name: string;
  distance: string;
  pool_type?: string | null;
  gender: string;
  time_original: string;
  time_millisecond?: number | null;
  swimmer_id: number;
  swimmer_name: string;
  swimmer_name_en: string;
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
  international_points: number;
  is_relay: boolean;
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
}

/**
 * Публикация личного медиа участника в группу (GroupPublicationInboxItemDto, snake_case).
 * Используется и для published-списков (GET .../media/published?level=...), и для inbox
 * модерации (GET /api/hub-groups/{id}/media/publications) — форма одинаковая.
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
  swimmer_id?: number | null;
  swimmer_name?: string | null;
  result_id?: number | null;
  result_label?: string | null;
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
  location?: string | null;
  /** Alpha-3 код страны группы (ISR…), null — не задана. Флаг — через UI_FlagEmoji. */
  country?: string | null;
  club_name?: string | null;
  /** Официальная группа клуба (одобрена админом) — не путать с составом-watchlist. */
  is_official: boolean;
  /** open | approval — политика самозаписи (кнопка «Вступить» vs «Подать заявку»). */
  join_policy?: 'open' | 'approval';
  links: HubGroupLink[];
  is_virtual: boolean;
  members: HubGroupMember[];
  recent_results: HubGroupRecentResult[];
  bests: HubGroupBest[];
  season_label: string;
  standings: HubGroupStanding[];
  /** Публичная галерея группы (HubGroupMedia с TrainingId == null). */
  gallery: HubGroupMediaItem[];
  /** Лента хайлайтов шапки; пустая/отсутствует — модуль скрыт (старый вид шапки). */
  highlights?: HubGroupHighlight[];
}

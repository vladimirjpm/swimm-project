// DTO публичного API групп (/api/hub-groups*) — ключи snake_case, как отдаёт сервер.

export interface HubGroupListItem {
  slug: string;
  name: string;
  name_en?: string | null;
  description?: string | null;
  icon_url?: string | null;
  location?: string | null;
  club_name?: string | null;
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

export interface HubGroupDetails {
  slug: string;
  name: string;
  name_en?: string | null;
  description?: string | null;
  icon_url?: string | null;
  cover_image_url?: string | null;
  location?: string | null;
  club_name?: string | null;
  links: HubGroupLink[];
  is_virtual: boolean;
  members: HubGroupMember[];
  recent_results: HubGroupRecentResult[];
  bests: HubGroupBest[];
}

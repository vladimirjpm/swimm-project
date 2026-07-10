// DTO пользовательского API групп (/api/me/hub-groups*, 8.6) — ключи camelCase
// (дефолт System.Text.Json для контроллеров), в отличие от snake_case публичного API в types.ts.

export interface MyHubGroupRow {
  id: number;
  name: string;
  slug: string;
  iconUrl?: string | null;
  clubName?: string | null;
  memberCount: number;
  isPublic: boolean;
  updatedAt: string;
}

export interface HubGroupLinkInput {
  kind: string;
  url: string;
}

export interface HubGroupInput {
  name: string;
  nameEn?: string | null;
  slug?: string | null;
  description?: string | null;
  iconUrl?: string | null;
  coverImageUrl?: string | null;
  location?: string | null;
  clubId?: number | null;
  isPublic: boolean;
  links: HubGroupLinkInput[];
}

export interface HubGroupMemberRow {
  id: number;
  swimmerId: number;
  swimmerName: string;
  swimmerNameEn: string;
  birthYear: number;
  clubName?: string | null;
  role: 'member' | 'captain' | 'coach';
  sortOrder: number;
}

export interface HubGroupEditData {
  id: number;
  name: string;
  nameEn?: string | null;
  slug: string;
  description?: string | null;
  iconUrl?: string | null;
  coverImageUrl?: string | null;
  location?: string | null;
  clubId?: number | null;
  ownerUserId: number;
  ownerDisplayName: string;
  isPublic: boolean;
  links: HubGroupLinkInput[];
  members: HubGroupMemberRow[];
}

export interface SwimmerSearchResult {
  id: number;
  name: string;
  nameEn: string;
  birthYear: number;
  clubName?: string | null;
}

export interface HubGroupManager {
  userId: number;
  displayName: string;
  email: string;
  createdAt: string;
}

export interface CreateEligibility {
  canCreate: boolean;
  reason?: string | null;
  remaining?: number | null;
}

export interface SaveResult {
  success: boolean;
  id?: number;
  error?: string | null;
}

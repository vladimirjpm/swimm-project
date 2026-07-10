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
  isOfficial: boolean;
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

/** Участник-аккаунт группы (приватный список, не пловец). */
export interface HubGroupUserMember {
  userId: number;
  displayName: string;
  email: string;
  status: 'active' | 'pending';
  selfJoined: boolean;
  joinedAt: string;
}

/** Группа, в которую текущий пользователь вступил (список «участвую»). */
export interface JoinedHubGroup {
  id: number;
  name: string;
  slug: string;
  iconUrl?: string | null;
  status: 'active' | 'pending';
  joinedAt: string;
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
  isOfficial: boolean;
  links: HubGroupLinkInput[];
  members: HubGroupMemberRow[];
  userMembers: HubGroupUserMember[];
}

export interface SwimmerSearchResult {
  id: number;
  name: string;
  nameEn: string;
  birthYear: number;
  clubName?: string | null;
}

export interface HubGroupAdmin {
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

export interface ClubOption {
  id: number;
  name: string;
}

export interface HubGroupClubRequestInput {
  clubId: number;
  message?: string | null;
}

/** pending | approved | rejected */
export interface ClubRequest {
  id: number;
  clubId: number;
  clubName: string;
  message?: string | null;
  status: 'pending' | 'approved' | 'rejected';
  createdAt: string;
  decidedAt?: string | null;
}

export interface SaveResult {
  success: boolean;
  id?: number;
  error?: string | null;
}

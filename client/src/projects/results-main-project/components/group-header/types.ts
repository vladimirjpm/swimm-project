// Типы модульной шапки группы (design_handoff_group_header, вариант 2b).
// DTO-типы (HubGroupDetails, HubGroupHighlight) живут в hub-groups-project/types.ts —
// здесь только пропсы компонентов.

import type { HubGroupDetails, HubGroupHighlight } from '../../../hub-groups-project/types';

export type GroupTab = 'competitions' | 'trainings';

export interface GroupHeaderProps {
  /** slug из URL — рендерим шапку по нему ещё до прихода DTO. */
  slug: string;
  /** null, пока /api/hub-groups/:slug грузится. */
  group: HubGroupDetails | null;
  /** Лента хайлайтов; пусто/undefined — модуль не рендерится (старый вид шапки). */
  highlights?: HubGroupHighlight[];
  activeTab: GroupTab;
  onTabChange(tab: GroupTab): void;
}

export type { HubGroupDetails, HubGroupHighlight };

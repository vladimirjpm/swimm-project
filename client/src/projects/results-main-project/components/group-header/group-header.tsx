import React from 'react';
import GroupHeaderTop from './group-header-top';
import GroupHighlights from './group-highlights';
import GroupTabs from './group-tabs';
import type { GroupHeaderProps } from './types';

// Контейнер шапки группы (design_handoff_group_header, вариант 2b) — ТОЛЬКО компонует:
//   GroupHeaderTop  (primary)  → GroupHighlights (surface, скрыта если пусто) → GroupTabs (primary).
// Каждый модуль сам красит свой фон токенами темы, контейнер даёт скругление и тень.

export default function GroupHeader({ slug, group, highlights, activeTab, onTabChange }: GroupHeaderProps) {
  return (
    <div
      className="overflow-hidden rounded-[14px]"
      style={{ boxShadow: 'var(--theme-mode-card-shadow)' }}
    >
      <GroupHeaderTop slug={slug} group={group} />
      <GroupHighlights highlights={highlights} />
      <GroupTabs slug={slug} activeTab={activeTab} onTabChange={onTabChange} />
    </div>
  );
}

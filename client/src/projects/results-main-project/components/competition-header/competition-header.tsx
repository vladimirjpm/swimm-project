import React from 'react';
import CompetitionHeaderTop from './competition-header-top';
import CompetitionTabs from './competition-tabs';
import type { CompetitionHeaderProps } from './types';

// Контейнер шапки соревнования (design_handoff_competition_overview, вариант 1b) —
// ТОЛЬКО компонует: Hero (primary) → [персональная полоса — TODO, отдельный модуль,
// только залогиненному] → CompetitionTabs (primary). Паттерн — GroupHeader.

export default function CompetitionHeader({
  title,
  overview,
  activeTab,
  onTabChange,
  mediaCount,
  onAddMedia,
  source,
  onChangeClick,
  changeOpen,
}: CompetitionHeaderProps & {
  onAddMedia?: () => void;
  source?: import('../../../../utils/helpers/competition-source').CompetitionSource;
  onChangeClick?: () => void;
  changeOpen?: boolean;
}) {
  return (
    <div
      className="overflow-hidden rounded-[14px] md:rounded-t-none"
      style={{ boxShadow: 'var(--theme-mode-card-shadow)' }}
    >
      <CompetitionHeaderTop
        title={title}
        overview={overview}
        source={source}
        onAddMedia={onAddMedia}
        onChangeClick={onChangeClick}
        changeOpen={changeOpen}
      />
      {/* Персональная полоса (⭐ Имя / ❤️ Favorites / My media) — следующий модуль,
          красится ТОЛЬКО токенами --theme-personal-* */}
      <CompetitionTabs
        overview={overview}
        activeTab={activeTab}
        onTabChange={onTabChange}
        mediaCount={mediaCount}
      />
    </div>
  );
}

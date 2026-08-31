import React from 'react';
import CompetitionHeaderTop from './competition-header-top';
import CompetitionPersonalStrip from './competition-personal-strip';
import CompetitionTabs from './competition-tabs';
import CombineBar from './combine-bar';
import CompetitionStickyBar from './competition-sticky-bar';
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
  startListEntries,
  onAddMedia,
  source,
  onChangeClick,
  changeOpen,
  onOpenSwimsScoped,
}: CompetitionHeaderProps & {
  onAddMedia?: () => void;
  source?: import('../../../../utils/helpers/competition-source').CompetitionSource;
  onChangeClick?: () => void;
  changeOpen?: boolean;
  /** Переход в Swims со скоупом my|favorites (персональная полоса). */
  onOpenSwimsScoped?: (scope: 'my' | 'favorites') => void;
}) {
  return (
    <>
      {/* Стики-минимизация (16b): полная шапка остаётся в потоке как есть, а поверх
          контента выезжает компакт-бар. Живёт рядом с шапкой, рисуется порталом в body.
          Хендофф описывал мобайл; на десктопе то же поведение (решение Влада 31.08.2026). */}
      <CompetitionStickyBar
        title={title}
        overview={overview}
        source={source}
        activeTab={activeTab}
        onTabChange={onTabChange}
        mediaCount={mediaCount}
        startListEntries={startListEntries}
        onAddMedia={onAddMedia}
        onChangeClick={onChangeClick}
      />
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
      {/* Персональная полоса (только залогиненному; красится токенами --theme-personal-*).
          В мобайле её нет (handoff v3, 9f) — её роль выполняет таб-модуль Favorites. */}
      {onOpenSwimsScoped && (
        <div className="hidden lg:block">
          <CompetitionPersonalStrip
            overview={overview}
            onOpenSwims={onOpenSwimsScoped}
            onOpenMedia={() => onTabChange('media')}
            onAddMedia={onAddMedia}
          />
        </div>
      )}
      <CompetitionTabs
        overview={overview}
        activeTab={activeTab}
        onTabChange={onTabChange}
        mediaCount={mediaCount}
        startListEntries={startListEntries}
      />
      {/* Combine All Results — полоса сразу под табами, перед контентом таба (handoff 12b).
          Сама решает, рендериться ли (только там, где тумблер был доступен раньше).

          На табе Start list её нет вовсе: полоса объединяет результаты РАЗНЫХ ДНЕЙ в один
          зачёт, а в стартовом протоколе результатов ещё нет — и дни там, наоборот, разводятся
          (у детских чемпионатов каждый день свой). Управлять из протокола нечем, а место
          она занимает. */}
      {activeTab !== 'startlist' && <CombineBar />}
    </div>
    </>
  );
}

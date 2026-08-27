import React from 'react';
import type { CompetitionOverview, CompetitionTab } from './types';
import { PAGE_CONTAINER } from '../../../../utils/layout';

// Табы шапки соревнования (паттерн GroupTabs: primary + внутренний bg-black/10).
// Overview | Swims N | Clubs N | Records N (только если рекорды есть) | Media N.
// Тогглера Combine All Results тут больше нет: он стал полосой под табами
// (combine-bar.tsx, handoff 12b) — на всех брейкпоинтах.

interface Props {
  overview: CompetitionOverview | null;
  activeTab: CompetitionTab;
  onTabChange(tab: CompetitionTab): void;
  mediaCount: number | null;
  /** Число заявок стартового протокола; таб рисуется только когда есть org_comp_id
   *  (шаг 1.1, start-list-ui-sonnet.md) — по образцу условного таба Records. */
  startListEntries?: number | null;
}

export default function CompetitionTabs({ overview, activeTab, onTabChange, mediaCount, startListEntries }: Props) {
  const tabs: { tab: CompetitionTab; label: string; count?: number | null }[] = [
    { tab: 'overview', label: 'Overview' },
    { tab: 'swims', label: 'Swims', count: overview?.summary.result_count },
    { tab: 'clubs', label: 'Clubs', count: overview?.summary.club_count },
    // Records — только когда рекорды есть (v1 сервер отдаёт пусто → таба нет)
    ...(overview?.records.length
      ? [{ tab: 'records' as const, label: 'Records', count: overview.records.length }]
      : []),
    { tab: 'media', label: 'Media', count: mediaCount },
    // Start list — только когда у соревнования есть org_comp_id: без него стартовому
    // протоколу неоткуда взяться (решение 1, шаг 0 задания).
    ...(overview?.org_comp_id != null
      ? [{ tab: 'startlist' as const, label: 'Start list', count: startListEntries }]
      : []),
  ];

  return (
    // Фон табов — край-в-край; сами табы в общем контейнере (handoff v2, 5a).
    <div style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}>
      <div className="bg-black/10">
      <div className={`${PAGE_CONTAINER} flex items-center gap-0.5 overflow-x-auto`}>
        {tabs.map(({ tab, label, count }) => (
          <button
            key={tab}
            type="button"
            onClick={() => onTabChange(tab)}
            className={`shrink-0 border-b-[2.5px] bg-transparent px-3 pb-3 pt-[10px] text-[13px] font-bold ${
              activeTab === tab ? 'opacity-100' : 'opacity-75 hover:opacity-100'
            }`}
            style={{
              color: 'inherit',
              borderBottomColor: activeTab === tab ? 'var(--theme-mode-accent-text)' : 'transparent',
            }}
          >
            {label}
            {count != null && count > 0 && (
              <span className="ml-1 text-[11px] font-semibold opacity-70">{count}</span>
            )}
          </button>
        ))}
      </div>
      </div>
    </div>
  );
}

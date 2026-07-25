import React from 'react';
import type { GroupTab } from './types';
import { routes } from '../../../../utils/routes';

// Нижний модуль шапки: Overview/Members/Records — ссылки на обзорную groups.html (↗),
// Competitions / 🔒Trainings — локальный toggle (единственное место переключения
// активности, см. FilterActivity). Затемнение bg-black/10 поверх primary — как в демо.

interface GroupTabsProps {
  slug: string;
  activeTab: GroupTab;
  onTabChange(tab: GroupTab): void;
}

const OVERVIEW_TABS = [
  { label: 'Overview', anchor: '' },
  { label: 'Members', anchor: '#members' },
  { label: 'Records', anchor: '#records' },
];

export default function GroupTabs({ slug, activeTab, onTabChange }: GroupTabsProps) {
  return (
    <div style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}>
      <div className="flex flex-wrap items-center gap-0.5 bg-black/10 px-4">
        {OVERVIEW_TABS.map((t) => (
          <a
            key={t.label}
            href={`${routes.group(slug)}${t.anchor}`}
            className="border-b-[2.5px] border-transparent px-3 pb-3 pt-[10px] text-[13px] font-bold no-underline opacity-75 hover:opacity-100"
            style={{ color: 'inherit' }}
          >
            {t.label}
            <span className="ml-0.5 text-[10px] opacity-70">↗</span>
          </a>
        ))}
        <span
          className="mx-2 h-[18px] w-px"
          style={{ background: 'color-mix(in srgb, var(--theme-mode-accent-text) 30%, transparent)' }}
        />
        {(['competitions', 'trainings'] as const).map((tab) => (
          <button
            key={tab}
            type="button"
            onClick={() => onTabChange(tab)}
            className={`border-b-[2.5px] bg-transparent px-3 pb-3 pt-[10px] text-[13px] font-bold ${
              activeTab === tab ? 'opacity-100' : 'opacity-75'
            }`}
            style={{
              color: 'inherit',
              borderBottomColor: activeTab === tab ? 'var(--theme-mode-accent-text)' : 'transparent',
            }}
          >
            {tab === 'trainings' ? '🔒 Trainings' : 'Competitions'}
          </button>
        ))}
      </div>
    </div>
  );
}

import React from 'react';
import type { OverviewRecord } from './types';
import { routes } from '../../../../utils/routes';
import UI_SwimmerNameCell from '../../../components/mix/swimmer-name-cell/swimmer-name-cell';
import { cardStyle, SectionTitle } from './overview-shared';

// Модуль «New records»: новые рекорды соревнования, сгруппированные по пловцу
// (id/имя), сорт по числу рекордов ↓; топ-2 держателя + ссылка на таб Records.

interface RecordGroup { key: string; holderName: string; swimmerId: number; club: string | null; ageGroup: string; items: OverviewRecord[]; }
function groupRecords(records: OverviewRecord[]): RecordGroup[] {
  const map = new Map<string, RecordGroup>();
  for (const r of records) {
    const key = r.swimmer_id > 0 ? `id:${r.swimmer_id}` : `name:${r.holder_name}`;
    let g = map.get(key);
    if (!g) { g = { key, holderName: r.holder_name, swimmerId: r.swimmer_id, club: r.club, ageGroup: r.age_group, items: [] }; map.set(key, g); }
    g.items.push(r);
  }
  return [...map.values()].sort(
    (a, b) => b.items.length - a.items.length || a.holderName.localeCompare(b.holderName),
  );
}

interface Props {
  records: OverviewRecord[];
  onOpenTab(tab: 'records'): void;
  onOpenSwim?(swim: { result_id: number | null; style_name: string; distance: string }): void;
}

export default function CompetitionNewRecords({ records, onOpenTab, onOpenSwim }: Props) {
  if (records.length === 0) return null;
  return (
    <div data-module="new-records" className="rounded-[12px] p-4" style={cardStyle}>
      <SectionTitle>New records ({records.length})</SectionTitle>
      {groupRecords(records).slice(0, 2).map((g) => (
        <div key={g.key} className="border-t py-2 first:border-t-0" style={{ borderColor: 'var(--theme-mode-border)' }}>
          <div className="flex items-center justify-start gap-2">
            <UI_SwimmerNameCell
              firstName={g.holderName}
              club={g.club ?? undefined}
              showClubIcon
              clubIconSide="left"
              clubIconWidth="7"
              onClick={g.swimmerId > 0 ? () => { window.location.href = routes.swimmer(g.swimmerId); } : undefined}
              className="min-w-0"
              nameBlockClassName="min-w-0"
              firstLineClassName="truncate text-[13px] font-extrabold text-[var(--theme-mode-text)]"
              secondLineClassName="truncate text-[11.5px] font-semibold text-[var(--theme-mode-text-secondary)]"
            />
            {g.ageGroup && (
              <span
                className="shrink-0 whitespace-nowrap rounded-[7px] px-1.5 py-[2px] text-[11px] font-extrabold"
                style={{ background: 'color-mix(in srgb, var(--theme-primary) 12%, transparent)', color: 'var(--theme-primary)' }}
              >
                {g.ageGroup}
              </span>
            )}
          </div>
          {g.items.map((r, i) => (
            <div
              key={i}
              className={`mt-1 text-[12.5px] font-bold ${onOpenSwim && r.result_id != null ? 'cursor-pointer hover:underline' : ''}`}
              dir="auto"
              onClick={onOpenSwim && r.result_id != null ? () => onOpenSwim(r) : undefined}
            >
              {r.distance}m {r.style_name} — {r.time}
            </div>
          ))}
        </div>
      ))}
      <button type="button" onClick={() => onOpenTab('records')}
        className="mt-2 bg-transparent p-0 text-[12px] font-bold hover:underline"
        style={{ color: 'var(--theme-primary)' }}>
        Records tab →
      </button>
    </div>
  );
}

import React, { useMemo } from 'react';
import type { CompetitionOverview, OverviewRecord } from '../types';
import { initials } from './module-defs';

// Карточка модуля New records (9d): слева рекордсмены (сгруппированы по holder_name,
// чипы «N rec»), справа витрина лидера (больше всего рекордов).
// СТАБ — реализация по спеке dc.html секция 9d (sc-if is9dRecords).
//
// ПРАВИЛА КАРТОЧКИ — docs/competition-overview-cards.md (раздел New records). Кратко:
//   рекорды ищет CompetitionRecordsDetector по оси пол|бассейн|стиль|дистанция|категория|
//   возраст; результаты с пометкой качества (SuspectReason) рекорд НЕ бьют.

interface Props {
  overview: CompetitionOverview;
  onOpenTab(tab: 'swims' | 'clubs' | 'media' | 'records'): void;
  onOpenSwim?(swim: { result_id: number | null; style_name: string; distance: string }): void;
}

interface HolderGroup {
  holder_name: string;
  swimmer_id: number;
  club: string | null;
  age_group: string;
  records: OverviewRecord[];
}

export default function ModuleCardRecords({ overview, onOpenTab, onOpenSwim }: Props) {
  const holders = useMemo<HolderGroup[]>(() => {
    const map = new Map<string, HolderGroup>();
    for (const rec of overview.records) {
      const key = rec.holder_name;
      let group = map.get(key);
      if (!group) {
        group = {
          holder_name: rec.holder_name,
          swimmer_id: rec.swimmer_id,
          club: rec.club,
          age_group: rec.age_group,
          records: [],
        };
        map.set(key, group);
      }
      group.records.push(rec);
    }
    return Array.from(map.values()).sort((a, b) => b.records.length - a.records.length);
  }, [overview.records]);

  if (overview.records.length === 0 || holders.length === 0) {
    return (
      <section className="ov2-card" data-module="records">
        <div className="ov2-card__title">New records</div>
        <div className="text-[12px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          Нет новых рекордов
        </div>
      </section>
    );
  }

  const leader = holders[0];

  function openHolder(group: HolderGroup) {
    const first = group.records.find((r) => r.result_id != null);
    if (first) onOpenSwim?.(first);
  }

  return (
    <section className="ov2-card" data-module="records">
      <div className="grid grid-cols-1 lg:grid-cols-[minmax(0,1.7fr)_268px] gap-5 items-start">
        {/* Левая колонка: список рекордсменов */}
        <div className="min-w-0 flex flex-col">
          <div className="mb-2 flex items-baseline gap-2.5">
            <span className="ov2-card__title">New records</span>
            <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
              {overview.records.length} records · {holders.length} swimmers
            </span>
            <span className="flex-1" />
            <button type="button" className="ov2-card__link" onClick={() => onOpenTab('records')}>
              Records tab →
            </button>
          </div>

          {holders.map((group, i) => (
            <button
              key={group.holder_name + i}
              type="button"
              onClick={() => openHolder(group)}
              className="flex items-center gap-2.5 py-2 text-[13px] font-bold min-w-0 text-left"
              style={{
                color: 'var(--theme-mode-text)',
                borderTop: i > 0 ? '1px solid var(--theme-mode-border)' : undefined,
              }}
            >
              <span className="flex-none w-4" style={{ color: 'var(--theme-mode-text-muted)' }}>
                {i + 1}
              </span>
              <span className="min-w-0 flex flex-col">
                <span dir="auto" className="truncate">
                  {group.holder_name}
                </span>
                <span
                  dir="auto"
                  className="truncate text-[11.5px] font-semibold"
                  style={{ color: 'var(--theme-mode-text-secondary)' }}
                >
                  {group.club} · {group.age_group}
                </span>
              </span>
              <span className="flex-1" />
              <span className="ov2-card__chip">{group.records.length} rec</span>
            </button>
          ))}

          <button
            type="button"
            className="ov2-card__link mt-2 text-left"
            style={{ fontSize: '12.5px' }}
            onClick={() => onOpenTab('records')}
          >
            All {holders.length} record breakers →
          </button>
        </div>

        {/* Правая колонка: витрина лидера */}
        <button type="button" className="ov2-card__hero" onClick={() => openHolder(leader)}>
          <span
            className="rounded-full px-2.5 py-0.5 text-[10px] font-extrabold tracking-wide"
            style={{ background: 'var(--m-solid)', color: 'var(--m-on-solid)' }}
          >
            RECORD BREAKER
          </span>
          <span
            className="flex-none flex items-center justify-center rounded-full"
            style={{
              width: 96,
              height: 96,
              background: 'var(--m-surface)',
              border: '2px solid color-mix(in srgb, var(--m) 30%, transparent)',
              boxShadow: '0 6px 18px color-mix(in srgb, var(--m) 14%, transparent)',
            }}
          >
            <span className="ov2-logo" style={{ width: 76, height: 76, fontSize: 18 }}>
              {initials(leader.holder_name)}
            </span>
          </span>
          <span dir="auto" className="text-[17px] font-black leading-tight text-balance">
            {leader.holder_name}
          </span>
          <span dir="auto" className="text-[11.5px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
            {leader.club} · {leader.age_group}
          </span>
          <span className="flex items-baseline justify-center gap-1.5">
            <span className="ov2-card__value text-[30px] leading-none">{leader.records.length}</span>
            <span className="text-[11px] font-extrabold tracking-wide opacity-70" style={{ color: 'var(--m-solid)' }}>
              RECORDS
            </span>
          </span>
          <span className="w-full flex flex-col gap-1.5">
            {leader.records.slice(0, 3).map((rec, i) => (
              <span
                key={rec.result_id ?? i}
                role="button"
                tabIndex={0}
                onClick={(e) => {
                  e.stopPropagation();
                  onOpenSwim?.(rec);
                }}
                className="flex items-center gap-2 rounded-[9px] py-1.5 px-2.5"
                style={{ background: 'var(--m-surface)' }}
              >
                <span className="text-[11.5px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
                  {rec.distance} {rec.style_name}
                </span>
                <span className="flex-1" />
                <span className="text-[13px] font-black tabular-nums">{rec.time}</span>
              </span>
            ))}
          </span>
        </button>
      </div>
    </section>
  );
}

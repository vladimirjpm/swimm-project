import React from 'react';
import type { CompetitionOverview } from './types';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';

// Таб Records: рекорды, установленные на соревновании (overview.records; сам таб
// рендерится только когда список непуст). Строка: бейдж вида + событие+время +
// пловец·клуб + Day N (НАВ-контракт: диплинк на заплыв — следующий шаг).

export default function CompetitionRecords({
  overview,
  onOpenSwim,
}: {
  overview: CompetitionOverview | null;
  onOpenSwim?(swim: { result_id: number | null; style_name: string; distance: string }): void;
}) {
  const records = overview?.records ?? [];

  return (
    <div
      className="mt-4 rounded-[12px] p-4"
      style={{
        background: 'var(--theme-mode-surface)',
        color: 'var(--theme-mode-text)',
        boxShadow: 'var(--theme-mode-card-shadow)',
        border: '1px solid var(--theme-mode-card-border)',
      }}
    >
      <div className="mb-2 text-[14px] font-extrabold">New records at this competition</div>
      {records.length === 0 ? (
        <div className="py-6 text-center text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
          No records were set at this competition.
        </div>
      ) : (
        records.map((r, i) => (
          <div
            key={i}
            className={`flex flex-wrap items-center gap-x-3 gap-y-1 border-t py-2.5 first:border-t-0 ${
              onOpenSwim && r.result_id != null ? 'cursor-pointer hover:underline' : ''
            }`}
            style={{ borderColor: 'var(--theme-mode-border)' }}
            onClick={onOpenSwim && r.result_id != null ? () => onOpenSwim(r) : undefined}
          >
            <span
              className="rounded-full px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wide"
              style={{
                background: 'color-mix(in srgb, var(--theme-primary) 15%, transparent)',
                color: 'var(--theme-primary)',
              }}
            >
              {r.kind}
            </span>
            <span className="text-[13px] font-extrabold">
              {r.distance}m {r.style_name} —{' '}
              <UI_SwimTime
                time={r.time}
                quality={r.suspect_reason ? { kind: 'protocol', reason: r.suspect_reason } : null}
              />
            </span>
            <span className="text-[12.5px] font-semibold" dir="auto" style={{ color: 'var(--theme-mode-text-secondary)' }}>
              {r.holder_name}
              {r.club && <> · {r.club}</>}
            </span>
            {r.day_number != null && (
              <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
                Day {r.day_number}
              </span>
            )}
          </div>
        ))
      )}
    </div>
  );
}

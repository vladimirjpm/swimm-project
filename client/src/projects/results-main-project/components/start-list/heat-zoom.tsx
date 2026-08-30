import React from 'react';
import UI_SwimmerNameCell from '../../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';
import RefreshBar from './refresh-bar';
import { useStartListEvent } from './use-start-list';
import { eventLineLabel, formatApproxTime, mergeRelayLanes } from './start-list-helpers';

/**
 * Зум 2 — заплыв с дорожками (§4.2 плана): «с кем плывёт мой».
 *
 * Остаётся в табе и после редизайна (решение Влада 29.08.2026): карточка плана — вход, а не
 * замена, и строка D3 будет открывать именно этот экран.
 */

/**
 * Одна строка дорожки (личная или команда-эстафета).
 *
 * Своя вёрстка, а НЕ общий `SwimRow`: тот собран под результат (место, медаль, очки, дуга
 * уровня, крупное время справа), и в стартовом протоколе он врёт видом — показывает то,
 * чего ещё не произошло. Здесь вопрос другой: кто на какой дорожке и с каким посевом.
 */
function LaneRow({ row }: { row: ReturnType<typeof mergeRelayLanes>[number] }) {
  if ('isRelay' in row) {
    return (
      <div className="flex items-center gap-3 border-b py-2 last:border-b-0" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
        <div className="w-8 shrink-0 text-center text-sm font-black">{row.lane}</div>
        <UI_SwimmerNameCell
          firstName=""
          isRelay
          club={row.club_name}
          relaySwimmersList={row.members.map((m, i) => ({
            order: i + 1, first_name: m.swimmer_name, last_name: '', birth_year: m.birth_year ?? undefined,
          }))}
          className="min-w-0 flex-1"
        />
        <div className="shrink-0 text-right text-xs">
          {row.seed_time ? (
            <>
              <span className="mr-1 text-[9px] font-bold uppercase opacity-60">seed</span>
              <UI_SwimTime time={row.seed_time} />
            </>
          ) : 'NT'}
        </div>
      </div>
    );
  }
  return (
    <div className="flex items-center gap-3 border-b py-2 last:border-b-0" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
      <div className="w-8 shrink-0 text-center text-sm font-black">{row.lane}</div>
      <UI_SwimmerNameCell
        firstName={row.swimmer_name}
        club={`${row.birth_year ?? ''} · ${row.club_name}`.replace(/^ · /, '')}
        className="min-w-0 flex-1"
      />
      <div className="shrink-0 text-right text-xs">
        {row.seed_time ? (
          <>
            <span className="mr-1 text-[9px] font-bold uppercase opacity-60">seed</span>
            <UI_SwimTime time={row.seed_time} />
          </>
        ) : 'NT'}
      </div>
    </div>
  );
}

export default function HeatZoom({ orgCompId, orgDisciplineId, heat, onBack, onOpenSwimmer }: {
  orgCompId: number;
  orgDisciplineId: number;
  heat: number | null;
  onBack: () => void;
  onOpenSwimmer: (swimmerId: number) => void;
}) {
  const { data, loading, notFound, refresh } = useStartListEvent(orgCompId, orgDisciplineId);
  if (loading && !data) return <div className="py-6 text-center text-sm opacity-60">Loading…</div>;
  if (notFound || !data) return <div className="py-6 text-center text-sm opacity-60">No start list for this event yet.</div>;

  const heats = heat != null ? data.heats.filter((h) => h.heat === heat) : data.heats;

  return (
    <div>
      <button type="button" onClick={onBack} className="mb-2 text-xs font-bold opacity-70 hover:opacity-100">← Programme</button>
      <div className="mb-2 text-sm font-bold">{eventLineLabel(data.event)}</div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {heats.map((h) => (
        <div key={h.heat} className="mb-4 rounded-[12px] border p-3" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
          <div className="mb-2 text-xs font-extrabold">
            Heat {h.heat} · {formatApproxTime(h.start_at)}{h.round ? ` · ${h.round}` : ''}
          </div>
          {mergeRelayLanes(h.lanes).map((row) => (
            <div key={'isRelay' in row ? `r${row.lane}` : row.id} onClick={() => !('isRelay' in row) && onOpenSwimmer(row.swimmer_id)} className={'isRelay' in row ? '' : 'cursor-pointer'}>
              <LaneRow row={row} />
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimmerNameCell from '../../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';
import {
  useStartListEvent, useStartListProgramme, useStartListSwimmer,
} from './use-start-list';
import {
  arriveByTime, eventLineLabel, formatApproxTime, mergeRelayLanes, sortEvents, swimLabel,
} from './start-list-helpers';

/**
 * Таб «Start list» карточки соревнования (С7, docs/plans/start-list-plan.md §4) — три уровня
 * приближения на ОДНОМ табе: программа дня → заплыв с дорожками → карточка пловца.
 * `?swimmer=` открывает сразу зум 3, `?heat=` — зум 2, ничего — зум 1 (решение 2).
 *
 * Мобильный — основной вид (решение 6): никакой горизонтальной прокрутки, время и номер
 * заплыва крупно. Автообновления нет (решение 7) — вместо него метка «Updated HH:MM» + Refresh.
 */

type Zoom = 'programme' | 'heat' | 'swimmer';

interface Props {
  orgCompId: number;
}

function updatedLabel(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  return `Updated ${hh}:${mm}`;
}

function RefreshBar({ updatedAt, onRefresh }: { updatedAt: string | null; onRefresh: () => void }) {
  return (
    <div className="mb-3 flex items-center justify-between gap-2 text-[11px] font-semibold opacity-70">
      <span>{updatedLabel(updatedAt)}</span>
      <button
        type="button"
        onClick={onRefresh}
        className="rounded-full border px-2.5 py-1 text-[11px] font-bold"
        style={{ borderColor: 'var(--theme-mode-border-input)' }}
      >
        ↻ Refresh
      </button>
    </div>
  );
}

/** Одна строка дорожки (личная или команда-эстафета) — зум 2. */
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

/** Зум 2 — заплыв (§4.2). */
function HeatZoom({ orgCompId, orgDisciplineId, heat, onBack, onOpenSwimmer }: {
  orgCompId: number; orgDisciplineId: number; heat: number | null;
  onBack: () => void; onOpenSwimmer: (swimmerId: number) => void;
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

/** Зум 3 — карточка пловца (§4.3, главный экран для родителя). */
function SwimmerZoom({ orgCompId, swimmerId, onBack, onOpenHeat }: {
  orgCompId: number; swimmerId: number; onBack: () => void; onOpenHeat: (orgDisciplineId: number, heat: number) => void;
}) {
  const { data, loading, notFound, refresh } = useStartListSwimmer(orgCompId, swimmerId);
  if (loading && !data) return <div className="py-6 text-center text-sm opacity-60">Loading…</div>;
  if (notFound || !data) return <div className="py-6 text-center text-sm opacity-60">This swimmer has no entries here.</div>;

  const arrive = arriveByTime(data.first_start_at);

  return (
    <div>
      <button type="button" onClick={onBack} className="mb-2 text-xs font-bold opacity-70 hover:opacity-100">← Programme</button>
      <div className="mb-1 text-lg font-black">{data.swimmer_name} · {data.birth_year} · {data.club_name}</div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {data.first_start_at && (
        <div
          className="mb-4 rounded-[12px] p-3 text-sm font-bold"
          style={{ background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border-input)' }}
        >
          ⏱ First start {formatApproxTime(data.first_start_at)}{arrive ? ` — arrive by ${arrive}` : ''}
        </div>
      )}
      <div className="space-y-2">
        {data.swims.map((s) => (
          <div
            key={s.id}
            onClick={() => onOpenHeat(s.org_discipline_id, s.heat)}
            className="flex cursor-pointer items-center gap-3 rounded-[12px] border p-3"
            style={{ borderColor: 'var(--theme-mode-border-input)' }}
          >
            <UI_SwimmStyleIcon styleName={s.style_name} className="h-10 w-10 shrink-0" />
            <div className="min-w-0 flex-1">
              <div className="text-base font-black">{formatApproxTime(s.heat_start_at)}</div>
              <div className="text-xs opacity-70">
                {swimLabel(s.distance, s.style_name)} · heat {s.heat} · lane {s.lane}
              </div>
            </div>
            <div className="shrink-0 text-right text-xs">
              {s.seed_time ? (
                <>
                  <span className="mr-1 text-[9px] font-bold uppercase opacity-60">seed</span>
                  <UI_SwimTime time={s.seed_time} />
                </>
              ) : '—'}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Зум 1 — программа дня (§4.1), умолчание таба. */
function ProgrammeZoom({ orgCompId, onOpenEvent }: {
  orgCompId: number; onOpenEvent: (orgDisciplineId: number) => void;
}) {
  const { data, loading, notFound, refresh } = useStartListProgramme(orgCompId);
  const [search, setSearch] = useState('');
  const [dayIdx, setDayIdx] = useState(0);

  if (loading && !data) return <div className="py-6 text-center text-sm opacity-60">Loading…</div>;
  if (notFound || !data) return <div className="py-6 text-center text-sm opacity-60">No start list published for this competition yet.</div>;

  const day = data.days[dayIdx] ?? data.days[0];
  const events = day ? sortEvents(day.events) : [];
  const filtered = search.trim().length >= 2
    ? events.filter((e) => `${e.distance}${e.style_name}${e.event_category ?? ''}`.toLowerCase().includes(search.trim().toLowerCase()))
    : events;

  return (
    <div>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="text-sm font-bold">{data.comp_name} · {data.entries} entries</div>
      </div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {data.days.length > 1 && (
        <div className="mb-3 flex flex-wrap gap-1.5">
          {data.days.map((d, i) => (
            <button
              key={d.date}
              type="button"
              onClick={() => setDayIdx(i)}
              className={`rounded-full border px-3 py-1 text-xs font-bold ${i === dayIdx ? 'opacity-100' : 'opacity-60'}`}
              style={{ borderColor: 'var(--theme-mode-border-input)' }}
            >
              {new Date(d.date).toLocaleDateString(undefined, { weekday: 'short', day: '2-digit', month: 'short' })}
            </button>
          ))}
        </div>
      )}
      <input
        type="search"
        placeholder="Search event / category…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        className="mb-3 w-full rounded-[10px] border px-3 py-2 text-sm"
        style={{ borderColor: 'var(--theme-mode-border-input)', background: 'var(--theme-mode-surface)' }}
      />
      <div className="divide-y" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
        {filtered.map((e) => (
          <button
            key={e.org_discipline_id}
            type="button"
            onClick={() => onOpenEvent(e.org_discipline_id)}
            className="flex w-full items-center gap-3 py-2.5 text-left"
          >
            <div className="w-16 shrink-0 text-sm font-black">{formatApproxTime(e.start_at)}</div>
            <div className="w-10 shrink-0 text-center text-sm font-extrabold opacity-80">
              {e.event_number != null ? `#${e.event_number}` : '—'}
            </div>
            <div className="min-w-0 flex-1 text-sm">
              {swimLabel(e.distance, e.style_name)} {e.event_category ? `· ${e.event_category}` : ''}
            </div>
            <div className="shrink-0 text-xs opacity-70">{e.entries} entries</div>
            <div className="shrink-0 opacity-40">›</div>
          </button>
        ))}
        {filtered.length === 0 && <div className="py-4 text-center text-sm opacity-60">No events match.</div>}
      </div>
    </div>
  );
}

export default function StartListTab({ orgCompId }: Props) {
  const readQuery = () => {
    const q = new URLSearchParams(window.location.search);
    const swimmer = Number(q.get('swimmer'));
    const heat = Number(q.get('heat'));
    if (Number.isFinite(swimmer) && swimmer > 0) return { zoom: 'swimmer' as Zoom, swimmerId: swimmer, orgDisciplineId: null as number | null, heat: null as number | null };
    if (Number.isFinite(heat) && heat > 0) return { zoom: 'heat' as Zoom, swimmerId: null as number | null, orgDisciplineId: heat, heat: null as number | null };
    return { zoom: 'programme' as Zoom, swimmerId: null as number | null, orgDisciplineId: null as number | null, heat: null as number | null };
  };

  const [state, setState] = useState(readQuery);

  const setUrl = useCallback((params: { swimmer?: number | null; heat?: number | null }) => {
    const url = new URL(window.location.href);
    url.searchParams.delete('swimmer');
    url.searchParams.delete('heat');
    if (params.swimmer != null) url.searchParams.set('swimmer', String(params.swimmer));
    if (params.heat != null) url.searchParams.set('heat', String(params.heat));
    window.history.replaceState(null, '', url.toString());
  }, []);

  const openProgramme = useCallback(() => { setUrl({}); setState({ zoom: 'programme', swimmerId: null, orgDisciplineId: null, heat: null }); }, [setUrl]);
  const openEvent = useCallback((orgDisciplineId: number) => { setUrl({ heat: orgDisciplineId }); setState({ zoom: 'heat', swimmerId: null, orgDisciplineId, heat: null }); }, [setUrl]);
  const openSwimmer = useCallback((swimmerId: number) => { setUrl({ swimmer: swimmerId }); setState({ zoom: 'swimmer', swimmerId, orgDisciplineId: null, heat: null }); }, [setUrl]);

  // Разворот заплыва из карточки пловца — открываем зум 2 той дисциплины.
  const openHeatFromSwimmer = useCallback((orgDisciplineId: number, heat: number) => {
    setUrl({ heat: orgDisciplineId });
    setState({ zoom: 'heat', swimmerId: null, orgDisciplineId, heat });
  }, [setUrl]);

  return (
    <div>
      {state.zoom === 'programme' && <ProgrammeZoom orgCompId={orgCompId} onOpenEvent={openEvent} />}
      {state.zoom === 'heat' && state.orgDisciplineId != null && (
        <HeatZoom orgCompId={orgCompId} orgDisciplineId={state.orgDisciplineId} heat={state.heat} onBack={openProgramme} onOpenSwimmer={openSwimmer} />
      )}
      {state.zoom === 'swimmer' && state.swimmerId != null && (
        <SwimmerZoom orgCompId={orgCompId} swimmerId={state.swimmerId} onBack={openProgramme} onOpenHeat={openHeatFromSwimmer} />
      )}
    </div>
  );
}

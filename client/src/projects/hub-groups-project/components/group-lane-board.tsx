import React from 'react';
import type { LanePlan, LanePlanLane, LanePlanSwimmer } from '../types';
import LaneCircle from './group-lane-circle';
import { levelColor } from './level-color';
import { workoutOneLine } from './trainings/next/next-format';

/**
 * Доска плана дорожек — вид участника (Т7, хендофф Trainings Tab, 1e; docs/plans/lane-plans-plan.md).
 * Сверху — «Your lane» на каждого «своего» пловца (`my_swimmers`: сам зритель и его семья) —
 * крупно: номер и уровень, задание, товарищи по дорожке; перенесённому тренером — золотая заметка
 * «Coach moved you from lane 3 to 2» (метку не гасим, `PUT …/lane-plans/seen` вызывает Т8). Остальные
 * дорожки — компактно одним бассейном. Это только подсветка, прав она не даёт. У тренера доски нет
 * — он сразу в редакторе (`group-lane-editor.tsx`).
 */

export function SwimmerName({ swimmer }: { swimmer: Pick<LanePlanSwimmer, 'name' | 'name_en'> }) {
  // Имя на иврите внутри английского интерфейса — в <bdi>, иначе рвётся порядок символов.
  return <bdi>{swimmer.name || swimmer.name_en}</bdi>;
}

export function LevelChip({ level }: { level: NonNullable<LanePlanLane['level']> }) {
  return (
    <span className="inline-flex min-w-0 items-center gap-1.5 rounded-full border border-[var(--t-border)] px-2 py-[2px] text-[11.5px] font-extrabold text-[var(--t-text-2)]">
      <span className="inline-block h-[9px] w-[9px] shrink-0 rounded-full" style={{ background: levelColor(level) }} />
      <span className="truncate">{level.name}</span>
    </span>
  );
}

/** Подпись дорожки: «2 · medium» (уровень в нижнем регистре, если он есть) либо «2». */
function laneLabel(lane: LanePlanLane): string {
  return lane.level ? `${lane.lane_no} · ${lane.level.name.toLowerCase()}` : String(lane.lane_no);
}

const laneColor = (lane: LanePlanLane | null) => (lane?.level ? levelColor(lane.level) : null);

/** Где стоит пловец: дорожка и место в ней, либо Unassigned. */
function findPlace(plan: LanePlan, swimmerId: number) {
  for (const lane of plan.lanes) {
    const index = lane.swimmers.findIndex((s) => s.swimmer_id === swimmerId);
    if (index >= 0) return { lane, index, swimmer: lane.swimmers[index] };
  }
  const swimmer = plan.unassigned.find((s) => s.swimmer_id === swimmerId);
  return swimmer ? { lane: null, index: -1, swimmer } : null;
}

/** Золотая заметка о переносе — над карточкой «Your lane». */
function MovedNote({ kind, swimmer, from, lane }: {
  kind: 'me' | 'family';
  swimmer: LanePlanSwimmer;
  from: number;
  lane: LanePlanLane | null;
}) {
  return (
    <div
      role="status"
      className="flex items-center gap-2 rounded-full border text-[11px] font-bold"
      style={{
        padding: '6px 12px', borderColor: 'var(--deep-gold-border)', background: 'var(--deep-gold-chip)', color: 'var(--deep-gold)',
      }}
    >
      <span aria-hidden="true" className="inline-block flex-none rounded-full" style={{ width: 7, height: 7, background: 'var(--deep-gold-bar)' }} />
      <span className="min-w-0">
        Coach moved {kind === 'me' ? 'you' : <SwimmerName swimmer={swimmer} />} from lane {from} to {lane ? lane.lane_no : 'unassigned'}
      </span>
    </div>
  );
}

function YourLaneCard({
  plan, swimmerId, kind, mineMe,
}: {
  plan: LanePlan;
  swimmerId: number;
  kind: 'me' | 'family';
  /** Пловцы-«я» зрителя: у них в списке дорожки стоит «you». */
  mineMe: Set<number>;
}) {
  const place = findPlace(plan, swimmerId);
  if (!place) return null;
  const { lane, swimmer } = place;
  const workout = lane?.workout?.trim() ? lane.workout : null;

  return (
    <section
      className="flex min-w-0 flex-col gap-2 rounded-[16px] border p-3.5"
      style={{ borderColor: 'var(--deep-accent-border)', background: 'var(--deep-pool-water)' }}
      aria-label={kind === 'me' ? 'Your lane' : `Lane for ${swimmer.name || swimmer.name_en}`}
    >
      <div className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--deep-pool-label)' }}>
        {kind === 'me' ? 'Your lane' : <>Lane for <SwimmerName swimmer={swimmer} /></>}
      </div>

      {lane ? (
        <>
          <div className="text-[28px] leading-none" style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}>
            {laneLabel(lane)}
          </div>
          {workout && (
            <div dir="auto" className="hp-mono whitespace-pre-wrap text-[13px] font-extrabold leading-[1.6]" style={{ color: 'var(--deep-text)' }}>
              {workout}
            </div>
          )}
          <ul className="m-0 mt-1 flex list-none flex-col gap-1 p-0">
            {lane.swimmers.map((s) => (
              <li key={s.swimmer_id} className="flex min-w-0 items-center gap-2">
                <LaneCircle name={s.name || s.name_en} color={laneColor(lane)} size={24} />
                <span className="min-w-0 truncate text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                  <SwimmerName swimmer={s} />
                </span>
                {mineMe.has(s.swimmer_id) && (
                  <span className="flex-none text-[10.5px]" style={{ color: 'var(--deep-accent)' }}>you</span>
                )}
              </li>
            ))}
          </ul>
        </>
      ) : (
        <p className="m-0 text-[13px] font-bold" style={{ color: 'var(--t-text-3)' }}>
          On the list for today — the coach hasn’t picked a lane yet.
        </p>
      )}
    </section>
  );
}

/** Остальные дорожки — один бассейн: строка на дорожку, пунктир каната между ними. */
function OtherLanesPool({ lanes }: { lanes: LanePlanLane[] }) {
  return (
    <div
      className="overflow-hidden rounded-[12px] border"
      style={{ borderColor: 'var(--deep-pool-border)', background: 'var(--deep-pool-water)' }}
      role="list"
      aria-label="Lanes"
    >
      {lanes.map((lane, i) => {
        const workout = workoutOneLine(lane.workout);
        return (
          <div
            key={lane.lane_no}
            role="listitem"
            className="flex min-h-[48px] items-center gap-2.5"
            style={{ padding: '6px 10px', borderTop: i === 0 ? undefined : '1px dashed var(--deep-pool-rope)' }}
          >
            <div className="w-[96px] flex-none">
              <div className="hp-mono truncate text-[10.5px] font-extrabold" style={{ color: 'var(--deep-pool-label)' }}>
                {laneLabel(lane)}
              </div>
              {workout && (
                <div dir="auto" className="hp-mono truncate text-[10px] font-bold" style={{ color: 'var(--t-text-3)' }} title={workout}>
                  {workout}
                </div>
              )}
            </div>
            <div className="flex min-w-0 flex-1 flex-wrap items-center gap-1.5">
              {lane.swimmers.map((s) => (
                <span key={s.swimmer_id} title={s.name || s.name_en}>
                  <LaneCircle name={s.name || s.name_en} color={laneColor(lane)} size={26} />
                </span>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

function GroupLaneBoard({ plan }: { plan: LanePlan }) {
  const mySwimmers = plan.my_swimmers ?? [];
  const mine = new Set(mySwimmers.map((m) => m.swimmer_id));
  const mineMe = new Set(mySwimmers.filter((m) => m.kind === 'me').map((m) => m.swimmer_id));
  // Дорожки моих пловцов уже выше крупно — в общий бассейн не входят.
  const myLaneNos = new Set(mySwimmers.map((m) => findPlace(plan, m.swimmer_id)?.lane?.lane_no).filter((n): n is number => n != null));
  const otherLanes = plan.lanes.filter((l) => !myLaneNos.has(l.lane_no));

  return (
    <div className="flex min-w-0 flex-col gap-3">
      {mySwimmers.length > 0 && (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {mySwimmers.map((m) => {
            const place = findPlace(plan, m.swimmer_id);
            return (
              <div key={m.swimmer_id} className="flex min-w-0 flex-col gap-2">
                {place && m.moved_from_lane != null && (
                  <MovedNote kind={m.kind} swimmer={place.swimmer} from={m.moved_from_lane} lane={place.lane} />
                )}
                <YourLaneCard plan={plan} swimmerId={m.swimmer_id} kind={m.kind} mineMe={mineMe} />
              </div>
            );
          })}
        </div>
      )}
      {plan.note && (
        <p
          className="m-0 whitespace-pre-wrap rounded-[10px] px-3 py-2 text-[13px] font-semibold"
          style={{ background: 'var(--deep-accent-soft)', color: 'var(--deep-text)' }}
        >
          {plan.note}
        </p>
      )}
      {otherLanes.length > 0 && <OtherLanesPool lanes={otherLanes} />}
      {plan.unassigned.length > 0 && (
        <div className="rounded-[12px] p-3" style={{ border: '1px dashed var(--deep-card-border)' }}>
          <div className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
            Not placed in a lane yet
          </div>
          <div className="mt-1.5 flex flex-wrap gap-x-3 gap-y-1 text-[13px] font-bold">
            {plan.unassigned.map((s) => (
              <span key={s.swimmer_id} style={{ color: mine.has(s.swimmer_id) ? 'var(--deep-accent)' : 'var(--deep-text)' }}>
                <SwimmerName swimmer={s} />
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export default GroupLaneBoard;

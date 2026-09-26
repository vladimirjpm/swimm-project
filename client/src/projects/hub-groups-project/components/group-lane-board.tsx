import React from 'react';
import type { LanePlan, LanePlanLane, LanePlanSwimmer } from '../types';
import { levelColor } from './level-color';

/**
 * Доска плана дорожек — только просмотр (docs/plans/lane-plans-plan.md). Одна на тренера
 * (вид между правками) и участника: дорожка = карточка с уровнем, заданием и людьми, первый
 * в дорожке ведёт. Полоска слева — цвет уровня, тот же, что в карточке Levels.
 *
 * L4: над доской — карточка «Your lane» на каждого «своего» пловца (`my_swimmers`: сам зритель
 * и его семья), в дорожках их имена подсвечены. Это только подсветка, прав она не даёт.
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

const ordinal = (n: number) => {
  const tail = n % 100 >= 11 && n % 100 <= 13 ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' } as Record<number, string>)[n % 10] ?? 'th';
  return `${n}${tail}`;
};

/** Где стоит пловец: дорожка и место в ней, либо Unassigned. */
function findPlace(plan: LanePlan, swimmerId: number) {
  for (const lane of plan.lanes) {
    const index = lane.swimmers.findIndex((s) => s.swimmer_id === swimmerId);
    if (index >= 0) return { lane, index, swimmer: lane.swimmers[index] };
  }
  const swimmer = plan.unassigned.find((s) => s.swimmer_id === swimmerId);
  return swimmer ? { lane: null, index: -1, swimmer } : null;
}

function YourLaneCard({ plan, swimmerId, kind }: { plan: LanePlan; swimmerId: number; kind: 'me' | 'family' }) {
  const place = findPlace(plan, swimmerId);
  if (!place) return null;
  const { lane, index, swimmer } = place;
  const color = lane?.level ? levelColor(lane.level) : 'var(--t-accent)';

  return (
    <section
      className="flex min-w-0 flex-col gap-2 rounded-[14px] border-2 border-[var(--t-accent)] bg-[var(--t-accent-soft)] p-3.5"
      aria-label={kind === 'me' ? 'Your lane' : `Lane for ${swimmer.name || swimmer.name_en}`}
    >
      <div className="flex items-baseline gap-2">
        <span className="text-[11px] font-extrabold uppercase tracking-[0.06em] text-[var(--t-accent)]">
          {kind === 'me' ? 'Your lane' : 'Lane for'}
        </span>
        <span className="min-w-0 truncate text-[13px] font-black text-[var(--t-text)]">
          <SwimmerName swimmer={swimmer} />
        </span>
      </div>

      {lane ? (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <span className="flex items-baseline gap-1.5">
              <span className="text-[12px] font-extrabold text-[var(--t-text-2)]">Lane</span>
              <span className="hp-mono text-[34px] font-black leading-none" style={{ color }}>{lane.lane_no}</span>
            </span>
            {lane.level && <LevelChip level={lane.level} />}
            <span className="ml-auto text-[12px] font-bold text-[var(--t-text-2)]">
              {index === 0
                ? (lane.swimmers.length > 1 ? `leads · ${lane.swimmers.length} in the lane` : 'alone in the lane')
                : `${ordinal(index + 1)} of ${lane.swimmers.length}`}
            </span>
          </div>
          {lane.workout
            ? <p className="m-0 whitespace-pre-wrap text-[14px] font-bold leading-[1.45] text-[var(--t-text)]">{lane.workout}</p>
            : <p className="m-0 text-[12px] italic text-[var(--t-text-3)]">No workout yet</p>}
        </>
      ) : (
        <p className="m-0 text-[13px] font-bold text-[var(--t-text-2)]">
          On the list for today — the coach hasn’t picked a lane yet.
        </p>
      )}
    </section>
  );
}

function LaneCard({ lane, mine }: { lane: LanePlanLane; mine: Set<number> }) {
  return (
    <section
      className="flex min-w-0 flex-col rounded-[12px] border border-[var(--t-border)] bg-[var(--t-nested)] p-3"
      style={{ borderLeft: `4px solid ${lane.level ? levelColor(lane.level) : 'var(--t-border)'}` }}
      aria-label={`Lane ${lane.lane_no}`}
    >
      <header className="flex items-center gap-2">
        <span className="hp-mono text-[20px] font-black leading-none text-[var(--t-text)]">{lane.lane_no}</span>
        <span className="text-[11px] font-extrabold uppercase tracking-[0.06em] text-[var(--t-text-3)]">Lane</span>
        <span className="ml-auto min-w-0">{lane.level && <LevelChip level={lane.level} />}</span>
      </header>

      {lane.workout
        ? <p className="m-0 mt-2 whitespace-pre-wrap text-[13px] font-semibold leading-[1.45] text-[var(--t-text)]">{lane.workout}</p>
        : <p className="m-0 mt-2 text-[12px] italic text-[var(--t-text-3)]">No workout yet</p>}

      {lane.swimmers.length > 0 ? (
        <ol className="m-0 mt-2.5 flex list-none flex-col gap-1 border-t border-[var(--t-border-2)] p-0 pt-2">
          {lane.swimmers.map((s, i) => {
            const isMine = mine.has(s.swimmer_id);
            return (
              <li
                key={s.swimmer_id}
                className={`flex items-baseline gap-2 rounded-[6px] text-[13px] ${isMine ? '-mx-1.5 bg-[var(--t-accent-soft)] px-1.5' : ''}`}
                aria-current={isMine ? 'true' : undefined}
              >
                <span className="hp-mono w-[14px] shrink-0 text-right text-[11px] text-[var(--t-text-3)]">{i + 1}</span>
                <span className={`min-w-0 truncate font-bold ${
                  s.left_group ? 'text-[var(--t-text-3)]' : isMine ? 'text-[var(--t-accent)]' : 'text-[var(--t-text)]'
                }`}>
                  <SwimmerName swimmer={s} />
                </span>
                {s.left_group && <span className="text-[10.5px] text-[var(--t-text-3)]">left the group</span>}
              </li>
            );
          })}
        </ol>
      ) : (
        <p className="m-0 mt-2.5 border-t border-[var(--t-border-2)] pt-2 text-[12px] text-[var(--t-text-3)]">Empty lane</p>
      )}
    </section>
  );
}

function GroupLaneBoard({ plan }: { plan: LanePlan }) {
  const mySwimmers = plan.my_swimmers ?? [];
  const mine = new Set(mySwimmers.map((m) => m.swimmer_id));

  return (
    <div>
      {mySwimmers.length > 0 && (
        <div className="mb-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
          {mySwimmers.map((m) => (
            <YourLaneCard key={m.swimmer_id} plan={plan} swimmerId={m.swimmer_id} kind={m.kind} />
          ))}
        </div>
      )}
      {plan.note && (
        <p className="m-0 mb-3 whitespace-pre-wrap rounded-[10px] bg-[var(--t-accent-soft)] px-3 py-2 text-[13px] font-semibold text-[var(--t-text)]">
          {plan.note}
        </p>
      )}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 min-[1200px]:grid-cols-3">
        {plan.lanes.map((lane) => <LaneCard key={lane.lane_no} lane={lane} mine={mine} />)}
      </div>
      {plan.unassigned.length > 0 && (
        <div className="mt-3 rounded-[12px] border border-dashed border-[var(--t-border)] p-3">
          <div className="text-[11px] font-extrabold uppercase tracking-[0.06em] text-[var(--t-text-3)]">
            Not placed in a lane yet
          </div>
          <div className="mt-1.5 flex flex-wrap gap-x-3 gap-y-1 text-[13px] font-bold">
            {plan.unassigned.map((s) => (
              <span key={s.swimmer_id} className={mine.has(s.swimmer_id) ? 'text-[var(--t-accent)]' : 'text-[var(--t-text)]'}>
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

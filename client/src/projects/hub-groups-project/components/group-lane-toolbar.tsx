import React from 'react';

/**
 * Тулбар тренера над дорожками (Т7, Р6 п.3): «3 lanes − +», «Max per lane» (перелив по подуровням), «Distribute», «Auto lanes»,
 * «Copy from Sun 27», «Delete» (только у существующего плана). Кнопки 40px на телефоне / 36px
 * на десктопе, перенос строк разрешён (горизонтальной прокрутки ряда нет). На десктопе редактор
 * выносит тулбар в шапку вида (портал), на телефоне он стоит над дорожками.
 */

/** «Sun 27» — день недели и число для кнопки копии и подтверждения. */
/** Лимит людей на дорожку — до 20 (сервер: LanePlanRules.MaxPerLane). */
const MAX_PER_LANE = 20;

export function shortDay(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const weekday = new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-US', { weekday: 'short', timeZone: 'UTC' });
  return `${weekday} ${d}`;
}

function LaneToolbar({
  desktop, laneCount, minLanes, maxLanes, onLaneCount, maxPerLane, onMaxPerLane, working, canRun,
  onDistribute, onAutoLanes, copyFrom, onCopy, canDelete, onDelete,
}: {
  desktop: boolean;
  laneCount: number;
  minLanes: number;
  maxLanes: number;
  onLaneCount: (delta: number) => void;
  /** Лимит людей на дорожку (перелив по подуровням); null — без лимита. */
  maxPerLane: number | null;
  onMaxPerLane: (value: number | null) => void;
  /** Идёт Distribute / Auto lanes. */
  working: boolean;
  /** Есть кого раскладывать. */
  canRun: boolean;
  onDistribute: () => void;
  onAutoLanes: () => void;
  /** Дата плана-источника копии; null — копировать не с чего. */
  copyFrom: string | null;
  onCopy: () => void;
  canDelete: boolean;
  onDelete: () => void;
}) {
  const h = desktop ? 36 : 40;
  const step = desktop ? 26 : 30;
  const btn = 'flex-none cursor-pointer rounded-[10px] border bg-transparent px-3 text-[12px] font-extrabold disabled:cursor-not-allowed disabled:opacity-40';
  const violet = { height: h, borderColor: 'var(--deep-ow-border)', color: 'var(--deep-ow)' };
  const stepBtn = 'flex-none cursor-pointer rounded-[8px] border-0 text-[14px] font-black leading-none disabled:cursor-not-allowed disabled:opacity-40';
  const stepStyle = { width: step, height: step, background: 'var(--deep-seg-track)', color: 'var(--deep-text)' };

  return (
    <div className="flex flex-wrap items-center gap-2">
      <div
        className="flex flex-none items-center gap-2 rounded-[10px] border text-[12px] font-extrabold"
        style={{ height: h, padding: '0 5px 0 12px', borderColor: 'var(--deep-card-border)', color: 'var(--deep-text)' }}
        role="group"
        aria-label="Number of lanes"
      >
        <span className="hp-mono">{laneCount} {laneCount === 1 ? 'lane' : 'lanes'}</span>
        <button type="button" className={stepBtn} style={stepStyle} onClick={() => onLaneCount(-1)} disabled={laneCount <= minLanes} aria-label="Fewer lanes">−</button>
        <button type="button" className={stepBtn} style={stepStyle} onClick={() => onLaneCount(1)} disabled={laneCount >= maxLanes} aria-label="More lanes">+</button>
      </div>
      <label
        className="flex flex-none items-center gap-1.5 rounded-[10px] border text-[12px] font-extrabold"
        style={{ height: h, padding: '0 6px 0 12px', borderColor: 'var(--deep-card-border)', color: 'var(--deep-text)' }}
        title="When a lane is full, the bottom sub-level of its level moves to the next level’s lane"
      >
        <span>Max per lane</span>
        <select
          value={maxPerLane ?? ''}
          onChange={(e) => onMaxPerLane(e.target.value === '' ? null : Number(e.target.value))}
          aria-label="Max swimmers per lane"
          className="hp-mono cursor-pointer rounded-[8px] border-0 px-1.5 text-[12px] font-extrabold"
          style={{ height: step, background: 'var(--deep-seg-track)', color: 'var(--deep-text)' }}
        >
          <option value="">No limit</option>
          {Array.from({ length: MAX_PER_LANE }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{n}</option>)}
        </select>
      </label>
      <button type="button" className={btn} style={violet} onClick={onDistribute} disabled={working || !canRun}>
        {working ? 'Working…' : 'Distribute'}
      </button>
      <button
        type="button"
        className={btn}
        style={violet}
        onClick={onAutoLanes}
        disabled={working || !canRun}
        title="Split the lanes between levels by how many are coming, then place everyone"
      >
        Auto lanes
      </button>
      {copyFrom && (
        <button
          type="button"
          className={btn}
          style={{ height: h, borderColor: 'var(--deep-card-border)', color: 'var(--t-text-3)' }}
          onClick={onCopy}
          disabled={working}
        >
          Copy from {shortDay(copyFrom)}
        </button>
      )}
      {canDelete && (
        <button
          type="button"
          className="flex-none cursor-pointer border-0 bg-transparent px-2 text-[12px] font-extrabold disabled:cursor-not-allowed disabled:opacity-40"
          style={{ height: h, color: 'var(--deep-danger)' }}
          onClick={onDelete}
          disabled={working}
        >
          Delete
        </button>
      )}
    </div>
  );
}

export default LaneToolbar;

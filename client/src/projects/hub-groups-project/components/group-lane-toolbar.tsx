import React from 'react';

/**
 * Тулбар тренера над дорожками (Т7, Р6 п.3): «3 lanes − +», «Distribute», «Auto lanes»,
 * «Copy from Sun 27», «Delete» (только у существующего плана). Кнопки 40px на телефоне / 36px
 * на десктопе, перенос строк разрешён (горизонтальной прокрутки ряда нет). На десктопе редактор
 * выносит тулбар в шапку вида (портал), на телефоне он стоит над дорожками.
 */

/** «Sun 27» — день недели и число для кнопки копии и подтверждения. */
export function shortDay(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const weekday = new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-US', { weekday: 'short', timeZone: 'UTC' });
  return `${weekday} ${d}`;
}

function LaneToolbar({
  desktop, laneCount, minLanes, maxLanes, onLaneCount, working, canRun,
  onDistribute, onAutoLanes, copyFrom, onCopy, canDelete, onDelete,
}: {
  desktop: boolean;
  laneCount: number;
  minLanes: number;
  maxLanes: number;
  onLaneCount: (delta: number) => void;
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

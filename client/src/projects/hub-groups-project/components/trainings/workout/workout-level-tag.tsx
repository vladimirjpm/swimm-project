import React from 'react';
import type { HubGroupLevel } from '../../../types';
import { levelColor } from '../../level-color';
import { levelAbbr } from './workout-format';

/**
 * Подпись уровня в workout (CF6; решение владельца 15.11.2026): кружок ранга цветом уровня и
 * сокращение — «① Adv»; полное название в `title`. Цвет уровня — данные тренера (`levelColor`),
 * цифра — `--deep-accent-ink`, как у кружка `LaneCircle`.
 */

export type WorkoutLevel = Pick<HubGroupLevel, 'id' | 'rank' | 'name' | 'color'>;

export function LevelRank({ level, size = 18 }: { level: WorkoutLevel; size?: number }) {
  return (
    <span
      aria-hidden="true"
      className="inline-flex flex-none items-center justify-center rounded-full font-black leading-none"
      style={{
        width: size, height: size, fontSize: Math.round(size * 0.58),
        background: levelColor(level), color: 'var(--deep-accent-ink)',
      }}
    >
      {level.rank}
    </span>
  );
}

export function LevelTag({ level, full = false, size }: { level: WorkoutLevel; full?: boolean; size?: number }) {
  return (
    <span className="inline-flex min-w-0 items-center gap-1" title={level.name}>
      <LevelRank level={level} size={size} />
      <span dir="auto" className="min-w-0 truncate text-[12px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
        {full ? level.name : levelAbbr(level.name)}
      </span>
    </span>
  );
}

/**
 * Ячейка колонки уровня: бледный тон цвета уровня тем же способом, что плашка пловца
 * (`group-lane-chip.tsx`), рамка — сам цвет. `off` — уровень этот сет не плывёт.
 */
export function levelCellStyle(level: WorkoutLevel, off = false, dashed = false): React.CSSProperties {
  if (off) {
    return { background: 'var(--deep-seg-track)', border: '1px solid transparent', color: 'var(--deep-text-mute)' };
  }
  const color = levelColor(level);
  return {
    background: `color-mix(in srgb, ${color} 22%, var(--deep-card-bg))`,
    border: `1px ${dashed ? 'dashed' : 'solid'} ${dashed ? 'var(--deep-gold)' : color}`,
    color: 'var(--deep-text)',
  };
}

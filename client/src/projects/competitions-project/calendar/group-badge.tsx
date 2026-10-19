import React from 'react';
import { IconSnowflake, IconSun, IconTrophy } from './calendar-icons';
import type { CalendarGroup } from './calendar-types';

/*
 * Бейдж группы чемпионата (K/Y/J/A/M): три полосы сверху вниз — верх (иконка сезона + кубок),
 * середина (буква на акценте), низ (возраст). Верх и низ лежат на `--t-plate` — светлой подложке,
 * одинаковой в обеих темах, поэтому бейдж читается и на тёмной. Размеры — по макету календаря.
 * Какие чемпионаты получают бейдж, а какие текст — `champMark` в calendar-model.ts.
 */

interface GroupBadgeProps {
  group: CalendarGroup;
  /**
   * full — куб соревнования (десктоп); full-sm — то же на телефоне (34px); mini — полоса месяца;
   * cell — плашка в ячейке дня (24px); dot — метка дня на телефоне (14×18: только буква,
   * без иконок и возраста).
   */
  size: 'full' | 'full-sm' | 'mini' | 'cell' | 'dot';
  className?: string;
}

const DIMS = {
  full: { w: 40, radius: 6, top: 11, mid: 19, bot: 11, icon: 9, padX: 4, letter: 14, age: 8, ageTrack: '-0.02em' },
  mini: { w: 26, radius: 4, top: 8, mid: 13, bot: 8, icon: 6, padX: 2, letter: 10, age: 6.5, ageTrack: undefined },
  cell: { w: 24, radius: 4, top: 8, mid: 13, bot: 8, icon: 6, padX: 2, letter: 10, age: 6.5, ageTrack: undefined },
  'full-sm': { w: 34, radius: 5, top: 9, mid: 15, bot: 9, icon: 7, padX: 3, letter: 11, age: 7, ageTrack: '-0.02em' },
  dot: { w: 14, radius: 3, top: 4, mid: 10, bot: 4, icon: 0, padX: 0, letter: 9, age: 0, ageTrack: undefined },
} as const;

/** Подпись для скринридера: «Kids 8–11 · winter championship». */
export function groupBadgeLabel(group: CalendarGroup): string {
  const who = group.ages ? `${group.label} ${group.ages}` : group.label;
  const season = group.season === 'winter' || group.season === 'summer' ? ` · ${group.season}` : '';
  return `${who}${season} championship`;
}

function GroupBadge({ group, size, className }: GroupBadgeProps) {
  const d = DIMS[size];
  const bare = size === 'dot';
  const SeasonIcon = group.season === 'winter' ? IconSnowflake : group.season === 'summer' ? IconSun : null;

  return (
    <span
      role="img"
      aria-label={groupBadgeLabel(group)}
      className={`flex shrink-0 flex-col overflow-hidden ${className ?? ''}`}
      style={{ width: d.w, borderRadius: d.radius }}
    >
      <span
        className="flex items-center justify-between bg-[var(--t-plate)] text-[var(--t-badge-ink)]"
        style={{ height: d.top, padding: `0 ${d.padX}px` }}
      >
        {!bare && (SeasonIcon ? <SeasonIcon size={d.icon} /> : <span />)}
        {!bare && <IconTrophy size={d.icon} />}
      </span>
      <span
        className="flex items-center justify-center bg-[var(--t-accent)] font-bold leading-none text-[var(--t-accent-ink)]"
        style={{ height: d.mid, fontSize: d.letter }}
      >
        {group.letter}
      </span>
      <span
        className="flex items-center justify-center whitespace-nowrap bg-[var(--t-plate)] font-bold leading-none text-[var(--t-badge-ink)]"
        style={{ height: d.bot, fontSize: d.age, letterSpacing: d.ageTrack }}
      >
        {!bare && group.ages}
      </span>
    </span>
  );
}

export default GroupBadge;

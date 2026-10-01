import React from 'react';
import type { NextTraining } from '../../../types';
import { formatSheetDate } from '../sheets/link-helpers';
import { timeRangeLabel, untilLabel } from '../next/next-format';
import { useTrainingsTab } from '../trainings-context';

/**
 * Навигация по трём ближайшим занятиям ‹ › вида тренера (Т6, хендофф Trainings Tab, 2a / 3b).
 * Состояние (`rollIndex`) живёт в контексте таба — выбранное занятие читают и бассейн, и
 * перекличка, и шторка человека. Отсчёт — `untilLabel` (`next/next-format.ts`), не дублируем.
 */

function ArrowButton({
  dir, disabled, onClick, size,
}: {
  dir: 'prev' | 'next';
  disabled: boolean;
  onClick: () => void;
  size: number;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      aria-label={dir === 'prev' ? 'Previous training' : 'Next training'}
      className="flex flex-none cursor-pointer items-center justify-center border bg-transparent font-extrabold disabled:cursor-default disabled:opacity-35"
      style={{ width: size, height: size, borderRadius: 10, borderColor: 'var(--deep-card-border)', color: 'var(--t-text-3)' }}
    >
      {dir === 'prev' ? '‹' : '›'}
    </button>
  );
}

function SessionNav({ sessions }: { sessions: NextTraining[] }) {
  const { isDesktop, rollIndex, setRollIndex } = useTrainingsTab();
  const index = Math.min(rollIndex, Math.max(0, sessions.length - 1));
  const current = sessions[index];
  if (!current) return null;

  const first = index === 0;
  const until = untilLabel(current.date, current.start, current.end);
  const times = timeRangeLabel(current.start, current.end);
  const prev = () => setRollIndex(Math.max(0, index - 1));
  const next = () => setRollIndex(Math.min(sessions.length - 1, index + 1));
  const atStart = index <= 0;
  const atEnd = index >= sessions.length - 1;

  if (!isDesktop) {
    const sub = first ? (until ? `NEXT · ${until}` : 'NEXT') : until;
    return (
      <div className="flex items-center gap-2">
        <ArrowButton dir="prev" disabled={atStart} onClick={prev} size={40} />
        <div className="min-w-0 flex-1 text-center">
          <div className="whitespace-nowrap text-[15px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
            {formatSheetDate(current.date)} · <span className="hp-mono">{times}</span>
          </div>
          {sub && <div className="text-[10.5px] font-extrabold" style={{ color: 'var(--deep-live)' }}>{sub}</div>}
        </div>
        <ArrowButton dir="next" disabled={atEnd} onClick={next} size={40} />
      </div>
    );
  }

  // Десктоп: слева кикер с точкой и крупная дата; стрелки по краям.
  const lower = until.toLowerCase();
  const kicker = first
    ? (lower ? `Next · ${lower}` : 'Next')
    : (lower ? lower.charAt(0).toUpperCase() + lower.slice(1) : '');
  return (
    <div className="flex items-center gap-2.5">
      <ArrowButton dir="prev" disabled={atStart} onClick={prev} size={36} />
      <div className="min-w-0 flex-1">
        {kicker && (
          <div
            className="flex items-center gap-1.5 text-[10px] font-extrabold uppercase tracking-[.06em]"
            style={{ color: 'var(--deep-live)' }}
          >
            <span aria-hidden="true" className="inline-block flex-none rounded-full" style={{ width: 7, height: 7, background: 'var(--deep-live)' }} />
            {kicker}
          </div>
        )}
        <div className="whitespace-nowrap text-[20px]" style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}>
          {formatSheetDate(current.date)} · <span className="hp-mono text-[18px] font-extrabold">{times}</span>
        </div>
      </div>
      <ArrowButton dir="next" disabled={atEnd} onClick={next} size={36} />
    </div>
  );
}

export default SessionNav;

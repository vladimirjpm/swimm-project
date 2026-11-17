import React from 'react';
import type { NextTraining } from '../../../types';
import { timeRangeLabel } from '../next/next-format';
import { CancelledPill, CancelReason, SessionTag } from '../next/session-tag';
import { formatSheetDate } from '../sheets/link-helpers';

/**
 * Шапка рабочего стола ПРОШЕДШЕГО занятия (CF9) — вместо `SessionNav`: стрелки ‹ › ходят по
 * ближайшим, а прошедшее открывают из списка «Past». Отменить / вернуть прошедшее нельзя
 * (сервер держит это в окне правки) — кнопок нет, только пометка.
 */
function PastSessionHead({ session, desktop }: { session: NextTraining; desktop: boolean }) {
  return (
    <div className="min-w-0">
      <div className="text-[10px] font-extrabold uppercase tracking-[.06em]" style={{ color: 'var(--t-text-3)' }}>
        Past training
      </div>
      <div
        className={desktop ? 'whitespace-nowrap text-[20px]' : 'whitespace-nowrap text-[15px] font-extrabold'}
        style={{ fontFamily: desktop ? 'var(--deep-font-display)' : undefined, color: 'var(--deep-text)' }}
      >
        {formatSheetDate(session.date)} · <span className={`hp-mono font-extrabold ${desktop ? 'text-[18px]' : ''}`}>{timeRangeLabel(session.start, session.end)}</span>
      </div>
      {(session.title || session.type || session.cancelled) && (
        <div className="mt-1 flex min-w-0 flex-wrap items-center gap-1.5">
          <SessionTag session={session} className="max-w-full" />
          {session.cancelled && <CancelledPill />}
          {session.cancelled && <CancelReason reason={session.cancel_reason} />}
        </div>
      )}
    </div>
  );
}

export default PastSessionHead;

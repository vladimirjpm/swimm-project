import React, { useState } from 'react';
import type { NextTraining } from '../../../types';
import ConfirmDialog from '../../../../components/confirm-dialog/confirm-dialog';
import { cancelApi } from '../cancel-api';
import { formatSheetDate } from '../sheets/link-helpers';
import { timeRangeLabel, untilLabel } from '../next/next-format';
import { CancelledPill, CancelReason, SessionTag } from '../next/session-tag';
import { useTrainingsTab } from '../trainings-context';

/**
 * Навигация по ближайшим занятиям ‹ › вида тренера (Т6, хендофф Trainings Tab, 2a / 3b).
 * Состояние (`rollIndex`) живёт в контексте таба — выбранное занятие читают и бассейн, и
 * перекличка, и шторка человека. Отсчёт — `untilLabel` (`next/next-format.ts`), не дублируем.
 *
 * Р9: у выбранного занятия — название разовой тренировки со значком типа, пилюля «Cancelled» с
 * причиной и кнопка «Cancel training» (с подтверждением и причиной) либо «Restore» (без).
 */

/** Максимум причины отмены — как на сервере (`TrainingRsvpRules.CancelReasonMax`). */
const REASON_MAX = 200;

/**
 * Пометки и действие выбранного занятия: название и тип разового, «Cancelled» + причина, «Cancel
 * training» / «Restore». После отмены и возврата перечитываются ближайшие занятия страницы и
 * ответы этого занятия — переклика покажет «Training cancelled» / строки.
 */
function SessionExtras({ session, index, center }: { session: NextTraining; index: number; center: boolean }) {
  const { group, upcoming, reloadNextTrainings } = useTrainingsTab();
  const [confirming, setConfirming] = useState(false);
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);

  const refresh = () => {
    reloadNextTrainings();
    upcoming[index]?.reload();
  };

  const restore = async () => {
    if (!session.id) return;   // ключ занятия — адрес запроса
    setError(null);
    const e = await cancelApi.restore(group.id, session.id);
    if (e) setError(e);
    else refresh();
  };

  const close = () => { setConfirming(false); setReason(''); };
  const cancel = async () => {
    if (!session.id) return { success: false, error: 'Unknown training.' };
    const e = await cancelApi.cancel(group.id, session.id, reason.trim() || null);
    if (e) return { success: false, error: e };
    close();
    refresh();
    return { success: true };
  };

  if (!session.id) return null;
  const align = center ? 'items-center text-center' : 'items-start text-left';
  return (
    <div className={`mt-1 flex flex-col gap-0.5 ${align}`}>
      <SessionTag session={session} className="max-w-full" />
      {session.cancelled && (
        <>
          <CancelledPill />
          <CancelReason reason={session.cancel_reason} />
        </>
      )}
      {session.cancelled ? (
        <button
          type="button"
          onClick={() => { void restore(); }}
          className="cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold"
          style={{ color: 'var(--deep-ow)' }}
        >
          Restore
        </button>
      ) : (
        <button
          type="button"
          onClick={() => setConfirming(true)}
          className="cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold"
          style={{ color: 'var(--deep-danger)' }}
        >
          Cancel training
        </button>
      )}
      {error && (
        <p className="m-0 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }} role="alert">{error}</p>
      )}
      {confirming && (
        <ConfirmDialog
          title={<>Cancel the training on {formatSheetDate(session.date)}?</>}
          confirmLabel="Cancel training"
          cancelLabel="Back"
          onConfirm={cancel}
          onClose={close}
        >
          <input
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Reason — optional, e.g. Pool closed"
            maxLength={REASON_MAX}
            dir="auto"
            autoComplete="off"
            aria-label="Reason"
            className="mt-3 w-full rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-input-bg)] px-3 py-2 text-[13px] text-[var(--t-text)] outline-none focus:border-[var(--t-accent)]"
          />
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            Members see it as cancelled and can't answer. Answers are kept — restore brings them back.
          </p>
        </ConfirmDialog>
      )}
    </div>
  );
}


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

  // «NEXT» — у первого НЕ отменённого (как `group.next_training`), у отменённого отсчёта нет:
  // «IN 2 DAYS» зелёным под «Cancelled» читался как «занятие будет».
  const first = index === sessions.findIndex((s) => !s.cancelled);
  const until = current.cancelled ? '' : untilLabel(current.date, current.start, current.end);
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
          <SessionExtras key={current.id} session={current} index={index} center />
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
        <SessionExtras key={current.id} session={current} index={index} center={false} />
      </div>
      <ArrowButton dir="next" disabled={atEnd} onClick={next} size={36} />
    </div>
  );
}

export default SessionNav;

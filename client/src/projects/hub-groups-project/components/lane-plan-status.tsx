import React from 'react';
import type { LanePlanStatus as PlanStatus } from '../types';
import PlanPill from './trainings/coach/plan-pill';
import type { AutosaveStatus } from './use-lane-autosave';

/**
 * Статус плана дорожек и ЕДИНСТВЕННАЯ кнопка Publish / Unpublish (docs/plans/trainings-coach-flow-plan.md,
 * CF3; макет F2). Заменяет липкую панель Save: слева — пилюля статуса (`PlanPill`, та же, что в
 * списке занятий) и состояние записи («Saving…» / «Saved» / «Not saved · Retry»), справа — кнопка.
 *
 * Где стоит — решает вызывающий: рабочий стол тренера даёт узел в своей шапке (десктоп) или слот
 * нижней панели каркаса (телефон, `components/deep/dock-slot.ts`); редактор кладёт строку туда
 * порталом. Плана ещё нет — только пилюля «No lane plan», без кнопки.
 */
function LanePlanStatus({
  status, unassigned = 0, save = null, busy = false, onRetry, onPublish, onUnpublish,
}: {
  /** null — плана ещё нет. */
  status: PlanStatus | null;
  /** Сколько человек в Unassigned — в пилюле. */
  unassigned?: number;
  /** Состояние автосохранения; null — редактора нет, записывать нечего. */
  save?: AutosaveStatus | null;
  /** Идёт Publish / Unpublish или подготовка диалога. */
  busy?: boolean;
  onRetry?: () => void;
  onPublish?: () => void;
  onUnpublish?: () => void;
}) {
  const button = 'h-[40px] flex-none cursor-pointer rounded-[12px] border px-[18px] text-[13px] font-extrabold disabled:cursor-not-allowed disabled:opacity-60';
  // Пока запись не дошла, публиковать нечего: кнопка ждёт (нажатие само дошлёт правки).
  const failed = save === 'error';
  return (
    <div className="flex min-h-[48px] min-w-0 items-center justify-between gap-3">
      <div className="flex min-w-0 flex-wrap items-center gap-x-2.5 gap-y-0.5">
        <PlanPill state={status ?? 'none'} unassigned={unassigned} />
        {save && !failed && (
          <span className="text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }} role="status" aria-live="polite">
            {save === 'saved' ? 'Saved' : 'Saving…'}
          </span>
        )}
        {failed && (
          <button
            type="button"
            onClick={onRetry}
            className="cursor-pointer border-0 bg-transparent p-0 text-[11.5px] font-extrabold underline"
            style={{ color: 'var(--deep-danger)' }}
          >
            Not saved · Retry
          </button>
        )}
      </div>
      {status === 'draft' && onPublish && (
        <button
          type="button"
          className={button}
          style={{ borderColor: 'var(--deep-ow)', background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }}
          onClick={onPublish}
          disabled={busy || failed}
        >
          Publish
        </button>
      )}
      {status === 'published' && onUnpublish && (
        <button
          type="button"
          className={button}
          style={{ background: 'transparent', borderColor: 'var(--deep-ow-border)', color: 'var(--deep-ow)' }}
          onClick={onUnpublish}
          disabled={busy}
        >
          {busy ? 'Working…' : 'Unpublish'}
        </button>
      )}
    </div>
  );
}

export default LanePlanStatus;

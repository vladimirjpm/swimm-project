import React from 'react';
import type { LanePlanStatus } from '../../../types';

/**
 * Пилюля статуса плана дорожек занятия — карточка списка занятий тренера
 * (docs/plans/trainings-coach-flow-plan.md, CF1; перенесена из прежнего блока бассейна тренера без правок):
 * «● Draft plan» / «● Draft · 3 unassigned» золотом, «Published» (с числом неразложенных —
 * «Published · 3 unassigned») зелёным, «No lane plan» серым.
 */

export type PlanPillState = LanePlanStatus | 'none';

function PlanPill({ state, unassigned = 0 }: { state: PlanPillState; unassigned?: number }) {
  if (state === 'draft') {
    return (
      <span
        className="inline-flex items-center gap-[5px] rounded-full border px-2 py-[3px] text-[10px] font-bold min-[960px]:text-[11px]"
        style={{ borderColor: 'var(--deep-gold-border)', background: 'var(--deep-gold-chip)', color: 'var(--deep-gold)' }}
      >
        <span aria-hidden="true" className="inline-block rounded-full" style={{ width: 6, height: 6, background: 'var(--deep-gold-bar)' }} />
        {unassigned > 0 ? `Draft · ${unassigned} unassigned` : 'Draft plan'}
      </span>
    );
  }
  return (
    <span
      className="text-[11px] font-bold"
      style={{ color: state === 'published' ? 'var(--deep-live)' : 'var(--t-text-3)' }}
    >
      {state === 'published' ? 'Published' : 'No lane plan'}
      {state === 'published' && unassigned > 0 && (
        <> · <span style={{ color: 'var(--deep-gold)' }}>{unassigned}</span> unassigned</>
      )}
    </span>
  );
}

export default PlanPill;

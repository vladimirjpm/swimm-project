import React, { useCallback, useEffect, useState } from 'react';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import type { HubGroupLevels, LanePlan, LanePlanSummary } from '../types';
import GroupLaneBoard from './group-lane-board';
import GroupLaneEditor from './group-lane-editor';
import { formatPlanDate, lanePlansApi, pickDefaultDate, todayInIsrael } from './lane-plans-api';

/**
 * Таб `Lanes` страницы группы (docs/plans/lane-plans-plan.md, L3): план дорожек на дату.
 *
 * Дата выбирается стрелками по существующим планам (участнику — только опубликованные,
 * сервер сам не отдаёт черновики), тренер может открыть любой день полем даты и завести там
 * план. По умолчанию — ближайший план с сегодняшнего дня (по Израилю), иначе последний.
 * Правит только управляющий (`manages` — тот же гейт, что у таба Admin); сервер проверяет
 * права сам, клиентский флаг только прячет кнопки.
 */

const ghostBtn =
  'cursor-pointer rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3 py-[6px] text-[12px] font-extrabold text-[var(--t-accent)] transition-colors hover:bg-[var(--t-accent-soft)] disabled:cursor-not-allowed disabled:opacity-40';
const primaryBtn =
  'cursor-pointer rounded-[9px] border border-[var(--t-accent)] bg-[var(--t-accent)] px-3 py-[6px] text-[12px] font-extrabold text-[var(--t-accent-ink)] disabled:cursor-not-allowed disabled:opacity-50';
const dangerBtn =
  'cursor-pointer rounded-[9px] border border-[var(--t-danger-border)] bg-transparent px-3 py-[6px] text-[12px] font-extrabold text-[var(--t-danger)] disabled:cursor-not-allowed disabled:opacity-40';
const arrowBtn =
  'cursor-pointer rounded-[9px] border border-[var(--t-border)] bg-transparent px-2.5 py-[5px] text-[14px] font-black text-[var(--t-text-2)] hover:bg-[var(--t-surface2)] disabled:cursor-not-allowed disabled:opacity-30';

/** Нет плана на дату — отдельное состояние, не ошибка. */
type PlanState = { kind: 'loading' } | { kind: 'none' } | { kind: 'error' } | { kind: 'ready'; plan: LanePlan };

function StatusBadge({ status }: { status: LanePlan['status'] }) {
  const published = status === 'published';
  return (
    <span
      className={`rounded-full border px-2 py-[2px] text-[11px] font-extrabold ${
        published
          ? 'border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] text-[var(--t-accent)]'
          : 'border-[var(--t-warn-border)] bg-[var(--t-warn-soft)] text-[var(--t-warn)]'
      }`}
    >
      {published ? 'Published' : 'Draft — only coaches see it'}
    </span>
  );
}

function GroupLanes({ groupId, manages }: { groupId: number; manages: boolean }) {
  const [plans, setPlans] = useState<LanePlanSummary[] | null>(null);
  const [listError, setListError] = useState(false);
  const [date, setDate] = useState<string | null>(null);
  const [state, setState] = useState<PlanState>({ kind: 'loading' });
  const [editing, setEditing] = useState(false);
  const [levels, setLevels] = useState<HubGroupLevels | null>(null);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  /** Заготовка нового плана — копия прошлого («Copy from …»); null — план с нуля. */
  const [template, setTemplate] = useState<LanePlan | null>(null);

  const loadList = useCallback(async () => {
    const result = await lanePlansApi.list(groupId);
    if (!result.ok || !result.data) { setListError(true); return null; }
    setPlans(result.data);
    return result.data;
  }, [groupId]);

  // Первый заход: список → дата по умолчанию.
  useEffect(() => {
    let cancelled = false;
    loadList().then((list) => {
      if (!cancelled && list) setDate((d) => d ?? pickDefaultDate(list, todayInIsrael()));
    });
    return () => { cancelled = true; };
  }, [loadList]);

  // Смена даты — план на неё.
  useEffect(() => {
    if (!date) return;
    let cancelled = false;
    setState({ kind: 'loading' });
    setEditing(false);
    setActionError(null);
    lanePlansApi.get(groupId, date).then((result) => {
      if (cancelled) return;
      if (result.ok && result.data) setState({ kind: 'ready', plan: result.data });
      else setState(result.status === 404 ? { kind: 'none' } : { kind: 'error' });
    });
    return () => { cancelled = true; };
  }, [groupId, date]);

  const startEdit = async (copyFrom?: string) => {
    setActionError(null);
    setBusy(true);
    let nextLevels = levels;
    if (!nextLevels) {
      const result = await lanePlansApi.levels(groupId);
      if (!result.ok || !result.data) { setBusy(false); setActionError('Could not load levels. Try again.'); return; }
      nextLevels = result.data;
    }
    let nextTemplate: LanePlan | null = null;
    if (copyFrom) {
      const result = await lanePlansApi.get(groupId, copyFrom);
      if (!result.ok || !result.data) { setBusy(false); setActionError('Could not load that plan. Try again.'); return; }
      nextTemplate = result.data;
    }
    setBusy(false);
    setLevels(nextLevels);
    setTemplate(nextTemplate);
    setEditing(true);
  };

  const setStatus = async (publish: boolean) => {
    if (!date || state.kind !== 'ready') return;
    setBusy(true);
    setActionError(null);
    const result = publish ? await lanePlansApi.publish(groupId, date) : await lanePlansApi.unpublish(groupId, date);
    setBusy(false);
    if (!result.ok) { setActionError(result.error ?? 'Could not change the status. Try again.'); return; }
    setState({ kind: 'ready', plan: { ...state.plan, status: publish ? 'published' : 'draft' } });
    void loadList();
  };

  const remove = async () => {
    if (!date) return { success: false };
    const result = await lanePlansApi.remove(groupId, date);
    if (!result.ok) return { success: false, error: result.error ?? 'Could not delete. Try again.' };
    setConfirmDelete(false);
    setState({ kind: 'none' });
    void loadList();
    return { success: true };
  };

  if (listError) {
    return (
      <div className="deep-card">
        <div className="deep-card-title">Lanes</div>
        <p className="m-0 mt-2 text-[12px] font-bold text-[var(--t-danger)]">Could not load lane plans.</p>
      </div>
    );
  }
  if (!plans || !date) {
    return (
      <div className="deep-card">
        <div className="deep-card-title">Lanes</div>
        <p className="m-0 mt-2 text-[12px] text-[var(--t-text-3)]">Loading…</p>
      </div>
    );
  }

  // Стрелки ходят по существующим планам; список новыми сверху.
  const ascending = [...plans].reverse();
  const prevPlan = [...ascending].reverse().find((p) => p.date < date);
  const nextPlan = ascending.find((p) => p.date > date);
  // Новый план — с числом дорожек последнего плана до этой даты (обычно бассейн тот же).
  const laneCountHint = prevPlan?.lane_count ?? nextPlan?.lane_count ?? 6;
  // Откуда копировать: ближайший план до этой даты, иначе самый свежий.
  const copySource = prevPlan ?? plans[0] ?? null;

  return (
    <div className="deep-card">
      <div className="flex flex-wrap items-center gap-2">
        <div className="deep-card-title mr-auto">Lanes</div>
        <button type="button" className={arrowBtn} onClick={() => prevPlan && setDate(prevPlan.date)} disabled={!prevPlan || editing} aria-label="Previous plan">‹</button>
        <span className="hp-mono min-w-[110px] text-center text-[14px] font-black text-[var(--t-text)]">{formatPlanDate(date)}</span>
        <button type="button" className={arrowBtn} onClick={() => nextPlan && setDate(nextPlan.date)} disabled={!nextPlan || editing} aria-label="Next plan">›</button>
        {manages && (
          <input
            type="date"
            value={date}
            onChange={(e) => { if (e.target.value) setDate(e.target.value); }}
            disabled={editing}
            aria-label="Open any day"
            className="rounded-[9px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2 py-[5px] text-[12px] text-[var(--t-text)]"
          />
        )}
      </div>

      {state.kind === 'ready' && !editing && (
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <StatusBadge status={state.plan.status} />
          {state.plan.can_edit && (
            <span className="ml-auto flex flex-wrap gap-2">
              <button type="button" className={ghostBtn} onClick={() => startEdit()} disabled={busy}>Edit</button>
              {state.plan.status === 'draft'
                ? <button type="button" className={primaryBtn} onClick={() => setStatus(true)} disabled={busy}>Publish</button>
                : <button type="button" className={ghostBtn} onClick={() => setStatus(false)} disabled={busy}>Unpublish</button>}
              <button type="button" className={dangerBtn} onClick={() => setConfirmDelete(true)} disabled={busy}>Delete</button>
            </span>
          )}
        </div>
      )}

      {actionError && <p className="m-0 mt-2 text-[12px] font-bold text-[var(--t-danger)]" role="alert">{actionError}</p>}

      <div className="mt-3">
        {state.kind === 'loading' && <p className="m-0 text-[12px] text-[var(--t-text-3)]">Loading…</p>}
        {state.kind === 'error' && <p className="m-0 text-[12px] font-bold text-[var(--t-danger)]">Could not load this plan.</p>}

        {state.kind === 'none' && !editing && (
          <div className="rounded-[12px] border border-dashed border-[var(--t-border)] p-4 text-center">
            <p className="m-0 text-[13px] font-bold text-[var(--t-text-2)]">No lane plan for {formatPlanDate(date)}.</p>
            {manages && (
              <div className="mt-3 flex flex-wrap justify-center gap-2">
                <button type="button" className={primaryBtn} onClick={() => startEdit()} disabled={busy}>Create plan</button>
                {copySource && (
                  <button type="button" className={ghostBtn} onClick={() => startEdit(copySource.date)} disabled={busy}>
                    Copy from {formatPlanDate(copySource.date)}
                  </button>
                )}
              </div>
            )}
          </div>
        )}

        {state.kind === 'ready' && !editing && <GroupLaneBoard plan={state.plan} />}

        {editing && levels && (state.kind === 'ready' || state.kind === 'none') && (
          <GroupLaneEditor
            key={date}
            groupId={groupId}
            date={date}
            plan={state.kind === 'ready' ? state.plan : null}
            template={state.kind === 'none' ? template : null}
            levels={levels}
            laneCountHint={laneCountHint}
            onSaved={(plan) => { setState({ kind: 'ready', plan }); setEditing(false); void loadList(); }}
            onCancel={() => setEditing(false)}
          />
        )}
      </div>

      {confirmDelete && (
        <ConfirmDialog
          title={`Delete the plan for ${formatPlanDate(date)}?`}
          confirmLabel="Delete plan"
          busyLabel="Deleting…"
          onConfirm={remove}
          onClose={() => setConfirmDelete(false)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            Lanes, workouts and who swims where are removed. Levels of swimmers stay.
          </p>
        </ConfirmDialog>
      )}
    </div>
  );
}

export default GroupLanes;

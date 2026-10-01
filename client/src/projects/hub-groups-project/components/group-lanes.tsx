import React, { useCallback, useEffect, useRef, useState } from 'react';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import type { HubGroupLevels, LanePlan, LanePlanStatus, LanePlanSummary } from '../types';
import GroupLaneBoard from './group-lane-board';
import GroupLaneEditor from './group-lane-editor';
import { formatPlanDate, lanePlansApi, pickDefaultDate, todayInIsrael } from './lane-plans-api';

/**
 * Вид `Lanes` таба Trainings группы (docs/plans/lane-plans-plan.md, L3; Т7 — хендофф Trainings Tab,
 * 1e / 6b, решения Р6): план дорожек на дату.
 *
 * Шапка — навигация по датам: стрелки ходят по существующим планам (участнику — только
 * опубликованные, сервер черновики не отдаёт), управляющий открывает любой день полем даты
 * (прозрачный `<input type="date">` поверх даты). По умолчанию — ближайший план с сегодняшнего
 * дня (по Израилю), иначе последний.
 *
 * Участник видит доску (`GroupLaneBoard`), управляющий — сразу редактор (`GroupLaneEditor`): режима
 * правки нет. Плана на дату нет — управляющему карточка «Create plan / Copy from …». Правит только
 * управляющий (`manages` — тот же гейт, что у таба Admin); сервер проверяет права сам, клиентский
 * флаг только прячет кнопки.
 *
 * Уход с несохранёнными правками (стрелки, выбор даты) спрашивает «Discard unsaved changes?»;
 * флаг правок отдаётся наверх (`onDirtyChange`) — каркас таба спрашивает то же при смене вида.
 */

/** Нет плана на дату — отдельное состояние, не ошибка. */
type PlanState = { kind: 'loading' } | { kind: 'none' } | { kind: 'error' } | { kind: 'ready'; plan: LanePlan };

const navBtn =
  'flex-none cursor-pointer rounded-[10px] border bg-transparent text-[16px] font-black disabled:cursor-not-allowed disabled:opacity-30';

/** Статус плана под датой (телефон) / пилюлей (десктоп). */
function statusInfo(status: LanePlanStatus | null): { text: string; color: string; bar: string; chip: string; border: string } {
  if (status === 'published') {
    return { text: 'PUBLISHED', color: 'var(--deep-live)', bar: 'var(--deep-live)', chip: 'transparent', border: 'var(--deep-live-border)' };
  }
  if (status === 'draft') {
    return { text: 'DRAFT — only coaches see it', color: 'var(--deep-gold)', bar: 'var(--deep-gold-bar)', chip: 'var(--deep-gold-chip)', border: 'var(--deep-gold-border)' };
  }
  return { text: 'NO PLAN', color: 'var(--t-text-3)', bar: 'var(--t-text-3)', chip: 'transparent', border: 'var(--deep-card-border)' };
}

function GroupLanes({
  groupId, manages, desktop, onDirtyChange, onPlansChanged,
}: {
  groupId: number;
  manages: boolean;
  /** ≥960px: шапка в одну строку с тулбаром, редактор колонками. */
  desktop: boolean;
  /** В редакторе есть несохранённые правки (каркас таба спрашивает при смене вида). */
  onDirtyChange?: (dirty: boolean) => void;
  /** План создан / сохранён / опубликован / удалён — вид Next тренера перечитывает статус плана. */
  onPlansChanged?: () => void;
}) {
  const [plans, setPlans] = useState<LanePlanSummary[] | null>(null);
  const [listError, setListError] = useState(false);
  const [date, setDate] = useState<string | null>(null);
  const [state, setState] = useState<PlanState>({ kind: 'loading' });
  /** Тренер нажал «Create plan» на дате без плана — открыт пустой редактор. */
  const [creating, setCreating] = useState(false);
  const [levels, setLevels] = useState<HubGroupLevels | null>(null);
  const [levelsError, setLevelsError] = useState(false);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  /** Заготовка черновика — копия другого плана («Copy from …»); null — исходный вид плана. */
  const [template, setTemplate] = useState<LanePlan | null>(null);
  /** Меняется, когда редактор надо собрать заново (копия, сохранение): `key` вместе с датой. */
  const [editorNonce, setEditorNonce] = useState(0);
  const [dirty, setDirty] = useState(false);
  /** Дата, куда хотели уйти, но ждём ответа на «Discard unsaved changes?». */
  const [pendingDate, setPendingDate] = useState<string | null>(null);
  /** Узел шапки (десктоп), куда редактор выносит тулбар. */
  const [toolbarHost, setToolbarHost] = useState<HTMLElement | null>(null);
  const dateInput = useRef<HTMLInputElement>(null);

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

  // Уровни нужны только редактору — управляющему грузим сразу, чтобы он открывался без паузы.
  useEffect(() => {
    if (!manages) return undefined;
    let cancelled = false;
    setLevelsError(false);
    lanePlansApi.levels(groupId).then((result) => {
      if (cancelled) return;
      if (result.ok && result.data) setLevels(result.data);
      else setLevelsError(true);
    });
    return () => { cancelled = true; };
  }, [groupId, manages]);

  // Смена даты — план на неё.
  useEffect(() => {
    if (!date) return undefined;
    let cancelled = false;
    setState({ kind: 'loading' });
    setCreating(false);
    setTemplate(null);
    setActionError(null);
    lanePlansApi.get(groupId, date).then((result) => {
      if (cancelled) return;
      if (result.ok && result.data) setState({ kind: 'ready', plan: result.data });
      else setState(result.status === 404 ? { kind: 'none' } : { kind: 'error' });
    });
    return () => { cancelled = true; };
  }, [groupId, date]);

  // Флаг правок — наверх; при размонтировании (смена вида, уход с таба) сбрасывается.
  useEffect(() => { onDirtyChange?.(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange?.(false), [onDirtyChange]);

  const handleDirty = useCallback((value: boolean) => setDirty(value), []);

  /** Перейти на дату; с несохранёнными правками — после подтверждения. */
  const goTo = (next: string) => {
    if (next === date) return;
    if (dirty) setPendingDate(next);
    else setDate(next);
  };

  const refreshPlans = () => {
    void loadList();
    onPlansChanged?.();
  };

  const setStatus = async (publish: boolean): Promise<string | null> => {
    if (!date || state.kind !== 'ready') return null;
    const result = publish ? await lanePlansApi.publish(groupId, date) : await lanePlansApi.unpublish(groupId, date);
    if (!result.ok) return result.error ?? 'Could not change the status. Try again.';
    setState({ kind: 'ready', plan: { ...state.plan, status: publish ? 'published' : 'draft' } });
    refreshPlans();
    return null;
  };

  const remove = async () => {
    if (!date) return { success: false };
    const result = await lanePlansApi.remove(groupId, date);
    if (!result.ok) return { success: false, error: result.error ?? 'Could not delete. Try again.' };
    setConfirmDelete(false);
    setState({ kind: 'none' });
    setCreating(false);
    setTemplate(null);
    setDirty(false);
    refreshPlans();
    return { success: true };
  };

  /** Копия плана `sourceDate` как заготовка редактора; редактор собирается заново. */
  const copyFrom = async (sourceDate: string): Promise<string | null> => {
    const result = await lanePlansApi.get(groupId, sourceDate);
    if (!result.ok || !result.data) return 'Could not load that plan. Try again.';
    setTemplate(result.data);
    setCreating(true);
    setEditorNonce((n) => n + 1);
    return null;
  };

  /** Кнопка «Copy from …» карточки «No lane plan» (до открытия редактора). */
  const startCopy = async (sourceDate: string) => {
    setActionError(null);
    setBusy(true);
    const err = await copyFrom(sourceDate);
    setBusy(false);
    if (err) setActionError(err);
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
  // Откуда копировать: ближайший план до этой даты, иначе самый свежий из других.
  const copySource = prevPlan ?? plans.find((p) => p.date !== date) ?? null;

  const plan = state.kind === 'ready' ? state.plan : null;
  const editable = manages && (state.kind === 'none' || (state.kind === 'ready' && state.plan.can_edit));
  const status = statusInfo(plan?.status ?? null);

  const dateLabel = formatPlanDate(date);
  const dateButton = (extraClass: string, children: React.ReactNode) => (
    <div className={`relative ${extraClass}`}>
      {children}
      {manages && (
        <input
          ref={dateInput}
          type="date"
          value={date}
          onChange={(e) => { if (e.target.value) goTo(e.target.value); }}
          onClick={() => { try { dateInput.current?.showPicker?.(); } catch { /* браузер без showPicker — откроется сам */ } }}
          aria-label="Open any day"
          className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
        />
      )}
    </div>
  );

  const arrows = (size: number) => (
    <>
      <PrevNext side="prev" size={size} disabled={!prevPlan} onClick={() => prevPlan && goTo(prevPlan.date)} />
      <PrevNext side="next" size={size} disabled={!nextPlan} onClick={() => nextPlan && goTo(nextPlan.date)} />
    </>
  );

  /** Шапка десктопа (6b): разделитель, ‹ ›, «Lanes · дата ▾», пилюля статуса, справа тулбар. */
  const header = (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
      <span aria-hidden="true" className="h-[20px] w-px flex-none" style={{ background: 'var(--deep-card-border)' }} />
      <div className="flex items-center gap-1.5">{arrows(36)}</div>
      {dateButton('flex-none', (
        <div className="flex items-center gap-1.5 text-[18px]" style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}>
          <span>Lanes · {dateLabel}</span>
          {manages && <span aria-hidden="true" className="text-[11px]" style={{ color: 'var(--deep-ow)' }}>▾</span>}
        </div>
      ))}
      <span
        className="inline-flex flex-none items-center gap-1.5 rounded-full border px-2.5 py-[3px] text-[11px] font-extrabold"
        style={{
          borderColor: status.border,
          background: status.chip,
          color: status.color,
        }}
      >
        <span aria-hidden="true" className="inline-block flex-none rounded-full" style={{ width: 7, height: 7, background: status.bar }} />
        {plan?.status === 'draft' ? 'Draft' : plan?.status === 'published' ? 'Published' : 'No plan'}
      </span>
      {/* Сюда редактор выносит тулбар (портал). */}
      <div ref={setToolbarHost} className="ml-auto min-w-0" />
    </div>
  );

  return (
    <div className="flex min-w-0 flex-col gap-3">
      {/* Порядок кнопок: ‹ › рядом на десктопе, по краям даты на телефоне — как в прототипе. */}
      {desktop ? header : (
        <div className="flex items-center gap-2">
          <PrevNext side="prev" size={40} disabled={!prevPlan} onClick={() => prevPlan && goTo(prevPlan.date)} />
          {dateButton('min-w-0 flex-1 text-center', (
            <>
              <div className="flex items-center justify-center gap-1 text-[15px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                <span>{dateLabel}</span>
                {manages && <span aria-hidden="true" className="text-[11px]" style={{ color: 'var(--deep-ow)' }}>▾</span>}
              </div>
              <div className="text-[10.5px] font-extrabold" style={{ color: status.color }}>{status.text}</div>
            </>
          ))}
          <PrevNext side="next" size={40} disabled={!nextPlan} onClick={() => nextPlan && goTo(nextPlan.date)} />
        </div>
      )}

      {actionError && <p className="m-0 text-[12px] font-bold text-[var(--t-danger)]" role="alert">{actionError}</p>}

      <div>
        {state.kind === 'loading' && <p className="m-0 text-[12px] text-[var(--t-text-3)]">Loading…</p>}
        {state.kind === 'error' && <p className="m-0 text-[12px] font-bold text-[var(--t-danger)]">Could not load this plan.</p>}

        {/* Участник (и управляющий без права правки): опубликованный план. */}
        {state.kind === 'ready' && !editable && <GroupLaneBoard plan={state.plan} />}
        {state.kind === 'none' && !manages && <NoPlan label={dateLabel} />}

        {/* Управляющий: плана нет — карточка с «Create plan / Copy from …», дальше редактор. */}
        {editable && state.kind === 'none' && !creating && (
          <NoPlan label={dateLabel}>
            <div className="mt-3 flex flex-wrap justify-center gap-2">
              <button
                type="button"
                className="h-[40px] cursor-pointer rounded-[10px] border-0 px-4 text-[12px] font-extrabold disabled:cursor-not-allowed disabled:opacity-50"
                style={{ background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }}
                onClick={() => setCreating(true)}
                disabled={busy}
              >
                Create plan
              </button>
              {copySource && (
                <button
                  type="button"
                  className="h-[40px] cursor-pointer rounded-[10px] border bg-transparent px-4 text-[12px] font-extrabold disabled:cursor-not-allowed disabled:opacity-50"
                  style={{ borderColor: 'var(--deep-card-border)', color: 'var(--t-text-3)' }}
                  onClick={() => startCopy(copySource.date)}
                  disabled={busy}
                >
                  Copy from {formatPlanDate(copySource.date)}
                </button>
              )}
            </div>
          </NoPlan>
        )}

        {editable && (state.kind === 'ready' || (state.kind === 'none' && creating)) && (
          levels ? (
            <GroupLaneEditor
              key={`${date}:${editorNonce}`}
              groupId={groupId}
              date={date}
              plan={plan}
              template={template}
              levels={levels}
              laneCountHint={laneCountHint}
              desktop={desktop}
              toolbarHost={toolbarHost}
              copyFrom={copySource?.date ?? null}
              onSaved={(saved) => {
                setState({ kind: 'ready', plan: saved });
                setTemplate(null);
                setCreating(false);
                setDirty(false);
                setEditorNonce((n) => n + 1);
                refreshPlans();
              }}
              onSetStatus={setStatus}
              onDelete={() => setConfirmDelete(true)}
              onCopy={copyFrom}
              onDirtyChange={handleDirty}
            />
          ) : (
            <p className={`m-0 text-[12px] ${levelsError ? 'font-bold text-[var(--t-danger)]' : 'text-[var(--t-text-3)]'}`}>
              {levelsError ? 'Could not load levels. Try again.' : 'Loading…'}
            </p>
          )
        )}
      </div>

      {confirmDelete && (
        <ConfirmDialog
          title={`Delete the plan for ${dateLabel}?`}
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
      {pendingDate && (
        <ConfirmDialog
          title="Discard unsaved changes?"
          confirmLabel="Discard"
          onConfirm={async () => {
            setDirty(false);
            setDate(pendingDate);
            setPendingDate(null);
            return { success: true };
          }}
          onClose={() => setPendingDate(null)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            The lane plan for {dateLabel} has changes that are not saved yet.
          </p>
        </ConfirmDialog>
      )}
    </div>
  );
}

/** Кнопка ‹ / › шапки телефона. */
function PrevNext({ side, size, disabled, onClick }: { side: 'prev' | 'next'; size: number; disabled: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      className={navBtn}
      style={{ width: size, height: size, borderColor: 'var(--deep-card-border)', color: 'var(--t-text-3)' }}
      onClick={onClick}
      disabled={disabled}
      aria-label={side === 'prev' ? 'Previous plan' : 'Next plan'}
    >
      {side === 'prev' ? '‹' : '›'}
    </button>
  );
}

/** «No lane plan for …» в пунктирной рамке. */
function NoPlan({ label, children }: { label: string; children?: React.ReactNode }) {
  return (
    <div className="rounded-[12px] p-4 text-center" style={{ border: '1px dashed var(--deep-card-border)' }}>
      <p className="m-0 text-[13px] font-bold" style={{ color: 'var(--t-text-3)' }}>No lane plan for {label}.</p>
      {children}
    </div>
  );
}

export default GroupLanes;

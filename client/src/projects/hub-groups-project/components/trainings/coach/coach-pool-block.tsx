import React, { useMemo, useState } from 'react';
import type { NextTraining, TrainingLanePerson, TrainingRsvp } from '../../../types';
import type { TrainingRsvpState } from '../../../use-training-rsvp';
import { lanePlansApi } from '../../lane-plans-api';
import { FullPool, MiniPool } from '../next/lane-block';
import { useTrainingsTab } from '../trainings-context';
import { MarkDot } from '../trainings-section';

/**
 * Бассейн вида тренера (Т6, хендофф Trainings Tab, 2a / 3b): мини-бассейн (телефон) или полный
 * (десктоп) выбранного занятия + статус плана дорожек НА ЕГО ДАТУ и вход в Lanes. Вид по
 * дорожкам считает сервер (`lane_view` ответа занятия) — здесь только рисуем.
 *
 * Статус плана берём из `lanePlans` контекста (`Draft` / `Published` / нет плана). Число
 * неразложенных даёт полный план выбранного занятия — `upcomingPlans` контекста (Т8); пока плана
 * там нет, пилюля без числа. Золотая точка у входа в Lanes — `marks.lanes` (план ближайшего занятия).
 */

type PlanState = 'draft' | 'published' | 'none';

/** Своей дорожки у тренера нет — подсветка и кольцо «меня» отключены, даже если он сам в составе. */
function withoutMe(r: TrainingRsvp): TrainingRsvp {
  const view = r.lane_view;
  if (!view) return r;
  const clear = (people: TrainingLanePerson[]) => people.map((p) => (p.is_me ? { ...p, is_me: false } : p));
  return {
    ...r,
    lane_view: {
      ...view,
      lanes: view.lanes.map((l) => ({ ...l, people: clear(l.people) })),
      no_lane: clear(view.no_lane),
    },
  };
}

/**
 * Пилюля статуса плана: «● Draft plan» / «● Draft · 3 unassigned» золотом, «Published» (с числом
 * неразложенных — «Published · 3 unassigned») зелёным, «No lane plan» серым.
 */
function PlanPill({ state, unassigned = 0 }: { state: PlanState; unassigned?: number }) {
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

function CoachPoolBlock({ session, roll }: { session: NextTraining; roll: TrainingRsvpState | null }) {
  const { group, isDesktop, setView, lanePlans, reloadLanePlans, upcomingPlans, marks } = useTrainingsTab();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const raw = roll?.rsvp ?? null;
  const r = useMemo(() => (raw ? withoutMe(raw) : null), [raw]);

  // Планов ещё нет в памяти (грузятся) — статус не показываем, чтобы не мигать «No lane plan».
  const plan = lanePlans?.find((p) => p.date === session.date) ?? null;
  const state: PlanState | null = lanePlans == null ? null : plan ? plan.status : 'none';
  // Неразложенные — из полного плана ЭТОГО занятия; плана в памяти ещё нет — без числа.
  const unassigned = upcomingPlans[session.date]?.unassigned.length ?? 0;
  const lanesDot = marks.lanes ? <MarkDot mark={{ ...marks.lanes, count: undefined }} /> : null;

  const publish = async () => {
    setBusy(true);
    setError(null);
    const res = await lanePlansApi.publish(group.id, session.date);
    setBusy(false);
    if (!res.ok) {
      setError(res.error ?? 'Could not publish the plan. Try again.');
      return;
    }
    // Вид по дорожкам занятия поменяется на опубликованный план.
    reloadLanePlans();
    roll?.reload();
  };

  if (!isDesktop) {
    const footer = (
      <div
        className="flex items-center justify-between gap-3 px-2.5 py-1.5 text-[11px] font-bold"
        style={{ borderTop: r?.lane_view ? '1px solid var(--deep-pool-border)' : undefined, color: 'var(--t-text-3)' }}
      >
        {state ? <PlanPill state={state} unassigned={unassigned} /> : <span />}
        <button
          type="button"
          onClick={() => setView('lanes')}
          className="-my-[7px] inline-flex h-10 cursor-pointer items-center gap-1.5 border-0 bg-transparent px-2 text-[11px] font-extrabold"
          style={{ color: 'var(--deep-ow)' }}
        >
          Lanes ›
          {lanesDot}
        </button>
      </div>
    );
    if (r?.lane_view) return <MiniPool r={r} footer={footer} />;
    // Вида по дорожкам нет — бассейна нет, строка статуса плана остаётся.
    return (
      <div className="overflow-hidden rounded-[12px] border" style={{ borderColor: 'var(--deep-pool-border)', background: 'var(--deep-pool-water)' }}>
        {footer}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center justify-between gap-x-2.5 gap-y-2">
        <span className="flex items-center gap-2 text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
          Lanes
          {state && <PlanPill state={state} unassigned={unassigned} />}
        </span>
        <span className="flex items-center gap-1.5">
          <button
            type="button"
            onClick={() => setView('lanes')}
            className="inline-flex h-[30px] cursor-pointer items-center gap-1.5 whitespace-nowrap border px-3 text-[12px] font-extrabold"
            style={{ borderRadius: 9, borderColor: 'var(--deep-ow-border)', background: 'var(--deep-ow-chip)', color: 'var(--deep-ow)' }}
          >
            ✎ Edit lanes
            {lanesDot}
          </button>
          {state === 'draft' && (
            <button
              type="button"
              disabled={busy}
              onClick={() => { void publish(); }}
              className="h-[30px] cursor-pointer whitespace-nowrap border px-3 text-[12px] font-extrabold disabled:cursor-default disabled:opacity-60"
              style={{ borderRadius: 9, borderColor: 'var(--deep-ow)', background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }}
            >
              Publish
            </button>
          )}
        </span>
      </div>
      {error && (
        <p className="m-0 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }} role="alert">{error}</p>
      )}
      {r?.lane_view && <FullPool r={r} coach />}
    </div>
  );
}

export default CoachPoolBlock;

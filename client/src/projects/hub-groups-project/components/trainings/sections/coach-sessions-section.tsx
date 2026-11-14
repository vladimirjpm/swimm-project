import React from 'react';
import type { NextTraining } from '../../../types';
import PlanPill, { type PlanPillState } from '../coach/plan-pill';
import { nextSessions, timeRangeLabel, untilLabel } from '../next/next-format';
import { CancelledPill, SessionTag } from '../next/session-tag';
import { formatSheetDate } from '../sheets/link-helpers';
import { useTrainingsTab } from '../trainings-context';
import TrainingsEmpty, { TrainingsEmptyActions, TrainingsEmptyButton } from '../trainings-empty';
import { MarkDot, TrainingsSection } from '../trainings-section';

/**
 * Список занятий УПРАВЛЯЮЩЕГО — вход в таб (docs/plans/trainings-coach-flow-plan.md, CF1; макет F1).
 * Ближайшие занятия (`nextSessions(group)`) карточками; лента прошедших — отдельной секцией ниже
 * (`training-feed`). Open открывает рабочий стол занятия (`openDesk` → `?session=<id>`).
 *
 * На карточке: дата, время и отсчёт, название / тип разового занятия, «Cancelled», строка ответов
 * (`upcoming[i].rsvp`) и статус плана дорожек на дату занятия (`lanePlans`). Метки внимания тренера
 * (не ответили — красная, черновик / неразложенные — золотая) стоят на ближайшем не отменённом
 * занятии. Строки «Workout» и «N need you» макета добавляют CF4 и CF6.
 *
 * Расписания нет — пустое состояние с «Set schedule» (как было в виде Next тренера).
 */

const LIVE_BORDER = { borderColor: 'var(--deep-live-border)' } as const;

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex min-h-[22px] items-center gap-2 text-[12px]">
      <span className="w-[62px] flex-none font-bold" style={{ color: 'var(--t-text-3)' }}>{label}</span>
      <span className="min-w-0 font-extrabold" style={{ color: 'var(--deep-text)' }}>{children}</span>
    </div>
  );
}

function SessionCard({ session, index, first }: { session: NextTraining; index: number; first: boolean }) {
  const { upcoming, lanePlans, upcomingPlans, marks, openDesk } = useTrainingsTab();
  const roll = upcoming[index] ?? null;
  // Ответы пришли именно на это занятие (при смене списка на секунду там ещё чужие).
  const r = roll?.rsvp && roll.rsvp.session_id === session.id ? roll.rsvp : null;
  const none = r ? Math.max(0, r.total - r.yes - r.maybe - r.no) : 0;

  // Планов ещё нет в памяти (грузятся) — статус не показываем, чтобы не мигать «No lane plan».
  const plan = lanePlans?.find((p) => p.date === session.date) ?? null;
  const state: PlanPillState | null = lanePlans == null ? null : plan ? plan.status : 'none';
  const unassigned = upcomingPlans[session.date]?.unassigned.length ?? 0;

  const until = session.cancelled ? '' : untilLabel(session.date, session.start, session.end);

  return (
    <article className="deep-card mb-0 flex min-w-0 flex-col gap-2.5" style={first ? LIVE_BORDER : undefined}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-[16px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
            {formatSheetDate(session.date)}
          </div>
          <div className="text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>
            <span className="hp-mono">{timeRangeLabel(session.start, session.end)}</span>
            {until && <span style={{ color: 'var(--deep-live)' }}> · {until.toLowerCase()}</span>}
          </div>
        </div>
        {first && (marks.next || marks.lanes) && (
          <span className="flex flex-none items-center gap-1.5 pt-1">
            {marks.next && <MarkDot mark={marks.next} />}
            {marks.lanes && <MarkDot mark={marks.lanes} />}
          </span>
        )}
      </div>

      {(session.title || session.type || session.cancelled) && (
        <div className="flex min-w-0 flex-wrap items-center gap-1.5">
          <SessionTag session={session} className="max-w-full" />
          {session.cancelled && <CancelledPill />}
        </div>
      )}

      <div className="flex flex-col gap-1">
        <Row label="Answers">
          {r ? (
            <span className="hp-mono">
              <span style={{ color: 'var(--deep-live)' }}>{r.yes}</span> going
              {r.maybe > 0 && <> · <span style={{ color: 'var(--deep-gold)' }}>{r.maybe}</span> maybe</>}
              {r.no > 0 && <> · <span style={{ color: 'var(--deep-danger)' }}>{r.no}</span> out</>}
              {none > 0 && <> · {none} no answer</>}
            </span>
          ) : (
            <span style={{ color: 'var(--t-text-3)' }}>{roll && !roll.loading ? '—' : 'Loading…'}</span>
          )}
        </Row>
        <Row label="Lanes">{state ? <PlanPill state={state} unassigned={unassigned} /> : null}</Row>
      </div>

      {session.id && (
        <button
          type="button"
          onClick={() => openDesk(session.id!)}
          className="mt-auto h-[40px] w-full cursor-pointer rounded-[10px] border px-4 text-[13px] font-extrabold"
          style={first
            ? { borderColor: 'var(--deep-ow)', background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }
            : { borderColor: 'var(--deep-ow-border)', background: 'var(--deep-ow-chip)', color: 'var(--deep-ow)' }}
        >
          Open
        </button>
      )}
    </article>
  );
}

function CoachSessionsSection() {
  const { group, isDesktop, openSchedule, openSheet } = useTrainingsTab();
  const sessions = nextSessions(group);
  const firstIndex = sessions.findIndex((s) => !s.cancelled);

  if (sessions.length === 0) {
    return (
      <TrainingsSection id="coach-sessions" bare>
        <div className={isDesktop ? undefined : 'deep-panel-row'}>
          <TrainingsEmpty
            glyph="◷"
            title="No weekly schedule yet"
            text="Set training days and members can answer “Going” for each one. Lanes and roll call work from the schedule."
          >
            <TrainingsEmptyActions>
              {openSchedule && <TrainingsEmptyButton primary onClick={openSchedule}>Set schedule</TrainingsEmptyButton>}
              <TrainingsEmptyButton onClick={() => openSheet({ kind: 'new-training' })}>+ New training</TrainingsEmptyButton>
            </TrainingsEmptyActions>
          </TrainingsEmpty>
        </div>
      </TrainingsSection>
    );
  }

  return (
    <TrainingsSection id="coach-sessions" bare>
      <div className={isDesktop ? undefined : 'deep-panel-row'}>
        <div className="mb-2 text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
          Upcoming
        </div>
        <div
          className="grid gap-3"
          style={{ gridTemplateColumns: isDesktop ? 'repeat(auto-fill, minmax(230px, 1fr))' : 'minmax(0, 1fr)' }}
        >
          {sessions.map((s, i) => (
            <SessionCard key={s.id ?? `${s.date}-${s.start}`} session={s} index={i} first={i === firstIndex} />
          ))}
        </div>
      </div>
    </TrainingsSection>
  );
}

export default CoachSessionsSection;

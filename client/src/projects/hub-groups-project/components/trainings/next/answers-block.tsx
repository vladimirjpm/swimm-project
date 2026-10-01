import React from 'react';
import type { NextTraining, RsvpAnswer } from '../../../types';
import type { TrainingRsvpState } from '../../../use-training-rsvp';
import { formatSheetDate } from '../sheets/link-helpers';
import { useTrainingsTab } from '../trainings-context';
import { nextSessions, relativePrefix, rowDateLabel } from './next-format';
import { CancelledPill, CancelReason, SessionTag } from './session-tag';

/**
 * «Your answers» — ответы участника на три ближайших занятия (Т5, хендофф Trainings Tab, 2a / 3a).
 * Состояние берём из `useTrainingsTab().upcoming` по индексу строки: первое НЕ отменённое занятие —
 * тот же экземпляр, что у шапки страницы, остальные — свои. Телефон — строки со справа тремя
 * кнопками 40×40; десктоп — сетка мини-карточек.
 *
 * Р9: разовое занятие несёт название и значок типа рядом с датой; отменённое остаётся в списке с
 * пилюлей «Cancelled» и причиной — кнопок ответа у него нет (сервер всё равно не примет).
 */

const ANSWERS: { id: RsvpAnswer; glyph: string; label: string }[] = [
  { id: 'yes', glyph: '✓', label: 'Going' },
  { id: 'maybe', glyph: '?', label: 'Not sure' },
  { id: 'no', glyph: '✕', label: 'Can’t come' },
];

const ANSWER_TEXT: Record<RsvpAnswer, { text: string; color: string }> = {
  yes: { text: 'Going', color: 'var(--deep-live)' },
  maybe: { text: 'Not sure', color: 'var(--deep-gold)' },
  no: { text: 'Can’t come', color: 'var(--deep-danger)' },
};

const SELECTED: Record<RsvpAnswer, React.CSSProperties> = {
  yes: { background: 'var(--deep-live)', color: 'var(--deep-accent-ink)', borderColor: 'var(--deep-live)' },
  maybe: { background: 'var(--deep-gold-chip)', color: 'var(--deep-gold)', borderColor: 'var(--deep-gold-border)' },
  no: { background: 'var(--deep-danger-soft)', color: 'var(--deep-danger)', borderColor: 'var(--deep-danger)' },
};

const IDLE: React.CSSProperties = {
  background: 'transparent', color: 'var(--t-text-3)', borderColor: 'var(--deep-card-border)',
};

/** Заголовок блока: «YOUR ANSWERS» + (десктоп) красная точка, если на ближайшее ещё не ответил. */
export function AnswersCaption({ dot = false }: { dot?: boolean }) {
  return (
    <div
      className="flex items-center gap-1.5 text-[10px] font-extrabold uppercase tracking-[.07em]"
      style={{ color: 'var(--t-text-3)' }}
    >
      Your answers
      {dot && (
        <span
          aria-hidden="true"
          className="inline-block flex-none rounded-full"
          style={{ width: 7, height: 7, background: 'var(--deep-danger)' }}
        />
      )}
    </div>
  );
}

/**
 * Ответ на ближайшее занятие ещё не дан и дать можно — для точки у подписи. На перерыве
 * человека не спрашивают — красного нет (`marks-spec.md`: `can_answer && mine == null && !on_break`).
 */
export function useFirstUnanswered(): boolean {
  // Ближайшее НЕ отменённое занятие — это и есть `rsvp` страницы (отменённое ответа не ждёт).
  const { rsvp } = useTrainingsTab();
  const r = rsvp?.rsvp;
  return !!r && r.can_answer && r.mine == null && !r.on_break;
}

/** Три кнопки ✓ ? ✕ одного занятия. `compact` — десктопная мини-карточка (сетка, высота 34). */
function AnswerButtons({
  state, date, compact, onPick,
}: {
  state: TrainingRsvpState | null;
  date: string;
  compact: boolean;
  onPick: (next: RsvpAnswer) => void;
}) {
  const r = state?.rsvp ?? null;
  const current = r?.mine?.answer ?? null;
  const disabled = !r || !r.can_answer || state?.loading === true;
  return (
    <div
      role="group"
      aria-label={`Your answer — ${formatSheetDate(date)}`}
      className={compact ? 'grid grid-cols-3 gap-1' : 'flex flex-none gap-1'}
    >
      {ANSWERS.map((a) => {
        const on = current === a.id;
        return (
          <button
            key={a.id}
            type="button"
            disabled={disabled}
            aria-pressed={on}
            aria-label={`${a.label} — ${formatSheetDate(date)}`}
            onClick={() => onPick(a.id)}
            className={`cursor-pointer border font-black disabled:cursor-default disabled:opacity-50 ${compact
              ? 'h-[34px] rounded-[8px] text-[13px]'
              : 'h-[40px] w-[40px] rounded-[10px] text-[14px]'}`}
            style={on ? SELECTED[a.id] : IDLE}
          >
            {a.glyph}
          </button>
        );
      })}
    </div>
  );
}

/** Подпись под датой: ответ / «No answer yet» / «On break». */
function Caption({ state, date, first }: { state: TrainingRsvpState | null; date: string; first: boolean }) {
  const r = state?.rsvp ?? null;
  const mine = r?.mine ?? null;
  let text: string;
  let color: string;
  if (mine) {
    const a = ANSWER_TEXT[mine.answer];
    text = a.text + (mine.set_by_coach ? ' · by coach' : '');
    color = a.color;
  } else if (r?.on_break) {
    text = 'On break';
    color = 'var(--t-text-3)';
  } else {
    // Нет ответа: у ближайшего — тревожно красным, у остальных — спокойно; «Tonight · » / «Tomorrow · » спереди.
    text = `${relativePrefix(date)}No answer yet`;
    color = first ? 'var(--deep-danger)' : 'var(--t-text-3)';
  }
  return <div className="text-[10.5px] font-bold" style={{ color }}>{text}</div>;
}

export default function AnswersBlock({ variant }: { variant: 'phone' | 'desktop' }) {
  const { group, upcoming, breaks } = useTrainingsTab();
  const sessions = nextSessions(group);
  if (sessions.length === 0) return null;
  const compact = variant === 'desktop';
  // «Первое» для тревожного красного — первое НЕ отменённое занятие, а не строка 0.
  const firstLive = sessions.findIndex((s) => !s.cancelled);

  const pick = async (i: number, ans: RsvpAnswer) => {
    const state = upcoming[i];
    const r = state?.rsvp;
    if (!state || !r) return;
    const mine = r.mine ?? null;
    const next = mine?.answer === ans ? null : ans;
    // Заметка живёт при смене «иду» ↔ «не уверен»; «не приду» и снятие её обнуляют.
    const note = next === 'yes' || next === 'maybe' ? mine?.note ?? null : null;
    const wasOnBreak = !!r.on_break;
    await state.answer(next, note);
    // «Going» на занятии, где зритель на перерыве, снимает перерыв (сервер) — перечитываем его
    // и ответы остальных занятий (знаменатель, бассейн, «On break»).
    if (next === 'yes' && wasOnBreak) {
      breaks.reload();
      upcoming.forEach((u, j) => { if (j !== i) u?.reload(); });
    }
  };

  const rows = sessions.map((s, i) => {
    const state = upcoming[i] ?? null;
    // Ответ пришёл на прежнее занятие (список сменился) — рисуем как «ещё грузится».
    const stale = !!state?.rsvp && state.rsvp.session_id !== s.id;
    return { s, i, state: stale ? null : state };
  });

  /** Подпись под датой: у отменённого — пилюля и причина, иначе ответ. */
  const caption = (s: NextTraining, i: number, state: TrainingRsvpState | null) => (s.cancelled ? (
    <div className="flex flex-col items-start gap-0.5">
      <CancelledPill />
      <CancelReason reason={s.cancel_reason} />
    </div>
  ) : (
    <Caption state={state} date={s.date} first={i === firstLive} />
  ));

  if (compact) {
    return (
      <div className="grid gap-2" style={{ gridTemplateColumns: 'repeat(3, minmax(0, 1fr))' }}>
        {rows.map(({ s, i, state }) => {
          const unanswered = i === firstLive && !s.cancelled && !!state?.rsvp && state.rsvp.mine == null && !state.rsvp.on_break;
          return (
            <div
              key={s.id ?? `${s.date}-${s.start}`}
              className="flex min-w-0 flex-col gap-2 rounded-[12px] border p-2.5"
              style={{
                background: 'var(--deep-card-bg-raised)',
                borderColor: unanswered ? 'var(--deep-danger)' : 'var(--deep-card-border)',
              }}
            >
              <div className="min-w-0">
                <div className="whitespace-nowrap text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                  {rowDateLabel(s.date, s.start)}
                </div>
                <SessionTag session={s} className="max-w-full" />
                {caption(s, i, state)}
              </div>
              {!s.cancelled && (
                <AnswerButtons state={state} date={s.date} compact onPick={(a) => { void pick(i, a); }} />
              )}
              {state?.error && (
                <p className="m-0 text-[12px] font-bold" style={{ color: 'var(--deep-danger)' }}>{state.error}</p>
              )}
            </div>
          );
        })}
      </div>
    );
  }

  return (
    <div role="list">
      {rows.map(({ s, i, state }) => (
        <div
          key={s.id ?? `${s.date}-${s.start}`}
          role="listitem"
          style={{ borderTop: i > 0 ? '1px solid var(--deep-card-border)' : undefined }}
        >
          <div className="flex min-h-[48px] items-center gap-2 py-1">
            <div className="min-w-0 flex-1">
              <div className="flex min-w-0 items-baseline gap-2">
                <span className="flex-none whitespace-nowrap text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                  {rowDateLabel(s.date, s.start)}
                </span>
                <SessionTag session={s} className="min-w-0" />
              </div>
              {caption(s, i, state)}
            </div>
            {!s.cancelled && (
              <AnswerButtons state={state} date={s.date} compact={false} onPick={(a) => { void pick(i, a); }} />
            )}
          </div>
          {state?.error && (
            <p className="m-0 pb-1.5 text-[12px] font-bold" style={{ color: 'var(--deep-danger)' }}>{state.error}</p>
          )}
        </div>
      ))}
    </div>
  );
}

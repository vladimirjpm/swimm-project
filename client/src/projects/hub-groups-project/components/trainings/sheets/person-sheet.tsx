import React, { useState } from 'react';
import { DeepSheet, SheetButton, SheetError, SheetFooter } from '../../../../components/deep/deep-sheet';
import type { PersonStatus, RsvpAnswer, RsvpNote } from '../../../types';
import { rsvpPersonKey } from '../../../use-training-rsvp';
import { todayInIsrael } from '../../lane-plans-api';
import { RsvpNotes } from '../../group-pool';
import { statusOf } from '../../person-status';
import { findBreak, laneCaption, laneOfPerson, ANSWER_PILL } from '../coach/roll-format';
import { useTrainingsTab } from '../trainings-context';
import type { TrainingsSheetProps } from '../trainings-sheets';
import { addDays, formatSheetDate } from './link-helpers';

/**
 * Шторка человека переклички (Т6, хендофф Trainings Tab, 1d #3; тон `coach`): ответ на выбранное
 * занятие + заметка и статус в группе (в строю / перерыв до даты / «не тренируется с нами»).
 * Открывается из строки переклички — ⋮ или долгое нажатие.
 *
 * ВСЁ применяется по Save: ответ/заметка — `answerFor`, статус и дата — `breaks.setBreak`
 * (затем ответы занятия перечитываются: знаменатель и дорожки зависят от статуса). Ошибка
 * остаётся в шторке. Изменение ответа сообщается перекличке (`onRollChange`) — она показывает тост.
 *
 * ⚠ Перерыв: `until` — ПОСЛЕДНИЙ день; пусто — бессрочно (так может только тренер). Сервер не
 * принимает `until` дальше 366 дней от сегодня.
 */

const ANSWERS: { id: RsvpAnswer; label: string }[] = [
  { id: 'yes', label: '✓ Going' },
  { id: 'maybe', label: '? Not sure' },
  { id: 'no', label: '✕ Can’t' },
];

const STATUSES: { id: PersonStatus; title: string; desc: React.ReactNode }[] = [
  { id: 'active', title: 'Active', desc: 'Trains with the group' },
  { id: 'break', title: '⏸ On break', desc: <>Answering &quot;Going&quot; ends it</> },
  { id: 'not_training', title: 'Doesn’t train with us', desc: 'In records, never in the pool or lanes' },
];

const MAX_DAYS_AHEAD = 366;

const CAPTION_CLS = 'text-[10px] font-extrabold uppercase tracking-[.07em]';

function PersonSheet({ sheet, onClose }: TrainingsSheetProps<'person'>) {
  const { rollIndex, upcoming, breaks, onRollChange } = useTrainingsTab();
  const roll = upcoming[rollIndex] ?? null;
  const r = roll?.rsvp ?? null;

  // Человек — по свежему ответу занятия (после правки строка в нём уже новая), иначе снимок со строки.
  const key = rsvpPersonKey(sheet.person);
  const person = r?.people?.find((p) => rsvpPersonKey(p) === key) ?? sheet.person;
  const brk = findBreak(breaks.data?.breaks, person);

  const startAnswer = person.answer;
  const startNote = person.note ?? null;
  const startStatus = statusOf(person);
  const startUntil = startStatus === 'break' ? brk?.until ?? '' : '';

  const [answer, setAnswer] = useState<RsvpAnswer | null>(startAnswer);
  const [note, setNote] = useState<RsvpNote | null>(startNote);
  const [status, setStatus] = useState<PersonStatus>(startStatus);
  const [until, setUntil] = useState(startUntil);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const today = todayInIsrael();
  const canAnswer = !!r?.can_answer;
  const lane = laneOfPerson(r?.lane_view, person);
  const sub = [
    lane ? laneCaption(lane) : null,
    person.user_id != null ? 'joined account' : 'no account — the coach answers for them',
  ].filter(Boolean).join(' · ');

  const pickAnswer = (next: RsvpAnswer) => {
    const nextAnswer = answer === next ? null : next;
    setAnswer(nextAnswer);
    if (!nextAnswer) setNote(null);
  };

  const answerChanged = answer !== startAnswer || note !== startNote;
  const statusChanged = status !== startStatus || (status === 'break' && until !== startUntil);

  const save = async () => {
    if (busy) return;
    setError(null);
    setBusy(true);

    if (answerChanged && roll) {
      const err = await roll.answerFor(person, answer, note);
      if (err) { setError(err); setBusy(false); return; }
      // Ответ «Going» снимает перерыв на сервере — если статус не правили, список перерывов устарел.
      if (answer === 'yes' && person.on_break && !statusChanged) breaks.reload();
      onRollChange?.({
        person,
        prev: { answer: startAnswer, note: startNote },
        next: { answer, note: answer ? note : null },
      });
    }

    if (statusChanged) {
      const subject = person.user_id != null ? { user_id: person.user_id } : { swimmer_id: person.swimmer_id };
      const err = await breaks.setBreak({ ...subject, status, until: status === 'break' ? (until || null) : undefined });
      if (err) { setError(err); setBusy(false); return; }
      // Ближайшее занятие перечитывает сам `breaks` (через ответы страницы); остальные — здесь.
      if (rollIndex > 0) roll?.reload();
    }

    onClose();
  };

  return (
    <DeepSheet
      title={<bdi>{person.name}</bdi>}
      sub={sub}
      tone="coach"
      busy={busy}
      onClose={onClose}
      onSubmit={() => { void save(); }}
      footer={(
        <div className="flex flex-col gap-3.5">
          <SheetError error={error} />
          <SheetFooter>
            <SheetButton kind="cancel" onClick={onClose}>Cancel</SheetButton>
            <SheetButton tone="coach" disabled={busy}>Save</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <div className="flex flex-col gap-1.5">
        <span className={CAPTION_CLS} style={{ color: 'var(--t-text-3)' }}>
          {r ? `${formatSheetDate(r.date)} · ` : ''}answer
        </span>
        <div
          className="grid grid-cols-3"
          style={{ borderRadius: 12, background: 'var(--deep-seg-track)', padding: 3 }}
          role="group"
          aria-label="Answer"
        >
          {ANSWERS.map((a) => {
            const on = answer === a.id;
            return (
              <button
                key={a.id}
                type="button"
                aria-pressed={on}
                disabled={!canAnswer}
                onClick={() => pickAnswer(a.id)}
                className="h-11 cursor-pointer border text-[13px] font-extrabold disabled:cursor-default disabled:opacity-50"
                style={{
                  borderRadius: 9,
                  ...(on ? ANSWER_PILL[a.id] : { background: 'transparent', color: 'var(--t-text-3)', borderColor: 'transparent' }),
                }}
              >
                {a.label}
              </button>
            );
          })}
        </div>
        {(answer === 'yes' || answer === 'maybe') && (
          <RsvpNotes current={note} onPick={setNote} disabled={!canAnswer} />
        )}
      </div>

      <div className="flex flex-col gap-1">
        <span className={CAPTION_CLS} style={{ color: 'var(--t-text-3)' }}>Status</span>
        <div className="flex flex-col gap-1" role="radiogroup" aria-label="Status in the group">
          {STATUSES.map((s) => {
            const on = status === s.id;
            return (
              <div
                key={s.id}
                className="flex items-center gap-2.5 border"
                style={{
                  minHeight: 48,
                  borderRadius: 12,
                  padding: '0 12px',
                  borderColor: on ? 'var(--deep-ow-border)' : 'var(--deep-card-border)',
                  background: on ? 'var(--deep-ow-soft)' : 'transparent',
                }}
              >
                <button
                  type="button"
                  role="radio"
                  aria-checked={on}
                  onClick={() => setStatus(s.id)}
                  className="flex min-h-[48px] min-w-0 flex-1 cursor-pointer items-center gap-2.5 border-0 bg-transparent p-0 text-left"
                  style={{ color: 'var(--t-text)' }}
                >
                  <span
                    aria-hidden="true"
                    className="flex flex-none items-center justify-center rounded-full"
                    style={{ width: 18, height: 18, border: `2px solid ${on ? 'var(--deep-ow)' : 'var(--deep-card-border)'}` }}
                  >
                    {on && <span className="rounded-full" style={{ width: 8, height: 8, background: 'var(--deep-ow)' }} />}
                  </span>
                  <span className="min-w-0">
                    <span className="block text-[13px] font-extrabold">{s.title}</span>
                    <span className="block text-[10.5px] font-semibold" style={{ color: 'var(--t-text-3)' }}>{s.desc}</span>
                  </span>
                </button>
                {s.id === 'break' && on && (
                  <input
                    type="date"
                    value={until}
                    min={today}
                    max={addDays(today, MAX_DAYS_AHEAD)}
                    onChange={(e) => setUntil(e.target.value)}
                    aria-label="Last day of the break (empty — no end date)"
                    title="Last day of the break. Empty — no end date."
                    className="hp-mono h-[34px] flex-none border bg-transparent px-2.5 text-[12px] font-extrabold outline-none"
                    style={{ borderRadius: 8, borderColor: 'var(--deep-ow-border)', color: 'var(--deep-ow)' }}
                  />
                )}
              </div>
            );
          })}
        </div>
      </div>
    </DeepSheet>
  );
}

export default PersonSheet;

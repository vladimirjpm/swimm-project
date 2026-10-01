import React from 'react';
import { RsvpNotes } from '../../group-pool';
import { useTrainingsTab } from '../trainings-context';
import TrainingsEmpty from '../trainings-empty';
import { TrainingsSection } from '../trainings-section';
import AnswersBlock, { AnswersCaption, useFirstUnanswered } from '../next/answers-block';
import { BreakLink, BreakRow, useHasBreakLink } from '../next/break-block';
import { FullPool, MiniPool, NextHeader, PoolFooter, useNextRsvp, YourLane } from '../next/lane-block';
import { nextSessions } from '../next/next-format';

/**
 * Вид Next УЧАСТНИКА (Т5, docs/tasks/trainings-tab-t5-sonnet.md; хендофф Trainings Tab, 2a / 3a).
 * Складывает блоки из `trainings/next/`: ответы на три занятия, «Your lane» с бассейном, перерыв.
 *
 * - телефон (2a) — три отдельных блока столбиком: «Your answers», карточка ближайшего занятия
 *   (рамка `--deep-live-border`), строка перерыва;
 * - десктоп (3a) — ОДНА карточка: шапка, ответы сеткой, заметки + «Taking a break?», полный бассейн.
 *
 * Управляющему (`manages`) секция не показывается — у него `coach-next` (Т6).
 */

const LIVE_BORDER = { borderColor: 'var(--deep-live-border)' } as const;

function MemberNextSection() {
  const { isDesktop, group } = useTrainingsTab();
  const { next } = useNextRsvp();

  // Р9: ближайшие занятия есть, но ВСЕ отменены — ближайшего живого нет, и «No weekly schedule yet» было
  // бы враньём. Показываем только список ответов: у отменённых — пилюля «Cancelled» вместо кнопок.
  if (!next && nextSessions(group).length > 0) {
    return (
      <TrainingsSection id="member-next-answers">
        <div className="mb-1"><AnswersCaption /></div>
        <AnswersBlock variant={isDesktop ? 'desktop' : 'phone'} />
      </TrainingsSection>
    );
  }

  if (!next) {
    return (
      <TrainingsSection id="member-next" bare>
        {/* На телефоне — на боковой линии сегментов (`deep-panel-row`), как лента. */}
        <div className={isDesktop ? undefined : 'deep-panel-row'}>
          <TrainingsEmpty
            glyph="◷"
            title="No weekly schedule yet"
            text="When the coach sets training days, the next training and “Are you coming?” will appear here."
          />
        </div>
      </TrainingsSection>
    );
  }

  return isDesktop ? <DesktopNext /> : <PhoneNext />;
}

/** Быстрые заметки к ответу на ближайшее занятие — только при «иду / не уверен» и если ответить можно. */
function NextNotes() {
  const { rsvp } = useTrainingsTab();
  const r = rsvp?.rsvp;
  const mine = r?.mine?.answer ?? null;
  if (!rsvp || !r || !r.can_answer || (mine !== 'yes' && mine !== 'maybe')) return null;
  return (
    <RsvpNotes
      current={r.mine?.note ?? null}
      onPick={(note) => { void rsvp.answer(mine, note); }}
    />
  );
}

function PhoneNext() {
  const { rsvp } = useTrainingsTab();
  const { next, r } = useNextRsvp();
  if (!next) return null;

  const notes = <NextNotes />;
  return (
    <>
      <TrainingsSection id="member-next-answers">
        <div className="mb-1"><AnswersCaption /></div>
        <AnswersBlock variant="phone" />
      </TrainingsSection>

      <TrainingsSection id="member-next" style={LIVE_BORDER}>
        <div className="flex flex-col gap-3">
          <NextHeader variant="phone" next={next} r={r} />
          {r ? (
            <>
              <YourLane r={r} />
              <MiniPool r={r} />
              <PoolFooter r={r} />
              {notes}
            </>
          ) : (
            <p
              className="m-0 text-[12.5px] font-bold"
              style={{ color: rsvp?.error ? 'var(--deep-danger)' : 'var(--t-text-3)' }}
              role={rsvp?.error ? 'alert' : undefined}
            >
              {rsvp?.error ?? 'Loading…'}
            </p>
          )}
        </div>
      </TrainingsSection>

      <BreakRow />
    </>
  );
}

function DesktopNext() {
  const { rsvp } = useTrainingsTab();
  const { next, r } = useNextRsvp();
  const unanswered = useFirstUnanswered();
  const hasBreakLink = useHasBreakLink();
  if (!next) return null;

  const mine = r?.mine?.answer ?? null;
  const hasNotes = !!r && r.can_answer && (mine === 'yes' || mine === 'maybe');

  return (
    <TrainingsSection id="member-next" style={{ ...LIVE_BORDER, padding: 16 }}>
      <div className="flex flex-col gap-4">
        <NextHeader variant="desktop" next={next} r={r} />

        <div className="flex flex-col gap-2">
          <AnswersCaption dot={unanswered} />
          <AnswersBlock variant="desktop" />
          {(hasNotes || hasBreakLink) && (
            <div className="flex flex-wrap items-center gap-1.5">
              <NextNotes />
              <BreakLink />
            </div>
          )}
        </div>

        {r ? <FullPool r={r} /> : (
          <p
            className="m-0 text-[12.5px] font-bold"
            style={{ color: rsvp?.error ? 'var(--deep-danger)' : 'var(--t-text-3)' }}
            role={rsvp?.error ? 'alert' : undefined}
          >
            {rsvp?.error ?? 'Loading…'}
          </p>
        )}
      </div>
    </TrainingsSection>
  );
}

export default MemberNextSection;

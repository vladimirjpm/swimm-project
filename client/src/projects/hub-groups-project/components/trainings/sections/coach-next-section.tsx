import React from 'react';
import CoachPoolBlock from '../coach/coach-pool-block';
import RollCall from '../coach/roll-call';
import SessionNav from '../coach/session-nav';
import { nextSessions } from '../next/next-format';
import { useTrainingsTab } from '../trainings-context';
import { TrainingsSection } from '../trainings-section';

/**
 * Вид Next УПРАВЛЯЮЩЕГО (Т6, docs/tasks/trainings-tab-t6-sonnet.md; хендофф Trainings Tab, 2a / 3b).
 * Складывает блоки из `trainings/coach/`: навигация ‹ › по трём ближайшим занятиям, бассейн со
 * статусом плана, перекличка (сводка, фильтр-чипы, строки со статусом по циклу, тост «Undo»).
 * Шторка человека — `sheets/person-sheet.tsx` (реестр `trainings-sheets.tsx`).
 *
 * - телефон (2a) — столбиком, gap 12, на боковой линии `deep-panel-row`;
 * - десктоп (3b) — ОДНА карточка с рамкой `--deep-live-border`: навигация, бассейн, перекличка.
 *
 * Нижней панели тренера из прототипа нет: на телефоне её роль играет панель каркаса «COACH TOOLS».
 * Выбранное занятие — `rollIndex` контекста; ответы — `upcoming[rollIndex]`.
 */

const LIVE_BORDER = { borderColor: 'var(--deep-live-border)' } as const;

function CoachNextSection() {
  const { group, isDesktop, rollIndex, upcoming } = useTrainingsTab();
  const sessions = nextSessions(group);
  const session = sessions[rollIndex];

  if (!session) {
    return (
      <TrainingsSection
        id="coach-next"
        title="Next training"
        status="empty"
        empty="No regular schedule yet — set it in Admin → Training schedule."
      />
    );
  }

  const roll = upcoming[rollIndex] ?? null;
  // Ответы пришли именно на выбранное занятие (при переходе ‹ › на секунду там ещё пусто).
  const r = roll?.rsvp && roll.rsvp.session_id === session.id ? roll.rsvp : null;

  const body = roll && r ? (
    <>
      <CoachPoolBlock session={session} roll={roll} />
      <RollCall roll={roll} r={r} />
    </>
  ) : (
    <p
      className="m-0 text-[12.5px] font-bold"
      style={{ color: roll && !roll.loading ? 'var(--deep-danger)' : 'var(--deep-text-mute)' }}
      role={roll && !roll.loading ? 'alert' : undefined}
    >
      {roll && !roll.loading ? 'Could not load the answers. Try again.' : 'Loading…'}
    </p>
  );

  if (!isDesktop) {
    return (
      <TrainingsSection id="coach-next" bare>
        <div className="deep-panel-row flex flex-col gap-3">
          <SessionNav sessions={sessions} />
          {body}
        </div>
      </TrainingsSection>
    );
  }

  return (
    <TrainingsSection id="coach-next" style={{ ...LIVE_BORDER, padding: 16 }}>
      <div className="flex flex-col gap-3.5">
        <SessionNav sessions={sessions} />
        {body}
      </div>
    </TrainingsSection>
  );
}

export default CoachNextSection;

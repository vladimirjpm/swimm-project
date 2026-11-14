import React, { useCallback, useState } from 'react';
import GroupLanes from '../../group-lanes';
import RollCall from '../coach/roll-call';
import SessionNav from '../coach/session-nav';
import { nextSessions } from '../next/next-format';
import { useTrainingsTab } from '../trainings-context';
import { TrainingsSection } from '../trainings-section';
import { SegmentBar, type SegmentOption } from '../trainings-segments';

/**
 * Рабочий стол занятия УПРАВЛЯЮЩЕГО (docs/plans/trainings-coach-flow-plan.md, CF1; макет F2):
 * перекличка и план дорожек одного занятия на одном экране. Открывается из списка занятий
 * (`coach-sessions-section.tsx`), занятие — `deskSession` контекста (`?session=<id>`).
 *
 * - шапка: «← All trainings» и существующий `SessionNav` (‹ ›, Cancel training / Restore);
 * - десктоп — две колонки: слева `RollCall`, справа `GroupLanes` на дату занятия, без своей
 *   навигации по датам (`embedded`);
 * - телефон — переключатель `People · Pool` (вид сегментов таба), под ним одна панель. Вторая
 *   панель остаётся смонтированной (`hidden`): несохранённый черновик дорожек переживает
 *   переключение.
 *
 * Смена занятия пересобирает редактор (`key` — id занятия); «Discard unsaved changes?» при уходе
 * со стола и смене занятия спрашивает контекст (`lanesDirty`).
 */

type DeskPanel = 'people' | 'pool';

const PANELS: SegmentOption<DeskPanel>[] = [
  { id: 'people', label: 'People' },
  { id: 'pool', label: 'Pool' },
];

function CoachDeskSection() {
  const {
    group, isDesktop, deskSession, closeDesk, rollIndex, upcoming, setLanesDirty, reloadLanePlans,
  } = useTrainingsTab();
  const [panel, setPanel] = useState<DeskPanel>('people');
  const sessions = nextSessions(group);

  const roll = deskSession ? upcoming[rollIndex] ?? null : null;
  // Ответы пришли именно на выбранное занятие (при переходе ‹ › на секунду там ещё пусто).
  const r = roll?.rsvp && deskSession && roll.rsvp.session_id === deskSession.id ? roll.rsvp : null;
  const reloadRoll = roll?.reload;
  // План создан / опубликован / удалён: статус в списке занятий и вид по дорожкам ответов меняются.
  const plansChanged = useCallback(() => {
    reloadLanePlans();
    reloadRoll?.();
  }, [reloadLanePlans, reloadRoll]);

  if (!deskSession) return null;

  const people = roll && r ? (
    <RollCall roll={roll} r={r} narrow />
  ) : (
    <p
      className="m-0 text-[12.5px] font-bold"
      style={{ color: roll && !roll.loading ? 'var(--deep-danger)' : 'var(--deep-text-mute)' }}
      role={roll && !roll.loading ? 'alert' : undefined}
    >
      {roll && !roll.loading ? 'Could not load the answers. Try again.' : 'Loading…'}
    </p>
  );

  const pool = (
    <GroupLanes
      key={deskSession.id}
      groupId={group.id}
      manages
      desktop={isDesktop}
      initialDate={deskSession.date}
      embedded
      onDirtyChange={setLanesDirty}
      onPlansChanged={plansChanged}
    />
  );

  const header = (
    <div className="flex flex-col gap-2.5">
      <div>
        <button
          type="button"
          onClick={closeDesk}
          className="cursor-pointer border-0 bg-transparent p-0 text-[13px] font-extrabold"
          style={{ color: 'var(--t-text-3)' }}
        >
          ← All trainings
        </button>
      </div>
      <SessionNav sessions={sessions} />
    </div>
  );

  if (!isDesktop) {
    return (
      <TrainingsSection id="coach-desk" bare>
        <div className="deep-panel-row flex flex-col gap-3">
          {header}
          <SegmentBar label="Training panel" options={PANELS} value={panel} onChange={setPanel} coach />
          <div hidden={panel !== 'people'}>{people}</div>
          <div hidden={panel !== 'pool'}>{pool}</div>
        </div>
      </TrainingsSection>
    );
  }

  return (
    <TrainingsSection id="coach-desk" bare>
      <div className="flex flex-col gap-3.5">
        {header}
        <div className="grid items-start gap-3.5" style={{ gridTemplateColumns: 'minmax(0, 320px) minmax(0, 1fr)' }}>
          <section className="deep-card mb-0 min-w-0">{people}</section>
          <section className="deep-card mb-0 min-w-0">{pool}</section>
        </div>
      </div>
    </TrainingsSection>
  );
}

export default CoachDeskSection;

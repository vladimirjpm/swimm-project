import React, { useCallback, useState } from 'react';
import { useMediaQuery } from '../../../../../hooks/useMediaQuery';
import { DEEP_DOCK_QUERY, useDockSlot } from '../../../../components/deep/dock-slot';
import GroupLanes from '../../group-lanes';
import NeedsYou from '../coach/needs-you';
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
 * Статус плана и единственный Publish (CF3): строка `LanePlanStatus`, которую редактор кладёт
 * порталом в узел этой секции — в шапке стола справа; на телефоне, где инструменты каркаса
 * закреплены панелью у низа экрана, — в слот ЭТОЙ ЖЕ панели (`components/deep/dock-slot.ts`).
 *
 * Над бассейном — полоса «Needs you» (CF4, `coach/needs-you.tsx`): идущие без уровня, дорожки сверх
 * лимита, заявки и аккаунты без связи с пловцом; пусто — полосы нет.
 *
 * Смена занятия пересобирает редактор (`key` — id занятия). Правки плана сохраняются сами;
 * «Discard unsaved changes?» при уходе со стола и смене занятия контекст спрашивает, только когда
 * запись не удалась (`lanesDirty`).
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
  // Куда встаёт строка статуса плана: слот нижней панели (телефон) или узел в шапке стола.
  const docked = useMediaQuery(DEEP_DOCK_QUERY);
  const dockSlot = useDockSlot();
  const [headerHost, setHeaderHost] = useState<HTMLElement | null>(null);
  const statusHost = docked ? dockSlot : headerHost;
  const showPool = useCallback(() => setPanel('pool'), []);

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
    <div className="flex min-w-0 flex-col gap-3">
    {r && <NeedsYou r={r} />}
    <GroupLanes
      key={deskSession.id}
      groupId={group.id}
      manages
      desktop={isDesktop}
      initialDate={deskSession.date}
      embedded
      statusHost={statusHost}
      onFocusPool={showPool}
      onDirtyChange={setLanesDirty}
      onPlansChanged={plansChanged}
    />
    </div>
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
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div className="min-w-0 flex-1 basis-[280px]"><SessionNav sessions={sessions} /></div>
        {/* Сюда редактор дорожек выносит статус плана и Publish (портал); на телефоне — в нижнюю панель. */}
        {!docked && <div ref={setHeaderHost} className="min-w-0 flex-none" />}
      </div>
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

import React from 'react';
import type { TrainingNeedPerson, TrainingRsvp, TrainingRsvpPerson } from '../../../types';
import { useTrainingsTab } from '../trainings-context';
import { needsCount, needsLabel } from './needs-format';

/**
 * Полоса «Needs you» над бассейном рабочего стола (docs/plans/trainings-coach-flow-plan.md, CF4;
 * макеты F2 / F4): всё, что ждёт решения тренера на это занятие. Пусто — полосы нет.
 *
 * Строки и куда ведут:
 * - идущий пловец без уровня → карточка человека (там уровень; по Save он встанет на дорожку);
 * - дорожка сверх «Max per lane» → эта дорожка в редакторе;
 * - заявка «<имя> wants to join» и аккаунт без связи с пловцом → шторка «Who is this?».
 *
 * Строки считает сервер (`rsvp.needs`), полоса ничего не грузит. Цвета — золото предупреждения
 * (`--deep-gold-*`), как у Unassigned редактора.
 */

const rowBtn = 'flex min-h-[40px] w-full cursor-pointer items-center gap-2 rounded-[10px] border-0 bg-transparent px-2 text-left text-[12.5px] font-bold';

function NeedsYou({ r }: { r: TrainingRsvp }) {
  const { openSheet, sendPoolCommand } = useTrainingsTab();
  const needs = r.needs;
  const count = needsCount(needs);
  if (!needs || count === 0) return null;

  const personOf = (n: TrainingNeedPerson): TrainingRsvpPerson | null =>
    r.people?.find((p) => (n.user_id != null ? p.user_id === n.user_id : p.user_id == null && p.swimmer_id === n.swimmer_id)) ?? null;

  const row = (key: string, text: React.ReactNode, action: string, onClick: () => void) => (
    <li key={key}>
      <button type="button" className={rowBtn} style={{ color: 'var(--t-text)' }} onClick={onClick}>
        <span aria-hidden="true" className="inline-block flex-none rounded-full" style={{ width: 7, height: 7, background: 'var(--deep-gold-bar)' }} />
        <span className="min-w-0 flex-1 truncate">{text}</span>
        <span className="flex-none text-[11.5px] font-extrabold" style={{ color: 'var(--deep-gold)' }}>{action} ›</span>
      </button>
    </li>
  );

  return (
    <section
      aria-label="Needs you"
      className="flex flex-col gap-1 rounded-[14px] border p-2"
      style={{ borderColor: 'var(--deep-gold-border)', background: 'var(--deep-gold-soft)' }}
    >
      <div className="px-2 pt-0.5 text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--deep-gold)' }}>
        {needsLabel(count)}
      </div>
      <ul className="m-0 flex list-none flex-col p-0">
        {needs.no_level.map((n) => {
          const person = personOf(n);
          return row(`nl-${n.user_id ?? 's'}-${n.swimmer_id}`, <><bdi>{n.name}</bdi> is going · no level</>, 'Set level',
            () => { if (person) openSheet({ kind: 'person', person }); });
        })}
        {needs.over_lanes.map((l) => row(`ol-${l.lane_no}`, `Lane ${l.lane_no}: ${l.count} of ${l.max} max`, 'Show',
          () => sendPoolCommand({ kind: 'show-lane', laneNo: l.lane_no })))}
        {needs.requests.map((n) => row(`rq-${n.user_id}`, <><bdi>{n.name}</bdi> wants to join</>, 'Who is this?',
          () => openSheet({ kind: 'who-is-this', who: { userId: n.user_id!, name: n.name, request: true, suggestedSwimmerId: n.suggested_swimmer_id } })))}
        {needs.unlinked.map((n) => row(`ul-${n.user_id}`, <><bdi>{n.name}</bdi> · account not linked to a swimmer</>, 'Who is this?',
          () => openSheet({ kind: 'who-is-this', who: { userId: n.user_id!, name: n.name, request: false, suggestedSwimmerId: n.suggested_swimmer_id } })))}
      </ul>
    </section>
  );
}

export default NeedsYou;

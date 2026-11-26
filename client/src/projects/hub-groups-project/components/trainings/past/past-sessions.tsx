import React, { useState } from 'react';
import type { PastTraining } from '../../../types';
import PlanPill from '../coach/plan-pill';
import { timeRangeLabel } from '../next/next-format';
import { CancelledPill, SessionTag } from '../next/session-tag';
import { formatSheetDate } from '../sheets/link-helpers';
import { useTrainingsTab } from '../trainings-context';

/**
 * Список «Past» (CF9, docs/plans/trainings-coach-flow-plan.md): прошедшие занятия, сначала новые.
 * Open открывает занятие по `?session=<id>` (`openDesk`).
 *
 * - Тренер — под карточками Upcoming: занятия с ответами или планом, строкой ниже — счётчики ответов
 *   и статус плана; Open — его рабочий стол. Править прошедшее сервер даёт неделю (`ManagerDaysBack`).
 * - Участник (решение Влада 26.11.2026; и режим «👁 View» тренера) — в виде Next: только занятия с
 *   опубликованным планом, без счётчиков и без статуса плана (он всегда «published»); Open — вид
 *   «только смотреть» (`sections/member-past-session-section.tsx`).
 *
 * Пусто или список не пришёл — секции нет (у новой группы прошлого ещё нет).
 */

/** Сколько строк видно сразу; остальные — по «Show all». */
const SHOWN = 5;

function answersLabel(p: PastTraining): React.ReactNode {
  const yes = p.yes ?? 0;
  const maybe = p.maybe ?? 0;
  const no = p.no ?? 0;
  if (yes + maybe + no === 0) return <span style={{ color: 'var(--t-text-3)' }}>No answers</span>;
  return (
    <span className="hp-mono">
      <span style={{ color: 'var(--deep-live)' }}>{yes}</span> going
      {maybe > 0 && <> · <span style={{ color: 'var(--deep-gold)' }}>{maybe}</span> maybe</>}
      {no > 0 && <> · <span style={{ color: 'var(--deep-danger)' }}>{no}</span> out</>}
    </span>
  );
}

function PastRow({ item }: { item: PastTraining }) {
  const { lanePlans, openDesk, manages } = useTrainingsTab();
  const s = item.session;
  const plan = lanePlans?.find((p) => p.session_id === s.id) ?? null;

  return (
    <li
      className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 border-t py-2.5 first:border-t-0"
      style={{ borderColor: 'var(--deep-card-border)' }}
    >
      <div className="min-w-0 flex-1 basis-[200px]">
        <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5">
          <span className="text-[14px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{formatSheetDate(s.date)}</span>
          <span className="hp-mono text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>{timeRangeLabel(s.start, s.end)}</span>
          <SessionTag session={s} className="max-w-full" />
          {s.cancelled && <CancelledPill />}
        </div>
        {manages && (
          <div className="mt-0.5 flex flex-wrap items-center gap-x-3 text-[12px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
            {answersLabel(item)}
            {lanePlans != null && <PlanPill state={plan ? plan.status : 'none'} />}
          </div>
        )}
      </div>
      {s.id && (
        <button
          type="button"
          onClick={() => openDesk(s.id!)}
          className="h-[34px] flex-none cursor-pointer rounded-[10px] border px-4 text-[12.5px] font-extrabold"
          style={{ borderColor: 'var(--deep-ow-border)', background: 'var(--deep-ow-chip)', color: 'var(--deep-ow)' }}
        >
          Open
        </button>
      )}
    </li>
  );
}

/** `className` — отступ сверху: у тренера список стоит под карточками (`mt-4`), у участника — секцией. */
function PastSessions({ className = 'mt-4' }: { className?: string }) {
  const { past, lanePreview, lanePlans } = useTrainingsTab();
  const [all, setAll] = useState(false);
  // «👁 View» тренера: сервер отдал ему свой список (черновики, занятия только с ответами) — оставляем
  // то, что увидит участник: занятия с опубликованным планом.
  const list = lanePreview
    ? past.list?.filter((p) => lanePlans?.some((lp) => lp.session_id === p.session.id && lp.status === 'published')) ?? null
    : past.list;
  if (!list || list.length === 0) return null;
  const shown = all ? list : list.slice(0, SHOWN);

  return (
    <div className={className} data-section="past-sessions">
      <div className="mb-2 text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
        Past
      </div>
      <section className="deep-card mb-0 min-w-0 py-1">
        <ul className="m-0 list-none p-0">
          {shown.map((p) => <PastRow key={p.session.id ?? `${p.session.date}-${p.session.start}`} item={p} />)}
        </ul>
        {list.length > SHOWN && (
          <button
            type="button"
            onClick={() => setAll((v) => !v)}
            className="mb-1.5 mt-0.5 cursor-pointer border-0 bg-transparent p-0 text-[12.5px] font-extrabold"
            style={{ color: 'var(--deep-ow)' }}
          >
            {all ? 'Show fewer' : `Show all ${list.length}`}
          </button>
        )}
      </section>
    </div>
  );
}

export default PastSessions;

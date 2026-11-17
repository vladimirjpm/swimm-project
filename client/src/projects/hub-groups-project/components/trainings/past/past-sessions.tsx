import React, { useState } from 'react';
import type { PastTraining } from '../../../types';
import PlanPill from '../coach/plan-pill';
import { timeRangeLabel } from '../next/next-format';
import { CancelledPill, SessionTag } from '../next/session-tag';
import { formatSheetDate } from '../sheets/link-helpers';
import { useTrainingsTab } from '../trainings-context';

/**
 * Список «Past» тренера под карточками Upcoming (CF9, docs/plans/trainings-coach-flow-plan.md):
 * прошедшие занятия, по которым есть ответы или план дорожек, сначала новые. Open открывает тот же
 * рабочий стол занятия (`openDesk` → `?session=<id>`), что и у ближайших: перекличка, дорожки,
 * workout. Править прошедшее сервер даёт неделю (`ManagerDaysBack`), смотреть — дольше.
 *
 * Пусто или список не пришёл — секции нет (у новой группы прошлого ещё нет).
 */

/** Сколько строк видно сразу; остальные — по «Show all». */
const SHOWN = 5;

function answersLabel(p: PastTraining): React.ReactNode {
  if (p.yes + p.maybe + p.no === 0) return <span style={{ color: 'var(--t-text-3)' }}>No answers</span>;
  return (
    <span className="hp-mono">
      <span style={{ color: 'var(--deep-live)' }}>{p.yes}</span> going
      {p.maybe > 0 && <> · <span style={{ color: 'var(--deep-gold)' }}>{p.maybe}</span> maybe</>}
      {p.no > 0 && <> · <span style={{ color: 'var(--deep-danger)' }}>{p.no}</span> out</>}
    </span>
  );
}

function PastRow({ item }: { item: PastTraining }) {
  const { lanePlans, openDesk } = useTrainingsTab();
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
        <div className="mt-0.5 flex flex-wrap items-center gap-x-3 text-[12px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
          {answersLabel(item)}
          {lanePlans != null && <PlanPill state={plan ? plan.status : 'none'} />}
        </div>
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

function PastSessions() {
  const { past } = useTrainingsTab();
  const [all, setAll] = useState(false);
  const list = past.list;
  if (!list || list.length === 0) return null;
  const shown = all ? list : list.slice(0, SHOWN);

  return (
    <div className="mt-4">
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

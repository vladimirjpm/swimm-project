import React, { useState } from 'react';
import type { LanePlanEvent } from '../../../types';
import { changesCount, changesLabel, eventText, eventTime } from './plan-events-format';

/**
 * Блок «Since publishing» над бассейном рабочего стола (docs/plans/trainings-coach-flow-plan.md, CF8;
 * макет F7-mobile): что изменилось в опубликованном плане с момента, когда участникам сообщили —
 * время и одна строка на событие, новые сверху; число изменений рядом с заголовком. Только тренеру
 * (ленту отдаёт сервер только управляющему). Черновик — блока нет.
 *
 * Строк много — видно последние {@link SHOWN}, остальные по «Show all». Вид — как у соседней полосы
 * Workout (рамка и подложка `--deep-ow*`), строки — как в «Needs you».
 */

const SHOWN = 5;

function SincePublishing({ events }: { events: readonly LanePlanEvent[] }) {
  const [all, setAll] = useState(false);
  const count = changesCount(events);
  const shown = all ? events : events.slice(0, SHOWN);

  return (
    <section
      aria-label="Since publishing"
      className="flex min-w-0 flex-col gap-1 rounded-[12px] px-3 py-2.5"
      style={{ border: '1px solid var(--deep-ow-border)', background: 'var(--deep-ow-chip)' }}
    >
      <div className="flex items-center gap-2">
        <span className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--deep-ow)' }}>
          Since publishing
        </span>
        <span
          className="ml-auto flex-none rounded-full px-2 py-[2px] text-[11px] font-extrabold"
          style={{ background: count > 0 ? 'var(--deep-ow)' : 'transparent', color: count > 0 ? 'var(--deep-accent-ink)' : 'var(--t-text-3)' }}
        >
          {changesLabel(count)}
        </span>
      </div>
      <ul className="m-0 flex list-none flex-col p-0">
        {shown.map((e) => {
          const t = eventText(e);
          return (
            <li
              key={e.id}
              className="flex min-w-0 items-baseline gap-3 border-t py-1.5 text-[12.5px] first:border-t-0"
              style={{ borderColor: 'var(--deep-card-border)' }}
            >
              <span className="hp-mono w-[62px] flex-none text-[11.5px] font-extrabold" style={{ color: 'var(--t-text-3)' }}>
                {eventTime(e.at)}
              </span>
              <span className="min-w-0 flex-1 font-bold" style={{ color: 'var(--deep-text)' }}>
                {t.before}{t.name && <bdi>{t.name}</bdi>}{t.after}
              </span>
            </li>
          );
        })}
      </ul>
      {events.length > SHOWN && (
        <button
          type="button"
          onClick={() => setAll((v) => !v)}
          className="cursor-pointer self-start border-0 bg-transparent p-0 text-[12px] font-extrabold"
          style={{ color: 'var(--deep-ow)' }}
        >
          {all ? 'Show less' : `Show all ${events.length}`}
        </button>
      )}
    </section>
  );
}

export default SincePublishing;

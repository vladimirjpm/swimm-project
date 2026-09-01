import React from 'react';
import { routes } from '../../../utils/routes';
import { useUpcomingStarts } from '../../results-main-project/components/start-list/use-start-list';
import { formatApproxTime, swimLabel } from '../../results-main-project/components/start-list/start-list-helpers';

/**
 * Блок «Upcoming starts» на странице пловца (С8.3, start-list-ui-sonnet.md).
 * Пусто → блок не рисуем вовсе (решение проекта: «нечего показывать — не показываем»,
 * та же логика, что у секции Upcoming на /competitions).
 */
export default function SwimmerUpcomingStarts({ swimmerId }: { swimmerId: number }) {
  const { data } = useUpcomingStarts([swimmerId]);
  if (!data || data.length === 0) return null;

  return (
    <div className="deep-folder mb-4 p-3">
      <div className="mb-2 text-sm font-black">Upcoming starts</div>
      <div className="flex flex-col gap-1.5">
        {data.map((s) => (
          <a
            key={s.id}
            href={`${routes.competitionUpcoming(s.org_comp_id)}?tab=startlist&swimmer=${swimmerId}`}
            className="flex items-center justify-between gap-3 rounded-[10px] px-2.5 py-1.5 text-inherit no-underline"
            style={{ background: 'var(--theme-mode-surface-alt)' }}
          >
            <span className="min-w-0 flex-1 truncate text-[13px] font-semibold">{s.comp_name}</span>
            <span className="shrink-0 text-[12px] opacity-70">
              {formatApproxTime(s.heat_start_at)} · {swimLabel(s.distance, s.style_name)}
            </span>
          </a>
        ))}
      </div>
    </div>
  );
}

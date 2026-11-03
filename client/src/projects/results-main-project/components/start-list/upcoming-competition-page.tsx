import React from 'react';
import AppTopbar from '../../../components/app-topbar/app-topbar';
import { PAGE_CONTAINER } from '../../../../utils/layout';
import { useTheme } from '../../../../hooks/useTheme';
import { useMode } from '../../../../hooks/useMode';
import StartListTab from './start-list-tab';
import { useStartListProgramme } from './use-start-list';
import UI_CompetitionName from '../../../components/mix/competition-name/competition-name';

const PULLED_FORMAT = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
});

/** «Start list updated 3 Nov 2026, 10:11» — время зрителя, как у «Updated HH:MM» в зумах. */
function pulledLabel(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : `Start list updated ${PULLED_FORMAT.format(d)}`;
}

/**
 * Страница предстоящего соревнования — `/competitions/upcoming/{orgCompId}` (С7б, шаг 3).
 * Тот же вход `results_main.html`, что и обычная карточка, но упрощённый: соревнование ещё
 * не проходило, своей строки в `Competitions` у него нет, поэтому Overview/Swims/Clubs/Records
 * рисовать нечем — только Start list, и он открыт сразу, без выбора таба.
 *
 * Данные берутся по `orgCompId` из ПУТИ, а не из overview (overview тут нет).
 */
export default function UpcomingCompetitionPage({ orgCompId }: { orgCompId: number }) {
  useTheme();
  useMode();
  const { data } = useStartListProgramme(orgCompId);

  return (
    // Цвет текста — парным токеном фона (правило парных токенов, client/CLAUDE.md):
    // без него в dark заголовок и подпись оставались тёмными по тёмному.
    <div style={{ minHeight: '100vh', background: 'var(--theme-mode-page-bg)', color: 'var(--theme-mode-text)' }}>
      <AppTopbar />
      <div className={PAGE_CONTAINER}>
        <div className="mt-4 mb-3">
          <h1 className="text-xl font-black">
            {data?.comp_name
              ? <UI_CompetitionName name={data.comp_name} isChampionship={data.is_championship} />
              : 'Upcoming competition'}
          </h1>
          {/* Дата последнего забора у источника: организатор правит программу до последнего
              дня, и «Updated HH:MM» в зумах без даты не говорит, вчерашний это посев или нет. */}
          {pulledLabel(data?.updated_at ?? null) && (
            // Акцент КАК ТЕКСТ на поверхности режима — accent-on-surface, не accent-text
            // (тот — для текста на фоне --theme-primary), см. client/CLAUDE.md.
            <p className="text-xs font-bold" style={{ color: 'var(--theme-mode-accent-on-surface)' }}>
              {pulledLabel(data?.updated_at ?? null)}
            </p>
          )}
          <p className="text-xs" style={{ color: 'var(--theme-mode-text-secondary)' }}>
            This meet has not started yet — results are not available. Showing the start list only.
          </p>
        </div>
        <StartListTab orgCompId={orgCompId} />
      </div>
    </div>
  );
}

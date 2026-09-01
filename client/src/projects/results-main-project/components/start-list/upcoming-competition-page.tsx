import React from 'react';
import AppTopbar from '../../../components/app-topbar/app-topbar';
import { PAGE_CONTAINER } from '../../../../utils/layout';
import { useTheme } from '../../../../hooks/useTheme';
import { useMode } from '../../../../hooks/useMode';
import StartListTab from './start-list-tab';
import { useStartListProgramme } from './use-start-list';

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
          <h1 className="text-xl font-black">{data?.comp_name ?? 'Upcoming competition'}</h1>
          <p className="text-xs" style={{ color: 'var(--theme-mode-text-secondary)' }}>
            This meet has not started yet — results are not available. Showing the start list only.
          </p>
        </div>
        <StartListTab orgCompId={orgCompId} />
      </div>
    </div>
  );
}

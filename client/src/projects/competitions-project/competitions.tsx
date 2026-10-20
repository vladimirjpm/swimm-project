import React from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import AppTopbar from '../components/app-topbar/app-topbar';
import RecordTicker from '../home-project/components/record-ticker';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import CompetitionCalendar from './calendar/competition-calendar';
import CompetitionsHero from './components/competitions-hero';
import FavoritesUpcoming from './components/favorites-upcoming';
import RecordsStandards from './components/records-standards';

/*
 * Страница /competitions — только раскладка: оболочка deep-темы и колонка 1180, внутри
 * независимые блоки сверху вниз. Блок не знает о соседях и берёт данные сам, поэтому любой
 * можно убрать, переставить или взять на другую страницу:
 *   CompetitionsHero    — заголовок и сезон            (components/competitions-hero.tsx)
 *   CompetitionCalendar — календарь сезона             (calendar/, карта его блоков — в competition-calendar.tsx)
 *   FavoritesUpcoming   — старты избранных пловцов     (components/favorites-upcoming.tsx)
 *   RecordsStandards    — плитки рекордов и нормативов (components/records-standards.tsx)
 */
function Competitions() {
  const deep = useDeepThemeClass();

  return (
    <div className={`home-page hp-bg-vivid ${deep} relative min-h-screen overflow-x-clip pb-[96px] text-[var(--t-text)]`}>
      <div className="hp-shimmer" aria-hidden="true" />

      <AppTopbar active="competitions" />

      {/* Весь контент — колонка 1180 по центру (хендофф competitions_home-2): шире ячейки и кубы
          растягиваются, и мелкий текст в них теряется. Отступ ≥24px — от обёртки, как у топбара. */}
      <div className="relative px-4 lg:px-6">
        <div className="content-box-xl pt-5 lg:pt-4">
          <CompetitionsHero />
          <div className="mt-5 lg:mt-[14px]">
            <CompetitionCalendar />
          </div>
          <FavoritesUpcoming />
          <RecordsStandards />
        </div>
      </div>

      <RecordTicker />
      {/* Переключатель тем. На витрине он единственный способ сменить режим: топбар его
          не носит, а внутренние экраны (results, клуб, пловец) — уже другая дверь.
          Отступ снизу считает `home.css` от высоты ленты рекордов. */}
      <UI_ModeToggle />
    </div>
  );
}

export default Competitions;

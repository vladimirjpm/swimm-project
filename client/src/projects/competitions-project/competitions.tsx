import React from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import AppTopbar from '../components/app-topbar/app-topbar';
import RecordTicker from '../home-project/components/record-ticker';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { HOME_REGION_LABEL } from '../../utils/constants/home-region';
import { routes } from '../../utils/routes';
import { seasonLabel, seasonStartYear } from '../../utils/helpers/season-helper';
import { useUpcomingStarts } from '../results-main-project/components/start-list/use-start-list';
import { useAuth } from '../../hooks/useAuth';
import { useFavorites } from '../../hooks/useFavorites';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import { formatApproxTime, swimLabel } from '../results-main-project/components/start-list/start-list-helpers';
import CompetitionCalendar from './calendar/competition-calendar';
import RecordsStandards from './calendar/records-standards';

// Избранные пловцы залогиненного — ближайшие старты (С8.4). Живёт на /competitions,
// а не на главной (вне скоупа — В9 говорит только про общий список соревнований).
function FavoritesUpcomingSection() {
  const auth = useAuth();
  const { favoriteSwimmerIds } = useFavorites();
  const swimmerIds = React.useMemo(() => Array.from(favoriteSwimmerIds), [favoriteSwimmerIds]);
  const { data } = useUpcomingStarts(auth.isAuthenticated ? swimmerIds : []);

  if (!auth.isAuthenticated || !data || data.length === 0) return null;

  return (
    <section className="relative pt-[38px]" aria-label="Your favorites — upcoming starts">
      <h2 className="mb-[14px] text-[18px] font-black tracking-[-0.02em] text-[var(--t-text)]">
        Your favorites — upcoming starts
      </h2>
      <div className="flex flex-col gap-2">
        {data.map((s) => (
          <a
            key={s.id}
            href={`${routes.competitionUpcoming(s.org_comp_id)}?tab=startlist&swimmer=${s.swimmer_id}`}
            className="hp-card-std flex items-center justify-between gap-3 rounded-[12px] border border-[var(--t-border)] px-4 py-2.5 text-inherit no-underline"
          >
            <span className="text-[13px] font-bold">{s.swimmer_name}</span>
            <span className="text-[12px] text-[var(--t-text-2)]">
              {formatApproxTime(s.heat_start_at)} · {swimLabel(s.distance, s.style_name)}
            </span>
          </a>
        ))}
      </div>
    </section>
  );
}

function Competitions() {
  const deep = useDeepThemeClass();

  return (
    <div className={`home-page ${deep} relative min-h-screen overflow-x-clip pb-[96px] text-[var(--t-text)]`}>
      <div className="hp-shimmer" aria-hidden="true" />

      <AppTopbar active="competitions" />

      {/* Весь контент — колонка 1180 по центру (хендофф competitions_home-2, «Layout на широких
          экранах»): на всю ширину ячейки календаря на 1980 растягивались до 280px, и текст 11px в
          них терялся. Отступ ≥24px — от обёртки, как у топбара (`PAGE_CONTAINER`). */}
      <div className="relative px-4 lg:px-6">
        <div className="content-box-xl">
          <section className="relative pt-[26px] lg:pt-[46px]">
            <p className="mb-[18px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[var(--t-accent-text)] lg:text-[15px] lg:tracking-[0.3em]">
              {`Season ${seasonLabel(seasonStartYear())} · ${HOME_REGION_LABEL}`}
            </p>
            <h1 className="text-[44px] font-black leading-[0.92] tracking-[-0.045em] text-[var(--t-text)] lg:text-[88px] lg:leading-[0.9]">
              Competitions
            </h1>
            <p className="mt-5 max-w-[560px] text-[14.5px] leading-[1.55] text-[var(--t-text-2)] lg:text-[18px] lg:leading-[1.6]">
              Pick a meet — results are live from the pool.
            </p>
          </section>

          <CompetitionCalendar />

          <FavoritesUpcomingSection />

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

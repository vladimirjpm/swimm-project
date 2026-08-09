import React, { useMemo, useState } from 'react';
import '../../index.css';
import './club-theme.css';
import { useMode } from '../../hooks/useMode';
import { useClubOverview, type ClubScope } from '../../hooks/useClubOverview';
import { parseRoute } from '../../utils/routes';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import ClubHero from './components/club-hero';
import ClubFilters, { SHOW_GROUP_TILES } from './components/club-filters';
import ClubSeasonCarousel from './components/club-season-carousel';
import ClubGrid from './components/club-grid';
import ClubStandings from './components/club-standings';
import ClubTimeline from './components/club-timeline';
import ClubTopSwimmers from './components/club-top-swimmers';
import ClubSwimmers from './components/club-swimmers';
import ClubRecords from './components/club-records';
import ClubRecordWall from './components/club-record-wall';
import ClubCoaches from './components/club-coaches';
import ClubTabs, { isClubTab, type ClubTab } from './components/club-tabs';
import ClubSoonCard from './components/club-soon-card';

/**
 * Страница клуба (Фаза 10, план docs/plans/club-page-plan.md, этап K5).
 *
 * Устройство: Hero → ОДИН глобальный блок фильтров (сезон + зачётная группа) → карточки.
 * Каждая карточка читает выбранный скоуп и своих фильтров не заводит — единственное
 * исключение будет у стены рекордов (переключатель бассейна: 25м и 50м несравнимы).
 *
 * Тема — токены `--deep-*` из дизайн-хендоффа (club-theme.css): класс .theme-deep или
 * .theme-deep-light навешивается по глобальному режиму light/dark.
 */
function ClubProject() {
  const clubId = useMemo<number | null>(() => parseRoute().clubId, []);
  const { mode } = useMode();

  const [scope, setScope] = useState<ClubScope>({
    season: null,
    group: null,
    standingCompetitionId: null,
  });

  // Активный таб — вид, поэтому живёт в query (?tab=), а не в пути: правило
  // routes.ts «в путь только идентичность ресурса». Диплинк на таб работает сразу.
  const [tab, setTab] = useState<ClubTab>(() => {
    const t = new URLSearchParams(window.location.search).get('tab');
    return isClubTab(t) ? t : 'season';
  });

  const handleTab = (next: ClubTab) => {
    setTab(next);
    const url = new URL(window.location.href);
    if (next === 'season') url.searchParams.delete('tab');
    else url.searchParams.set('tab', next);
    window.history.replaceState(null, '', url.toString());
  };

  const { data, loading, error } = useClubOverview(clubId, scope);

  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';
  const scopeLabel =
    scope.season == null
      ? 'all seasons'
      : data?.seasons.find((s) => s.season === scope.season)?.label ?? String(scope.season);

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar />

      <main className="mx-auto max-w-[1180px] px-4 py-6" style={{ color: 'var(--deep-text)' }}>
        <div className="mb-4 flex justify-end">
          <UI_ModeToggle />
        </div>

        {clubId == null && <Notice>Club not found</Notice>}
        {/* Плашка загрузки — ТОЛЬКО на первом заходе, пока данных нет вовсе.
            При смене сезона/зачёта данные остаются на экране и обновляются на месте:
            иначе плашка вставлялась над контентом и вся страница прыгала вниз-вверх
            на каждый клик по фильтру. */}
        {clubId != null && loading && !data && <Notice>Loading…</Notice>}
        {clubId != null && !loading && error === 'not-found' && <Notice>Club not found</Notice>}
        {clubId != null && !loading && error && error !== 'not-found' && (
          <Notice>Could not load this club</Notice>
        )}

        {data && (
          <>
            <ClubHero club={data.club} kpi={data.kpi} scopeLabel={scopeLabel} />

            {/* Полоса сезонов стоит МЕЖДУ шапкой и табами и действует на всю страницу
                (handoff filter-season 4c). Карточки, которым сезон не положен —
                Record wall (у рекорда сезона нет) и Season best (сознательно живёт
                текущим сезоном) — его по-прежнему не слушают. */}
            <ClubSeasonCarousel
              seasons={data.seasons}
              season={scope.season}
              // Смена сезона сбрасывает раскрытый зачёт: он мог принадлежать другому.
              onSeason={(season) => setScope((s) => ({ ...s, season, standingCompetitionId: null }))}
            />

            {/* «Папка» (TABS.md 3a folder-tab): плитки и панель контента — один корпус,
                активная плитка срастается с панелью. Поэтому они в общей обёртке, а не
                двумя блоками с отступом между ними. */}
            <div className="deep-folder mb-4">
            <ClubTabs
              active={tab}
              onSelect={handleTab}
              subs={{
                season: 'grid · standings',
                // Число рекордов знает сама карточка (свой эндпоинт с фильтром пула),
                // страница его не грузит — цифру не выдумываем.
                records: 'wall · best season',
                swimmers: `${data.club.swimmer_count} · coaches`,
                media: 'soon',
                history: `${data.timeline.length} competitions`,
              }}
            />

            <div className="deep-tabs-panel">
            {tab === 'season' && (
              <>
                {/* Плитки групп временно скрыты (SHOW_GROUP_TILES) — карточку фильтра
                    не рендерим совсем, чтобы не оставлять пустую рамку. */}
                {SHOW_GROUP_TILES && (
                  <ClubFilters
                    groups={data.groups}
                    group={scope.group}
                    // Смена скоупа сбрасывает раскрытый зачёт: он мог принадлежать другому сезону.
                    onGroup={(group) => setScope((s) => ({ ...s, group, standingCompetitionId: null }))}
                  />
                )}

                {/* Грид и таблица зачёта — пара: клик по линии слева меняет таблицу справа,
                    поэтому на десктопе они стоят рядом по половине ширины.
                    Порог 960px, а не стандартный lg (1024): строке грида нужно ~525px
                    (кружок группы + название + две линии чемпионатов по 10 сегментов),
                    таблице ~380px — вдвоём они помещаются уже с 960. Уже — одна колонка. */}
                <div className="mb-4 grid grid-cols-1 items-start gap-4 min-[960px]:grid-cols-2">
                  <ClubGrid
                    grid={data.grid}
                    currentSeason={scope.season}
                    selectedCompetitionId={data.standings?.competition_id ?? null}
                    onPickStanding={(competitionId) =>
                      setScope((s) => ({ ...s, standingCompetitionId: competitionId }))
                    }
                  />

                  <ClubStandings standings={data.standings} />
                </div>
              </>
            )}

            {/* Ростер и рекорды — отдельные пагинируемые эндпоинты (K4.2), им нужен
                уже-резолвленный clubId (гарантирован здесь: data загрузился только для
                непустого clubId). */}
            {tab === 'records' && clubId != null && (
              <>
                {/* Времена парой: Season best — наши протоколы за ТЕКУЩИЙ сезон по возрастным
                    ступеням (глобальный фильтр сезона не слушает — см. club-records.tsx),
                    Record wall — официальный справочник рекордов (сезона у него нет).
                    Данные разные, форма общая — club-record-card.tsx. */}
                <div className="mb-4 grid grid-cols-1 items-start gap-4 min-[960px]:grid-cols-2">
                  <ClubRecordWall clubId={clubId} />
                  <ClubRecords clubId={clubId} />
                </div>
                {/* Best season из макета — карточки ещё нет (нужен сезонный агрегат по клубу). */}
                <ClubSoonCard
                  title="Best season"
                  sub="The club's strongest season by rank and medals"
                  text="Not built yet — needs a per-season aggregate on the API side."
                />
              </>
            )}

            {tab === 'swimmers' && clubId != null && (
              <>
                {/* Люди клуба парой: слева выжимка «кто тащит», справа полный ростер. */}
                <div className="mb-4 grid grid-cols-1 items-start gap-4 min-[960px]:grid-cols-2">
                  <ClubTopSwimmers swimmers={data.top_swimmers} scopeLabel={scopeLabel} />
                  <ClubSwimmers clubId={clubId} season={scope.season} />
                </div>

                <ClubCoaches />
              </>
            )}

            {tab === 'media' && (
              // Медиа у клуба пока нет вообще: ссылки живут у соревнований и в My media
              // (docs/media-page.md), клубной выборки на API не существует.
              <ClubSoonCard
                title="Media"
                sub="Photos and videos from this club's meets"
                text="Not built yet — there is no club-scoped media feed on the API side."
              />
            )}

            {tab === 'history' && <ClubTimeline timeline={data.timeline} />}
            </div>
            </div>
          </>
        )}
      </main>
    </div>
  );
}

function Notice({ children }: { children: React.ReactNode }) {
  return (
    <div
      className="deep-card text-center text-[14px] font-extrabold"
      style={{ color: 'var(--deep-text-mute)' }}
    >
      {children}
    </div>
  );
}

export default ClubProject;

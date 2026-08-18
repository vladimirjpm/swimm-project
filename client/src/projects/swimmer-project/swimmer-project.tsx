import React, { useEffect, useMemo, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './swimmer-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import DeepSeasonCarousel from '../components/deep/season-carousel';
import DeepTabs from '../components/deep/tabs';
import { parseRoute } from '../../utils/routes';
import Helper from '../../utils/helpers/data-helper';
import { useSwimmerProfile } from './use-swimmer-profile';
import {
  useSwimmerBestTimes, useSwimmerPersonalBests, useSwimmerProgress, useSwimmerSummary,
} from './use-swimmer-page';
import SwimmerHero from './components/swimmer-hero';
import SwimmerMediaPanel from './components/swimmer-media-panel';
import {
  HistoryPanel, PanelEmpty, PersonalBestsPanel, ProgressPanel, ResultsPanel, SeasonPanel,
} from './components/swimmer-panels';

/**
 * Страница спортсмена `/swimmers/{id}` — вариант 2a «Card DNA»
 * (`!design_handoff/design_handoff_athlete_page/`, план docs/plans/athlete-page-plan.md).
 *
 * Устройство: Hero с KPI → ОДНА карусель сезонов на всю страницу → плитки-табы → панель.
 * Сезон выбирается один раз, все табы читают его; свой фильтр есть только у Records & PB
 * (25m и 50m несравнимы — это единственное исключение §7 хендоффа).
 *
 * Сезон и таб живут в query (`?season=`, `?tab=`), а не в пути: правило routes.ts —
 * в путь только идентичность ресурса. Поэтому и то и другое переживает перезагрузку.
 */

type SwimmerTab = 'season' | 'results' | 'pb' | 'progress' | 'media' | 'history';
const TABS: SwimmerTab[] = ['season', 'results', 'pb', 'progress', 'media', 'history'];
const isTab = (v: string | null | undefined): v is SwimmerTab =>
  v != null && (TABS as string[]).includes(v);

/** `?season=` в состояние: «all» и мусор → null (карьера), число → сезон. */
function seasonFromQuery(): number | null | undefined {
  const raw = new URLSearchParams(window.location.search).get('season');
  if (raw == null) return undefined;          // не задан — берём витринный с сервера
  if (raw === 'all') return null;
  const n = Number(raw);
  return Number.isFinite(n) && n > 0 ? n : undefined;
}

function Notice({ children }: { children: React.ReactNode }) {
  return <div className="deep-notice">{children}</div>;
}

function SwimmerProject() {
  useTheme();
  const { mode } = useMode();

  const swimmerId = useMemo<number | null>(() => {
    const fromPath = parseRoute().swimmerId;
    if (fromPath != null) return fromPath;
    const raw = new URLSearchParams(window.location.search).get('swimmer'); // легаси-фоллбек
    const n = raw != null ? Number(raw) : NaN;
    return Number.isFinite(n) && n > 0 ? n : null;
  }, []);

  const profileState = useSwimmerProfile(swimmerId);
  const profile = profileState.status === 'ok' ? profileState.profile : null;

  const [tab, setTab] = useState<SwimmerTab>(() => {
    const t = new URLSearchParams(window.location.search).get('tab');
    return isTab(t) ? t : 'results';
  });

  // undefined — сезон ещё не выбран (ждём витринный из профиля), null — режим All.
  const [season, setSeason] = useState<number | null | undefined>(seasonFromQuery);

  // Умолчание — ВИТРИННЫЙ сезон: до зимних чемпионатов это прошлый сезон, а не свежий
  // (docs/season-boundary-rule.md). Сервер помечает его isDisplayDefault.
  useEffect(() => {
    if (season !== undefined || !profile?.seasons?.length) return;
    const preferred = profile.seasons.find((s) => s.isDisplayDefault) ?? profile.seasons[0];
    setSeason(preferred.season);
  }, [profile, season]);

  const writeQuery = (next: { tab?: SwimmerTab; season?: number | null }) => {
    const url = new URL(window.location.href);
    if (next.tab !== undefined) {
      if (next.tab === 'results') url.searchParams.delete('tab');
      else url.searchParams.set('tab', next.tab);
    }
    if (next.season !== undefined) {
      url.searchParams.set('season', next.season == null ? 'all' : String(next.season));
    }
    window.history.replaceState(null, '', url.toString());
  };

  const handleTab = (next: SwimmerTab) => { setTab(next); writeQuery({ tab: next }); };
  const handleSeason = (next: number | null) => { setSeason(next); writeQuery({ season: next }); };

  // Сезон ещё не определён — запросы не шлём: иначе первый кадр уехал бы за карьеру,
  // а вторым пришёл бы сезон, и панель дважды перерисовалась бы другими цифрами.
  const seasonReady = season !== undefined;
  const activeSeason = seasonReady ? season : null;

  const summary = useSwimmerSummary(swimmerId, activeSeason, seasonReady);
  const career = useSwimmerSummary(swimmerId, null, seasonReady && tab === 'history');
  const bestTimes = useSwimmerBestTimes(swimmerId, activeSeason, seasonReady);

  const [poolType, setPoolType] = useState('25m');
  const personalBests = useSwimmerPersonalBests(swimmerId, poolType, tab === 'pb');

  // Список дистанций для Progress берём из лучших времён ЗА КАРЬЕРУ: у сезона их может
  // не быть вовсе, а история прогресса всё равно есть.
  const allBest = useSwimmerBestTimes(swimmerId, null, tab === 'progress');
  const [discipline, setDiscipline] = useState<string | null>(null);
  useEffect(() => {
    if (discipline == null && allBest.data?.length) setDiscipline(allBest.data[0].disciplineKey);
  }, [allBest.data, discipline]);
  const progress = useSwimmerProgress(swimmerId, tab === 'progress' ? discipline : null);

  const gender: 'male' | 'female' =
    (profile?.gender ?? '').toLowerCase().startsWith('f') ? 'female' : 'male';

  /**
   * Уровень для KPI-плитки: считаем из лучшего по очкам заплыва. Разряд определяет клиент
   * (`NormativeStandard`), и второй реализации на сервере быть не должно — иначе плитка и
   * дуга в строке разъедутся.
   */
  const level = useMemo(() => {
    const rows = (bestTimes.data ?? []).filter((r) => !r.quality && r.time);
    if (rows.length === 0) return null;
    const top = rows.reduce((a, b) => ((b.points ?? 0) > (a.points ?? 0) ? b : a));
    return Helper.getNormativeLevelInfo({
      gender,
      poolType: Helper.resolvePoolType(top.poolType),
      styleName: top.stroke ?? '',
      distance: `${top.distance}m`,
      time: Helper.parseTimeToSeconds(top.time!),
    });
  }, [bestTimes.data, gender]);

  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';
  const seasonLabel = activeSeason == null ? 'career' : String(activeSeason);

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar />

      <main className="mx-auto max-w-[1180px] px-4 py-6" style={{ color: 'var(--deep-text)' }}>
        <div className="mb-4 flex justify-end">
          <UI_ModeToggle />
        </div>

        {profileState.status === 'loading' && <Notice>Loading…</Notice>}
        {profileState.status === 'notfound' && (
          <Notice>{swimmerId == null ? 'No swimmer specified.' : 'Swimmer not found.'}</Notice>
        )}
        {profileState.status === 'error' && <Notice>Could not load this swimmer.</Notice>}

        {profile && (
          <>
            <SwimmerHero profile={profile} summary={summary.data} level={level} />

            {/* Одна карусель на всю страницу: сезон выбирается раз и читается всеми табами. */}
            <DeepSeasonCarousel
              seasons={profile.seasons ?? []}
              season={activeSeason}
              onSeason={handleSeason}
            />

            {/* «Папка» (TABS.md 3a): активная плитка срастается с панелью, поэтому общая обёртка. */}
            <div className="deep-folder mb-4">
              <DeepTabs
                ariaLabel="Athlete sections"
                active={tab}
                onSelect={handleTab}
                tabs={[
                  {
                    id: 'season',
                    icon: '▦',
                    label: 'Season',
                    sub: summary.data
                      ? `${summary.data.competitionCount} meets · ${summary.data.points} pts`
                      : seasonLabel,
                  },
                  {
                    id: 'results',
                    icon: '⏱',
                    label: 'Results',
                    sub: bestTimes.data ? `${bestTimes.data.length} best times` : 'best per distance',
                  },
                  {
                    id: 'pb',
                    icon: '🏅',
                    label: 'Records & PB',
                    shortLabel: 'PB',
                    sub: personalBests.data ? `${personalBests.data.length} bests` : 'career bests',
                  },
                  { id: 'progress', icon: '📈', label: 'Progress', sub: 'by stroke and distance' },
                  { id: 'media', icon: '▶', label: 'Media', sub: 'photos and video' },
                  {
                    id: 'history',
                    icon: '🗓',
                    label: 'History',
                    sub: profile.seasons?.length ? `${profile.seasons.length} seasons` : 'career',
                  },
                ]}
              />

              <div className="deep-tabs-panel">
                {!seasonReady && <PanelEmpty>Loading…</PanelEmpty>}

                {seasonReady && tab === 'season' && (
                  <SeasonPanel summary={summary.data} swimmerId={profile.id} />
                )}
                {seasonReady && tab === 'results' && (
                  <ResultsPanel rows={bestTimes.data} swimmerId={profile.id} gender={gender} />
                )}
                {seasonReady && tab === 'pb' && (
                  <PersonalBestsPanel
                    rows={personalBests.data}
                    poolType={poolType}
                    onPoolType={setPoolType}
                  />
                )}
                {seasonReady && tab === 'progress' && (
                  <ProgressPanel
                    distances={allBest.data}
                    selected={discipline}
                    onSelect={setDiscipline}
                    progress={progress.data}
                    swimmerId={profile.id}
                    gender={gender}
                  />
                )}
                {seasonReady && tab === 'media' && <SwimmerMediaPanel swimmerId={profile.id} />}
                {seasonReady && tab === 'history' && (
                  <HistoryPanel career={career.data} swimmerId={profile.id} />
                )}
              </div>
            </div>
          </>
        )}
      </main>
    </div>
  );
}

export default SwimmerProject;

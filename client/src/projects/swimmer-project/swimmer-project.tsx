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
import { seasonLabel } from '../../utils/helpers/season-helper';
import { useSwimmerProfile } from './use-swimmer-profile';
import {
  useSwimmerBestTimes, useSwimmerPersonalBests, useSwimmerProgress, useSwimmerSeasonRanks,
  useSwimmerSummary,
} from './use-swimmer-page';
import SwimmerHero from './components/swimmer-hero';
import SwimmerMediaPanel from './components/swimmer-media-panel';
import SwimmerUpcomingStarts from './components/swimmer-upcoming-starts';
import {
  HistoryPanel, holdsSeasonBest, PanelEmpty, PersonalBestsPanel, ProgressPanel, ResultsFilters,
  ResultsPanel, SeasonBestPanel, SeasonPanel, type ResultsView,
} from './components/swimmer-panels';

/**
 * Страница спортсмена `/swimmers/{id}` — вариант 2a «Card DNA»
 * (`!design_handoff/design_handoff_athlete_page/`, план docs/plans/athlete-page-plan.md).
 *
 * Устройство: Hero с KPI → ОДНА карусель сезонов на всю страницу → ЧЕТЫРЕ плитки-таба
 * (Season · Results · Media · History) → панель. Сезон выбирается один раз, все табы читают его.
 *
 * Внутри Results — полоса фильтров вместо отдельных табов: Best time · Season best ·
 * Records · Progress. Раньше «Records & PB» и «Progress» были плитками верхнего уровня;
 * шесть плиток не помещались в мобайл, а по смыслу все четыре вида отвечают на один вопрос
 * «как я плыву» и отличаются только точкой отсчёта. Тумблер бассейна остаётся ЛОКАЛЬНЫМ
 * фильтром вида Records (25m и 50m несравнимы — исключение §7 хендоффа).
 *
 * Сезон, таб и фильтр живут в query (`?season=`, `?tab=`, `?view=`), а не в пути: правило
 * routes.ts — в путь только идентичность ресурса. Поэтому всё это переживает перезагрузку.
 */

type SwimmerTab = 'season' | 'results' | 'media' | 'history';
const TABS: SwimmerTab[] = ['season', 'results', 'media', 'history'];
const isTab = (v: string | null | undefined): v is SwimmerTab =>
  v != null && (TABS as string[]).includes(v);

const VIEWS: ResultsView[] = ['best', 'season-best', 'records', 'progress'];
const isView = (v: string | null | undefined): v is ResultsView =>
  v != null && (VIEWS as string[]).includes(v);

/**
 * Старые диплинки на снятые табы (`?tab=pb`, `?tab=progress`) приземляются на Results с
 * нужным фильтром: ссылки на них уже разошлись, и молча открывать «Best time» вместо
 * запрошенного вида значит тихо соврать.
 */
const LEGACY_TAB_VIEW: Record<string, ResultsView> = { pb: 'records', progress: 'progress' };

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
    // Снятые табы (`?tab=pb`) тоже приземляются на Results — вид подхватит `view` ниже.
    return isTab(t) ? t : 'results';
  });

  const [view, setView] = useState<ResultsView>(() => {
    const params = new URLSearchParams(window.location.search);
    const v = params.get('view');
    if (isView(v)) return v;
    const legacy = params.get('tab');
    return legacy != null && LEGACY_TAB_VIEW[legacy] ? LEGACY_TAB_VIEW[legacy] : 'best';
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

  const writeQuery = (next: { tab?: SwimmerTab; season?: number | null; view?: ResultsView }) => {
    const url = new URL(window.location.href);
    if (next.tab !== undefined) {
      if (next.tab === 'results') url.searchParams.delete('tab');
      else url.searchParams.set('tab', next.tab);
    }
    if (next.view !== undefined) {
      // Умолчания в адресе не держим: `?view=best` это тот же адрес, что без него.
      if (next.view === 'best') url.searchParams.delete('view');
      else url.searchParams.set('view', next.view);
    }
    if (next.season !== undefined) {
      url.searchParams.set('season', next.season == null ? 'all' : String(next.season));
    }
    window.history.replaceState(null, '', url.toString());
  };

  const handleTab = (next: SwimmerTab) => { setTab(next); writeQuery({ tab: next }); };
  const handleView = (next: ResultsView) => { setView(next); writeQuery({ view: next }); };
  const handleSeason = (next: number | null) => { setSeason(next); writeQuery({ season: next }); };

  // Сезон ещё не определён — запросы не шлём: иначе первый кадр уехал бы за карьеру,
  // а вторым пришёл бы сезон, и панель дважды перерисовалась бы другими цифрами.
  const seasonReady = season !== undefined;
  const activeSeason = seasonReady ? season : null;

  const summary = useSwimmerSummary(swimmerId, activeSeason, seasonReady);
  const career = useSwimmerSummary(swimmerId, null, seasonReady && tab === 'history');
  const bestTimes = useSwimmerBestTimes(swimmerId, activeSeason, seasonReady);

  const onResults = tab === 'results';

  const [poolType, setPoolType] = useState('25m');
  const personalBests = useSwimmerPersonalBests(swimmerId, poolType, onResults && view === 'records');

  /**
   * Сезон, за который считаются МЕСТА. Обычно это выбранный сезон, но в режиме ∞ (All)
   * мест не бывает — сравнение живёт внутри одного сезона. Вместо тупика показываем места
   * за ВИТРИННЫЙ сезон и честно подписываем, за какой (решение Влада 2026-08-25).
   */
  const displaySeason = useMemo(() => {
    const list = profile?.seasons ?? [];
    return (list.find((x) => x.isDisplayDefault) ?? list[0])?.season ?? null;
  }, [profile]);
  const ranksSeason = activeSeason ?? displaySeason;
  const seasonBestFallback = activeSeason == null && ranksSeason != null;

  // Места среди сверстников нужны ДВУМ видам: своей панели Season best и бейджу SB на
  // строках Best time. Один запрос на оба — иначе бейдж и таблица мест могли бы разойтись.
  // Грузим всегда, а не только на табе Results: из этих же мест считается плитка достижений
  // в шапке (docs/swimmer-achievements-tile.md), а она видна на любом табе.
  const seasonRanks = useSwimmerSeasonRanks(swimmerId, ranksSeason, seasonReady);

  /**
   * Строки для панели Season best. В режиме ∞ строки таба — КАРЬЕРНЫЕ, а места сезонные:
   * поставить карьерное время рядом с сезонным местом значит показать место за один заплыв,
   * а время — за другой. Поэтому для этой панели строки того же сезона тянутся отдельно.
   */
  const fallbackBest = useSwimmerBestTimes(
    swimmerId, ranksSeason, seasonReady && onResults && view === 'season-best' && seasonBestFallback);
  const seasonBestRows = seasonBestFallback ? fallbackBest : bestTimes;

  /**
   * Дисциплины, где пловец первый в своей возрастной группе, — носители бейджа SB.
   * Предикат ОДИН на оба вида (`holdsSeasonBest`): иначе строка могла бы носить SB, а
   * таблица мест — не подсвечивать её.
   *
   * В режиме ∞ бейджей НЕТ: список Best time там карьерный, а место сезонное — SB на
   * карьерной строке утверждал бы то, чего никто не считал.
   */
  const sbKeys = useMemo(
    () => (activeSeason == null
      ? new Set<string>()
      : new Set((seasonRanks.data?.rows ?? []).filter(holdsSeasonBest).map((r) => r.disciplineKey))),
    [seasonRanks.data, activeSeason]);

  /**
   * Лучшие времена ЗА КАРЬЕРУ. Грузим всегда, а не только на виде Progress: из них же
   * считается разряд в шапке, а он про «лучшее, что я плыл когда-либо», и должен быть
   * готов на первом кадре. Список дистанций для Progress берётся отсюда же — у сезона их
   * может не быть вовсе, а история прогресса всё равно есть.
   */
  const allBest = useSwimmerBestTimes(swimmerId, null);
  const [discipline, setDiscipline] = useState<string | null>(null);
  useEffect(() => {
    if (discipline == null && allBest.data?.length) setDiscipline(allBest.data[0].disciplineKey);
  }, [allBest.data, discipline]);
  const progress = useSwimmerProgress(
    swimmerId, onResults && view === 'progress' ? discipline : null);

  const gender: 'male' | 'female' =
    (profile?.gender ?? '').toLowerCase().startsWith('f') ? 'female' : 'male';

  /**
   * Разряд для KPI-плитки — по ЛУЧШЕМУ ЗАПЛЫВУ ЗА ВСЮ КАРЬЕРУ (правило Влада 2026-08-25),
   * а не за выбранный сезон: разряд однажды выполнен и сезоном не отменяется, иначе ветеран
   * в межсезонье выглядел бы новичком. Карусель на эту плитку не влияет.
   *
   * ⚠ Мастерс считается по СВОЕЙ таблице с возрастными полосами. Без флага время 45-летней
   * женщины (00:31.47 на 50 на спине) меряется юношеской шкалой и даёт «первый взрослый»
   * вместо МСМК — ровно этот баг ловили на пловце 7424.
   *
   * Разряд определяет клиент (`NormativeStandard`), и второй реализации на сервере быть
   * не должно — иначе плитка и дуга в строке разъедутся.
   */
  const level = useMemo(() => {
    const rows = (allBest.data ?? []).filter((r) => !r.quality && r.time);
    if (rows.length === 0) return null;
    const top = rows.reduce((a, b) => ((b.points ?? 0) > (a.points ?? 0) ? b : a));
    return Helper.getNormativeLevelInfo({
      gender,
      poolType: Helper.resolvePoolType(top.poolType),
      styleName: top.stroke ?? '',
      distance: `${top.distance}m`,
      time: Helper.parseTimeToSeconds(top.time!),
      isMaster: top.isMasters,
      // Возраст хелпер сам разложит в полосу мастерса («45» → «45-49»).
      ageGroup: top.ageInSeason != null ? String(top.ageInSeason) : null,
    });
  }, [allBest.data, gender]);

  /**
   * Вход плитки достижений (правила — docs/swimmer-achievements-tile.md).
   * Рекорды всегда за КАРЬЕРУ: в справочнике федерации у записи нет сезона, а разбирать
   * `RecordDate` нельзя — там два формата и 47 строк вне обоих.
   */
  const achievements = useMemo(() => ({
    scopeLabel: activeSeason == null ? 'All time' : seasonLabel(activeSeason),
    records: profile?.recordsHeld ?? 0,
    seasonBests: (seasonRanks.data?.rows ?? []).filter(holdsSeasonBest).length,
    seasonBestsLabel: seasonRanks.data?.label ?? '',
  }), [activeSeason, profile, seasonRanks.data]);

  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';
  // Подпись плитки Season: тот же формат «2025/26», что у сервера и у остальных экранов —
  // голый год («2025») читался бы как календарный, а сезон идёт через границу года.
  const seasonSub = activeSeason == null ? 'career' : seasonLabel(activeSeason);

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
            <SwimmerHero
              profile={profile}
              summary={summary.data}
              level={level}
              achievements={achievements}
            />

            <SwimmerUpcomingStarts swimmerId={profile.id} />

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
                      : seasonSub,
                  },
                  {
                    id: 'results',
                    icon: '⏱',
                    label: 'Results',
                    sub: bestTimes.data ? `${bestTimes.data.length} best times` : 'best per distance',
                  },
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
                  <SeasonPanel summary={summary.data} swimmerId={profile.id} state={summary} />
                )}
                {seasonReady && onResults && (
                  <>
                    <ResultsFilters
                      view={view}
                      onView={handleView}
                      recordsHeld={profile.recordsHeld}
                    />

                    {view === 'best' && (
                      <ResultsPanel
                        rows={bestTimes.data}
                        swimmerId={profile.id}
                        gender={gender}
                        sbKeys={sbKeys}
                        state={bestTimes}
                      />
                    )}
                    {view === 'season-best' && (
                      <SeasonBestPanel
                        rows={seasonBestRows.data}
                        ranks={seasonRanks.data}
                        swimmerId={profile.id}
                        season={ranksSeason}
                        isFallbackSeason={seasonBestFallback}
                        state={{
                          loading: seasonBestRows.loading || seasonRanks.loading,
                          error: seasonBestRows.error || seasonRanks.error,
                        }}
                      />
                    )}
                    {view === 'records' && (
                      <PersonalBestsPanel
                        rows={personalBests.data}
                        poolType={poolType}
                        onPoolType={setPoolType}
                        records={profile.records}
                        gender={gender}
                        age={profile.ageInSeason}
                        state={personalBests}
                      />
                    )}
                    {view === 'progress' && (
                      <ProgressPanel
                        distances={allBest.data}
                        selected={discipline}
                        onSelect={setDiscipline}
                        progress={progress.data}
                        swimmerId={profile.id}
                        gender={gender}
                        state={allBest}
                        progressState={progress}
                      />
                    )}
                  </>
                )}
                {seasonReady && tab === 'media' && <SwimmerMediaPanel swimmerId={profile.id} />}
                {seasonReady && tab === 'history' && (
                  <HistoryPanel career={career.data} swimmerId={profile.id} state={career} />
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

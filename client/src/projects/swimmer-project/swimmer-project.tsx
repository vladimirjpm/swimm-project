import React, { useEffect, useMemo, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './swimmer-page.css';
import { useTheme } from '../../hooks/useTheme';
import DeepSeasonCarousel from '../components/deep/season-carousel';
import DeepEntityPage from '../components/deep/entity-page';
import DeepDigestCard from '../components/deep/digest-card';
import SwimRow from '../components/swim-row/swim-row';
import UI_DateIcon from '../components/mix/date-icon/date-icon';
import type {
  EntityPageStatus, EntityTabNav, EntityTabSpec,
} from '../components/deep/entity-page-types';
import { parseRoute, H2H_PARAM } from '../../utils/routes';
import Helper from '../../utils/helpers/data-helper';
import { seasonLabel } from '../../utils/helpers/season-helper';
import { useSwimmerProfile } from './use-swimmer-profile';
import {
  useSwimmerBestTimes, useSwimmerCompare, useSwimmerPersonalBests, useSwimmerProgress,
  useSwimmerSearch, useSwimmerSeasonRanks, useSwimmerSummary,
} from './use-swimmer-page';
import SwimmerHero from './components/swimmer-hero';
import SwimmerMediaPanel from './components/swimmer-media-panel';
import SwimmerUpcomingStarts from './components/swimmer-upcoming-starts';
import {
  H2HPanel, holdsSeasonBest, PanelEmpty, PersonalBestsPanel, ProgressPanel, ResultsFilters,
  ResultsPanel, SeasonBestPanel, SeasonPanel, type ResultsView,
} from './components/swimmer-panels';

/**
 * Страница спортсмена `/swimmers/{id}` — вариант 2a «Card DNA»
 * (`!design_handoff/design_handoff_athlete_page/`, план docs/plans/athlete-page-plan.md).
 *
 * Устройство: Hero с KPI → ОДНА карусель сезонов на всю страницу → ЧЕТЫРЕ плитки-таба
 * (Season · Results · Media · H2H) → панель. Сезон выбирается один раз, все табы читают его.
 *
 * Таба History здесь больше нет (2026-09-01): он показывал ТОТ ЖЕ список стартов, что Season
 * в режиме ∞, отличаясь одной группировкой по сезонам, — она переехала внутрь Season, а
 * плитку занял H2H (head-to-head).
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

type SwimmerTab = 'overview' | 'season' | 'results' | 'media' | 'h2h';
const TABS: SwimmerTab[] = ['overview', 'season', 'results', 'media', 'h2h'];
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

/**
 * Снятые табы, у которых есть НАСЛЕДНИК: `?tab=history` открывает Season за всю карьеру —
 * ровно то, что History и показывал. Ссылки на него разошлись, и молча открывать Results
 * (умолчание) значило бы тихо потерять запрошенный экран.
 */
const LEGACY_TAB_ALIAS: Record<string, SwimmerTab> = {
  history: 'season',
  rivals: 'h2h',
  // `pb` и `progress` — виды ВНУТРИ Results (`LEGACY_TAB_VIEW` подставит нужный `view`).
  // Раньше они попадали на Results умолчанием; с появлением дайджеста умолчание стало
  // `overview`, и без явного алиаса старая ссылка открывала бы не тот экран.
  pb: 'results',
  progress: 'results',
};

/**
 * `?h2h_b=` — с кем сравнивать в табе H2H. Имя общее со страницей `/h2h` (`H2H_PARAM`):
 * таб — её частный случай, где левая сторона это хозяин профиля, поэтому в адресе только
 * правая. Прежнее `?rival=` принимаем на чтение — ссылки уже разосланы.
 *
 * Мусор и «сам с собой» → соперника нет.
 */
function rivalFromQuery(selfId: number | null): number | null {
  const params = new URLSearchParams(window.location.search);
  const raw = params.get(H2H_PARAM.b) ?? params.get('rival');
  const n = raw != null ? Number(raw) : NaN;
  return Number.isFinite(n) && n > 0 && n !== selfId ? n : null;
}

/** `?season=` в состояние: «all» и мусор → null (карьера), число → сезон. */
function seasonFromQuery(): number | null | undefined {
  const raw = new URLSearchParams(window.location.search).get('season');
  if (raw == null) return undefined;          // не задан — берём витринный с сервера
  if (raw === 'all') return null;
  const n = Number(raw);
  return Number.isFinite(n) && n > 0 ? n : undefined;
}

function SwimmerProject() {
  useTheme();

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
    if (isTab(t)) return t;
    // Снятый таб с наследником (`?tab=history` → Season за карьеру); `?tab=pb` и
    // `?tab=progress` приземляются на Results — вид подхватит `view` ниже.
    return (t != null && LEGACY_TAB_ALIAS[t]) || 'overview';
  });

  const [view, setView] = useState<ResultsView>(() => {
    const params = new URLSearchParams(window.location.search);
    const v = params.get('view');
    if (isView(v)) return v;
    const legacy = params.get('tab');
    return legacy != null && LEGACY_TAB_VIEW[legacy] ? LEGACY_TAB_VIEW[legacy] : 'best';
  });

  // undefined — сезон ещё не выбран (ждём витринный из профиля), null — режим All.
  const [season, setSeason] = useState<number | null | undefined>(() => {
    const fromQuery = seasonFromQuery();
    if (fromQuery !== undefined) return fromQuery;
    // Старая ссылка на History — это «за всю карьеру»: без этого она открывала бы список
    // одного сезона под тем же адресом.
    const legacy = new URLSearchParams(window.location.search).get('tab');
    return legacy != null && LEGACY_TAB_ALIAS[legacy] ? null : undefined;
  });

  // Соперник таба H2H живёт в адресе (`?h2h_b=`) по той же причине, что сезон и таб:
  // сравнение — это то, чем делятся ссылкой.
  const [rivalId, setRivalId] = useState<number | null>(() => rivalFromQuery(swimmerId));
  const [rivalQuery, setRivalQuery] = useState('');

  // Умолчание — ВИТРИННЫЙ сезон: до зимних чемпионатов это прошлый сезон, а не свежий
  // (docs/season-boundary-rule.md). Сервер помечает его isDisplayDefault.
  useEffect(() => {
    if (season !== undefined || !profile?.seasons?.length) return;
    const preferred = profile.seasons.find((s) => s.isDisplayDefault) ?? profile.seasons[0];
    setSeason(preferred.season);
  }, [profile, season]);

  const writeQuery = (
    next: { season?: number | null; view?: ResultsView; rival?: number | null },
  ) => {
    const url = new URL(window.location.href);
    if (next.rival !== undefined) {
      // Легаси-имя вычищаем всегда: иначе адрес нёс бы обоих и читался бы по старому.
      url.searchParams.delete('rival');
      if (next.rival == null) url.searchParams.delete(H2H_PARAM.b);
      else url.searchParams.set(H2H_PARAM.b, String(next.rival));
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

  // Адрес пишет каркас (управляемый режим): здесь только своё состояние — по нему страница
  // гейтит запросы табов (`onH2H`, `onResults`).
  const handleTab = (next: SwimmerTab) => setTab(next);
  const handleView = (next: ResultsView) => { setView(next); writeQuery({ view: next }); };
  const handleSeason = (next: number | null) => { setSeason(next); writeQuery({ season: next }); };

  const handleRival = (next: number | null) => {
    setRivalId(next);
    // Строку поиска чистим вместе с выбором: она уже сделала своё дело, а оставленный
    // запрос снова раскрывал бы выдачу поверх таблицы на следующем кадре.
    setRivalQuery('');
    writeQuery({ rival: next });
  };

  // Сезон ещё не определён — запросы не шлём: иначе первый кадр уехал бы за карьеру,
  // а вторым пришёл бы сезон, и панель дважды перерисовалась бы другими цифрами.
  const seasonReady = season !== undefined;
  const activeSeason = seasonReady ? season : null;

  const summary = useSwimmerSummary(swimmerId, activeSeason, seasonReady);

  // Сравнение и поиск живут в табе H2H: пока его не открыли, запросов нет.
  const onH2H = tab === 'h2h';
  const compare = useSwimmerCompare(swimmerId, rivalId, activeSeason, seasonReady && onH2H);
  const rivalHits = useSwimmerSearch(onH2H && rivalId == null ? rivalQuery : '');
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

  // Подпись плитки Season: тот же формат «2025/26», что у сервера и у остальных экранов —
  // голый год («2025») читался бы как календарный, а сезон идёт через границу года.
  const seasonSub = activeSeason == null ? 'career' : seasonLabel(activeSeason);

  const status: EntityPageStatus =
    profile ? 'ready'
      : profileState.status === 'notfound' ? 'notfound'
        : profileState.status === 'error' ? 'error'
          : 'loading';

  // Табы собираются только когда профиль есть: подписи-сводки — живые числа, не хардкод.
  // У пловца тело таба одно на таб, поэтому карточка в каждом одна: сетка панели тут не
  // раскладывает пары, а только даёт общий корпус. Полоса фильтров Results и выбранный вид
  // живут в ОДНОЙ карточке — между ними свой отступ 12px (`.deep-filters`), а отдельными
  // карточками сетка поставила бы 16px и раздвинула их.
  const tabs: EntityTabSpec<SwimmerTab>[] = profile == null ? [] : [
    {
      // Дайджест: срезы соседних табов из уже загруженных данных, второго запроса нет.
      // Предстоящих стартов тут намеренно нет — они стоят НАД табами и видны с любого.
      id: 'overview',
      icon: '▦',
      label: 'Overview',
      sub: 'last meet · best times',
      cards: (nav: EntityTabNav<SwimmerTab>) => [
        {
          id: 'last-meet',
          span: 'half' as const,
          render: () => {
            const meets = summary.data?.competitions ?? [];
            const last = meets[0];
            return (
              <DeepDigestCard
                title="Recent meets"
                subtitle={last ? `latest — ${last.name}` : 'meets of the selected scope'}
                count={meets.length}
                countLabel="MEETS"
                moreLabel={`All ${meets.length} meets →`}
                onMore={() => nav.go('season')}
                isEmpty={!seasonReady || meets.length === 0}
                emptyText={seasonReady ? 'No meets in this season.' : 'Loading…'}
              >
                <div className="flex flex-col gap-1.5">
                  {meets.slice(0, 4).map((m) => (
                    <div
                      key={`${m.competitionId}-${m.date}`}
                      className="flex items-center justify-between gap-3 px-3 py-2"
                      style={{ background: 'var(--deep-card-bg-row)', borderRadius: 'var(--deep-radius-row)' }}
                    >
                      <div className="min-w-0">
                        <div className="truncate text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                          {m.name}
                        </div>
                        <div className="flex items-center gap-1.5 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                          <UI_DateIcon styleType="row-style-1" date={m.date} paddingClass="" fontClassName="text-[11px] font-bold" />
                          <span>· {m.swims} swims</span>
                        </div>
                      </div>
                      <span className="hp-mono shrink-0 text-[11.5px] font-extrabold" style={{ color: 'var(--deep-accent)' }}>
                        {m.points} pts
                      </span>
                    </div>
                  ))}
                </div>
              </DeepDigestCard>
            );
          },
        },
        {
          id: 'best-times-digest',
          span: 'half' as const,
          render: () => {
            const rows = [...(bestTimes.data ?? [])]
              .sort((a, b) => (b.points ?? 0) - (a.points ?? 0))
              .slice(0, 3);
            return (
              <DeepDigestCard
                title="Best times"
                subtitle="strongest results by FINA points"
                count={bestTimes.data?.length ?? null}
                countLabel="DISTANCES"
                moreLabel="All best times →"
                onMore={() => nav.go('results')}
                isEmpty={!seasonReady || rows.length === 0}
                emptyText={seasonReady ? 'No results in this scope.' : 'Loading…'}
              >
                <div className="deep-list">
                  {rows.map((r) => (
                    <SwimRow
                      key={r.disciplineKey}
                      stroke={r.stroke ?? ''}
                      distance={r.distance}
                      poolType={r.poolType}
                      time={r.time}
                      quality={r.quality}
                      timeFail={!!r.timeFail}
                      badge={sbKeys.has(r.disciplineKey) ? 'sb' : null}
                      place={{ kind: 'none' }}
                      competition={{ name: r.competition.name }}
                      date={r.date}
                      points={r.points}
                    />
                  ))}
                </div>
              </DeepDigestCard>
            );
          },
        },
      ],
    },
    {
      id: 'season',
      icon: '▦',
      label: 'Season',
      sub: summary.data
        ? `${summary.data.competitionCount} meets · ${summary.data.points} pts`
        : seasonSub,
      cards: () => [{
        id: 'season-panel',
        render: () => (!seasonReady
          ? <PanelEmpty>Loading…</PanelEmpty>
          : <SeasonPanel summary={summary.data} swimmerId={profile.id} state={summary} />),
      }],
    },
    {
      id: 'results',
      icon: '⏱',
      label: 'Results',
      sub: bestTimes.data ? `${bestTimes.data.length} best times` : 'best per distance',
      cards: () => [{
        id: 'results-panel',
        render: () => (!seasonReady ? <PanelEmpty>Loading…</PanelEmpty> : (
          <>
            <ResultsFilters view={view} onView={handleView} recordsHeld={profile.recordsHeld} />

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
        )),
      }],
    },
    {
      id: 'media',
      icon: '▶',
      label: 'Media',
      sub: 'photos and video',
      cards: () => [{
        id: 'media-panel',
        render: () => (!seasonReady
          ? <PanelEmpty>Loading…</PanelEmpty>
          : <SwimmerMediaPanel swimmerId={profile.id} />),
      }],
    },
    {
      id: 'h2h',
      icon: '⚔',
      label: 'H2H',
      sub: compare.data ? `vs ${compare.data.rival.name}` : 'compare with a swimmer',
      cards: () => [{
        id: 'h2h-panel',
        render: () => (!seasonReady ? <PanelEmpty>Loading…</PanelEmpty> : (
          <H2HPanel
            compare={compare.data}
            query={rivalQuery}
            onQuery={setRivalQuery}
            hits={rivalHits.data}
            hitsState={rivalHits}
            onPick={handleRival}
            onClear={() => handleRival(null)}
            rivalId={rivalId}
            swimmerId={profile.id}
            profileName={profile.fullName}
            season={activeSeason}
            state={compare}
          />
        )),
      }],
    },
  ];

  return (
    <DeepEntityPage<SwimmerTab>
      status={status}
      messages={{
        notfound: swimmerId == null ? 'No swimmer specified.' : 'Swimmer not found.',
        error: 'Could not load this swimmer.',
      }}
      noticeClassName="deep-notice"
      hero={profile ? (
        <SwimmerHero
          profile={profile}
          summary={summary.data}
          level={level}
          achievements={achievements}
        />
      ) : null}
      beforeTabs={profile ? (
        <>
          <SwimmerUpcomingStarts swimmerId={profile.id} />

          {/* Одна карусель на всю страницу: сезон выбирается раз и читается всеми табами. */}
          <DeepSeasonCarousel
            seasons={profile.seasons ?? []}
            season={activeSeason}
            onSeason={handleSeason}
          />
        </>
      ) : null}
      tabsAriaLabel="Athlete sections"
      tabs={tabs}
      // Умолчание — дайджест, и он же живёт БЕЗ `?tab=` в адресе. До появления Overview
      // умолчанием был Results; старые ссылки на него по-прежнему открывают Results, просто
      // теперь через явный `?tab=results`.
      defaultTabId="overview"
      // Управляемый режим: по активному табу страница гейтит запросы (H2H и Results не
      // грузятся, пока таб не открыт) и разбирает легаси-алиасы `?tab=history|pb|progress`.
      activeTabId={tab}
      onTabChange={handleTab}
    />
  );
}

export default SwimmerProject;

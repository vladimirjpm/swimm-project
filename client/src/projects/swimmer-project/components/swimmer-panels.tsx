import React, { useState } from 'react';
import UI_SwimTime from '../../components/mix/swim-time/swim-time';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import EventPlate from './event-plate';
import SwimmerResultRow, { type ResultRowData } from './swimmer-result-row';
import { routes } from '../../../utils/routes';
import { seasonLabel } from '../../../utils/helpers/season-helper';
import type {
  SwimmerBestTime, SwimmerCompetition, SwimmerDisciplineRank, SwimmerPersonalBest, SwimmerProgress,
  SwimmerSeasonRanks, SwimmerSummary,
} from '../use-swimmer-page';
import type { SwimmerHeldRecord } from '../use-swimmer-profile';

/**
 * Панели табов страницы спортсмена (BLOCKS.md §5–9). Каждая — независимый блок: свои данные,
 * никаких собственных фильтров, кроме тумблера 25m/50m в PB (единственное исключение §7).
 *
 * Пустое состояние — норма, а не край: панель рендерится с текстом «нет заплывов в этом
 * сезоне», карусель и табы остаются на месте.
 */

/**
 * Сколько сверстников должно быть в дисциплине, чтобы место вообще что-то значило.
 * Меньше двух — «первый среди одного»: у мастерса 1958 года рождения на 200 на спине
 * в сезоне вообще нет соперников, и бейдж SB там читался бы как достижение, которым не
 * является. Само место сервер отдаёт как есть — прятать данные нельзя, но и хвастаться ими
 * тоже; поэтому порог живёт здесь, в одном месте на оба вида.
 */
const MIN_PEERS_FOR_SB = 2;

/** Первое место в группе, которое действительно является местом (см. MIN_PEERS_FOR_SB). */
export const holdsSeasonBest = (r: SwimmerDisciplineRank) =>
  r.rank === 1 && r.peerCount >= MIN_PEERS_FOR_SB;

/**
 * Состояние загрузки блока — ровно то, что отдаёт `useJson`, поэтому в панель передаётся
 * сам результат хука (`state={bestTimes}`), а не разобранный на булевы.
 */
export interface PanelLoad {
  loading: boolean;
  error: boolean;
}

/**
 * Текст панели, у которой данных НЕТ. Состояний три, и слить их в одно «Loading…» — значит
 * соврать: при сетевой ошибке страница крутила бы вечный спиннер (баг Season best,
 * 2026-08-25), а при несостоявшемся запросе — обещала данные, которых никто не просил.
 * Поэтому «грузимся» и «не грузимся, но пусто» — разные надписи, и обе живут здесь.
 */
const noDataText = (loading: boolean, whenIdle: string) => (loading ? 'Loading…' : whenIdle);

export function PanelEmpty({ children }: { children: React.ReactNode }) {
  return <div className="deep-empty">{children}</div>;
}

function PanelHead({ title, hint, right }: { title: string; hint?: string; right?: React.ReactNode }) {
  return (
    <div className="deep-panel-head">
      <div>
        <div className="deep-panel-title">{title}</div>
        {hint && <div className="deep-panel-hint">{hint}</div>}
      </div>
      {right}
    </div>
  );
}

/** Шапка панели: очки сезона слева, медали справа (BLOCKS.md §5). */
export function SeasonBanner({ summary }: { summary: SwimmerSummary }) {
  return (
    <div className="deep-season-banner">
      <div>
        <div className="deep-season-banner__points">{summary.points.toLocaleString('en-US')}</div>
        <div className="deep-season-banner__sub">
          {summary.season == null ? 'career' : `season ${summary.label}`}
          {' · '}{summary.swims} swims · {summary.events} events
        </div>
      </div>
      <div className="deep-season-banner__medals">
        {(['1', '2', '3'] as const).map((place, i) => (
          <UI_MedalIcon
            key={place}
            place={place}
            styleType="icon-place"
            styleSize="medal-24"
            placeReplace={String([summary.medals.gold, summary.medals.silver, summary.medals.bronze][i])}
          />
        ))}
      </div>
    </div>
  );
}

/** Один старт в списке сезона/истории. */
function CompetitionRow({ meet, swimmerId }: { meet: SwimmerCompetition; swimmerId: number }) {
  const KIND_ICON: Record<string, string> = { winter: '❄', summer: '☀', openwater: '🌊' };
  return (
    <a
      className="deep-meet-row"
      href={`/results?competitionId=${meet.competitionId}&tab=swims&swimmerId=${swimmerId}`}
    >
      <span className="deep-meet-row__date">{meet.date}</span>
      <span className="deep-meet-row__name" dir="auto">
        {meet.isChampionship && <span aria-hidden="true">🏆 </span>}
        {meet.kind && KIND_ICON[meet.kind] && (
          <span aria-hidden="true">{KIND_ICON[meet.kind]} </span>
        )}
        {meet.name}
      </span>
      <span className="deep-meet-row__stats">
        {meet.swims} swims · {meet.points} pts
        {meet.bestPlace != null ? ` · best #${meet.bestPlace}` : ''}
      </span>
      <span className="deep-meet-row__medals">
        {meet.medals.gold > 0 && <span className="deep-medal-dot deep-medal-dot--gold">{meet.medals.gold}</span>}
        {meet.medals.silver > 0 && <span className="deep-medal-dot deep-medal-dot--silver">{meet.medals.silver}</span>}
        {meet.medals.bronze > 0 && <span className="deep-medal-dot deep-medal-dot--bronze">{meet.medals.bronze}</span>}
      </span>
    </a>
  );
}

/** Таб Season: сводка сезона + его старты (❄/☀ выведены из самих соревнований). */
export function SeasonPanel({
  summary, swimmerId, state,
}: { summary: SwimmerSummary | null; swimmerId: number; state: PanelLoad }) {
  if (state.error) return <PanelEmpty>Could not load this season.</PanelEmpty>;
  if (!summary) return <PanelEmpty>{noDataText(state.loading, 'No data for this season.')}</PanelEmpty>;

  return (
    <>
      <SeasonBanner summary={summary} />
      <PanelHead
        title="Competitions"
        hint={`${summary.competitionCount} meets · ${summary.personalBests} personal bests`}
      />
      {summary.competitions.length === 0 ? (
        <PanelEmpty>No swims in this season yet.</PanelEmpty>
      ) : (
        <div className="deep-list">
          {summary.competitions.map((meet) => (
            <CompetitionRow key={`${meet.eventId ?? 'c'}-${meet.competitionId}`} meet={meet} swimmerId={swimmerId} />
          ))}
        </div>
      )}
    </>
  );
}

/**
 * Фильтры внутри таба Results. Раньше это были отдельные табы «Records & PB» и «Progress»:
 * шесть плиток не помещались в мобайл, а по смыслу все четыре отвечают на один вопрос —
 * «как я плыву», отличаясь только точкой отсчёта (сезон, сверстники, карьера, время).
 *
 * `sb` (первое место среди сверстников) намеренно живёт НЕ здесь, а бейджем на строке:
 * фильтр — это точка зрения, а не достижение.
 */
export type ResultsView = 'best' | 'season-best' | 'records' | 'progress';

interface ResultsViewMeta {
  id: ResultsView;
  icon: string;
  label: string;
  /** Подпись под полосой чипов — что именно сейчас показано. */
  caption: string;
}

export const RESULTS_VIEWS: ResultsViewMeta[] = [
  { id: 'best', icon: '⏱', label: 'Best time', caption: 'one best result per distance this season' },
  {
    id: 'season-best',
    icon: '☀',
    label: 'Season best',
    caption: 'where you stand among swimmers born the same year',
  },
  {
    id: 'records',
    icon: '🏅',
    label: 'Personal bests',
    caption: 'career bests with club and national deltas',
  },
  { id: 'progress', icon: '📈', label: 'Progress', caption: 'every attempt at one distance, oldest first' },
];

/**
 * Имя третьего фильтра зависит от того, есть ли у пловца официальные рекорды: с рекордами —
 * «Records & PB» (и в панели рекорды идут отдельной секцией сверху), без них — просто
 * «Personal bests». Слово «Records» у пловца без рекордов обещало бы то, чего нет, а прятать
 * фильтр целиком нельзя: за ним личники карьеры, а они есть у каждого (решение Влада).
 */
export const recordsViewLabel = (recordsHeld?: number | null) =>
  (recordsHeld ?? 0) > 0 ? 'Records & PB' : 'Personal bests';

/**
 * Полоса фильтров таба Results. На мобайле чипы переносятся на вторую строку
 * (`flex-wrap`) — горизонтального скролла на 375px быть не должно.
 *
 * `recordsHeld` — тот же счётчик, что в шапке страницы (`profile.recordsHeld`): при нуле
 * третий чип называется «Personal bests» и бейджа не носит, иначе — «Records & PB» с числом.
 */
export function ResultsFilters({
  view, onView, recordsHeld,
}: {
  view: ResultsView;
  onView: (next: ResultsView) => void;
  recordsHeld?: number | null;
}) {
  const hasRecords = (recordsHeld ?? 0) > 0;
  const active = RESULTS_VIEWS.find((v) => v.id === view) ?? RESULTS_VIEWS[0];

  return (
    <div className="deep-filters">
      <div className="deep-filter-row" role="group" aria-label="Results view">
        {RESULTS_VIEWS.map((v) => (
          <button
            key={v.id}
            type="button"
            onClick={() => onView(v.id)}
            aria-pressed={view === v.id}
            className={`deep-filter-chip${view === v.id ? ' deep-filter-chip--active' : ''}`}
          >
            <span aria-hidden="true">{v.icon}</span>
            <span className="deep-filter-chip__label">
              {v.id === 'records' ? recordsViewLabel(recordsHeld) : v.label}
            </span>
            {v.id === 'records' && hasRecords && (
              <span className="deep-filter-chip__badge">{recordsHeld}</span>
            )}
          </button>
        ))}
      </div>
      <div className="deep-filter-caption">{active.caption}</div>
    </div>
  );
}

/**
 * Фильтр Best time: одна дистанция — одна строка, лучшее время за выбранный сезон.
 *
 * `sbKeys` — дисциплины, где это время ПЕРВОЕ среди сверстников: такая строка получает
 * бейдж SB вместо BEST (правило Влада: первое место в season best носится как рекорд).
 * Набор приходит из того же ответа `/season-ranks`, что и панель Season best, — иначе
 * бейдж и таблица мест могли бы разойтись.
 */
export function ResultsPanel({
  rows, swimmerId, gender, sbKeys, state,
}: {
  rows: SwimmerBestTime[] | null;
  swimmerId: number;
  gender: 'male' | 'female';
  sbKeys?: ReadonlySet<string>;
  state: PanelLoad;
}) {
  if (state.error) return <PanelEmpty>Could not load best times.</PanelEmpty>;
  if (!rows) return <PanelEmpty>{noDataText(state.loading, 'No best times for this season.')}</PanelEmpty>;
  if (rows.length === 0) return <PanelEmpty>No swims in this season yet.</PanelEmpty>;

  return (
    <>
      <PanelHead
        title="Best times"
        hint="one best result per distance"
        right={<span className="deep-legend">🏆 championship</span>}
      />
      <div className="deep-list">
        {rows.map((r) => {
          const row: ResultRowData = {
            ...r,
            badge: sbKeys?.has(r.disciplineKey) ? 'sb' : 'best',
          };
          return <SwimmerResultRow key={r.resultId} row={row} swimmerId={swimmerId} gender={gender} />;
        })}
      </div>
    </>
  );
}

/** «+3420» мс → «+3.42»; 0 — сам лидер. */
function gapLabel(ms: number): string {
  if (ms <= 0) return '—';
  return `+${(ms / 1000).toFixed(2)}`;
}

/**
 * Фильтр Season best: не «моё лучшее время», а МЕСТО среди сверстников — пловцов того же
 * года рождения и пола — по их лучшим временам сезона.
 *
 * Строки результатов берутся из того же ответа `/best-times`, что и фильтр Best time:
 * второго «лучшего времени сезона» на клиенте быть не должно. Дисциплины, которых нет в
 * ответе с местами, показываем БЕЗ места (прочерк), а не молча объявляем первыми.
 */
export function SeasonBestPanel({
  rows, ranks, swimmerId, season, isFallbackSeason = false, state,
}: {
  rows: SwimmerBestTime[] | null;
  ranks: SwimmerSeasonRanks | null;
  swimmerId: number;
  /** Сезон, за который посчитаны места. null — сезонов у пловца нет вовсе. */
  season: number | null;
  /**
   * Карусель стоит на ∞, а места показаны за витринный сезон. Панель ОБЯЗАНА сказать это
   * вслух: иначе адрес говорит «за карьеру», а цифры — за один сезон.
   */
  isFallbackSeason?: boolean;
  state: PanelLoad;
}) {
  // Сезонов нет совсем — сравнивать не с чем, и запрос не уходил: без этой ветки панель
  // висела бы на «Loading…» вечно, ожидая данных, которых никто не просил.
  if (season == null) {
    return <PanelEmpty>No seasons to compare yet.</PanelEmpty>;
  }

  if (state.error) return <PanelEmpty>Could not load places for this season.</PanelEmpty>;
  if (!rows || !ranks) {
    return <PanelEmpty>{noDataText(state.loading, 'No places for this season.')}</PanelEmpty>;
  }

  if (!ranks.groupLabel) {
    return <PanelEmpty>No birth year on file, so there is no age group to compare with.</PanelEmpty>;
  }
  if (rows.length === 0) return <PanelEmpty>No swims in this season yet.</PanelEmpty>;

  const byKey = new Map(rows.map((r) => [r.disciplineKey, r]));
  const ranked = ranks.rows.filter((r) => byKey.has(r.disciplineKey));

  return (
    <>
      <PanelHead
        title={`Among ${ranks.groupLabel}`}
        hint={`place by best time of season ${ranks.label}, swimmers born the same year`}
        right={<span className="deep-legend">SB = fastest in the group</span>}
      />

      {isFallbackSeason && (
        <div className="deep-scope-note">
          The carousel is on <strong>all seasons</strong>, but places only exist inside one
          season — showing <strong>{ranks.label}</strong>.
        </div>
      )}

      {ranked.length === 0 ? (
        <PanelEmpty>No comparable swims in this season yet.</PanelEmpty>
      ) : (
        <div className="deep-pb-table">
          <div className="deep-pb-row deep-rank-row deep-pb-row--head">
            <span>Place</span>
            <span>Event</span>
            <span>Best time</span>
            <span>Group</span>
            <span>Behind leader</span>
          </div>
          {ranked.map((rank) => {
            const row = byKey.get(rank.disciplineKey)!;
            return (
              // Вся строка — ссылка на список этой связки: адрес несёт сезон, возраст, пол,
              // стиль, дистанцию и бассейн, чтобы страница открылась сразу на нужном срезе,
              // а не на своих умолчаниях.
              <a
                key={rank.disciplineKey}
                className="deep-pb-row deep-rank-row deep-rank-row--link"
                href={routes.seasonBest({
                  season,
                  age: ranks.age,
                  gender: ranks.gender,
                  stroke: row.stroke,
                  distance: row.distance,
                  poolType: row.poolType,
                  swimmerId,
                })}
              >
                <span
                  className={`deep-rank-place${holdsSeasonBest(rank) ? ' deep-rank-place--first' : ''}`}
                >
                  #{rank.rank}
                </span>
                <EventPlate stroke={row.stroke} distance={row.distance} poolType={row.poolType} />
                <span className="deep-pb-time">
                  <UI_SwimTime time={row.time ?? '—'} quality={row.quality} marker="chip" chipSize="sm" />
                  {holdsSeasonBest(rank) && !row.quality && <span className="deep-chip-sb">SB</span>}
                </span>
                <span className="deep-pb-pts">
                  {rank.peerCount < MIN_PEERS_FOR_SB ? 'alone' : `of ${rank.peerCount}`}
                </span>
                <span className="deep-delta">
                  {gapLabel(rank.gapToLeaderMs)}
                  <span className="deep-rank-row__go" aria-hidden="true">→</span>
                </span>
              </a>
            );
          })}
        </div>
      )}

      <div className="deep-legend deep-legend--block">
        Open a row to see the full list for that event and age group. Places are counted among
        the meets we have imported, not from an official ranking. Equal times share a place;
        «alone» means nobody born the same year swam this event in our data, so there is no
        place to award.
      </div>
    </>
  );
}

/** Дельта в мс → «+0.44» / «record». Отрицательная дельта означает «быстрее эталона». */
function DeltaCell({ ms, holds }: { ms?: number | null; holds: boolean }) {
  if (holds) return <span className="deep-delta deep-delta--holds">record</span>;
  if (ms == null) return <span className="deep-delta deep-delta--none">—</span>;
  const seconds = (ms / 1000).toFixed(2);
  return <span className="deep-delta">+{seconds}</span>;
}

/**
 * Подпись рекорда: «Israel · age 12» / «Israel · masters». Ступень показываем только там,
 * где она есть, — у открытой категории AgeKey пустой.
 */
function recordScope(r: SwimmerHeldRecord): string {
  const region = r.regionType === 'country' ? r.regionCode : r.regionType;
  const step = r.ageKey ? `${r.category} ${r.ageKey}` : r.category;
  return `${region} · ${step}`;
}

/**
 * Секция официальных рекордов НАД таблицей личников — показывается только тому, кто их
 * держит (решение Влада: есть рекорды → «Records & PB» и рекорды впереди отдельной секцией).
 *
 * ⚠ Держатель в справочнике записан СТРОКОЙ имени, `SwimmerId` у рекорда нет — тёзка заберёт
 * чужой рекорд. Подпись под секцией обязана это признавать, а не делать вид, что связь точная.
 */
function HeldRecordsSection({ records }: { records: SwimmerHeldRecord[] }) {
  return (
    <div className="deep-records-block">
      <PanelHead
        title={records.length === 1 ? 'Official record' : `Official records · ${records.length}`}
        hint="records where the federation register names this swimmer as the holder"
      />
      <div className="deep-list">
        {records.map((r, i) => (
          <div key={`${r.regionCode}-${r.category}-${r.ageKey}-${r.stroke}-${r.distance}-${i}`}
            className="deep-record-row"
          >
            <span className="deep-record-row__crown" aria-hidden="true">🏆</span>
            <EventPlate stroke={r.stroke} distance={r.distance} poolType={r.poolType} />
            <span className="deep-record-row__scope">
              <span className="deep-record-row__region">{recordScope(r)}</span>
              {r.date && <span className="deep-record-row__date">{r.date}</span>}
            </span>
            <span className="deep-record-row__time">
              <UI_SwimTime time={r.time} quality={r.quality} marker="chip" chipSize="sm" />
            </span>
          </div>
        ))}
      </div>
      <div className="deep-legend deep-legend--block">
        The register stores the holder as a name, not as a swimmer id, so a namesake can show
        up here.
      </div>
    </div>
  );
}

/**
 * Фильтр Records &amp; PB: личные рекорды карьеры + дельты. Тумблер бассейна — единственный
 * локальный фильтр. Если пловец держит официальные рекорды, они идут ОТДЕЛЬНОЙ секцией
 * сверху: это разные вещи — рекорд из справочника федерации и «моё лучшее за карьеру».
 */
export function PersonalBestsPanel({
  rows, poolType, onPoolType, records, state,
}: {
  rows: SwimmerPersonalBest[] | null;
  poolType: string;
  onPoolType: (pool: string) => void;
  records?: SwimmerHeldRecord[] | null;
  state: PanelLoad;
}) {
  return (
    <>
      {records && records.length > 0 && <HeldRecordsSection records={records} />}

      <PanelHead
        title="Personal bests"
        hint="career best per distance"
        right={
          <div className="deep-seg" role="group" aria-label="Pool length">
            {['25m', '50m'].map((p) => (
              <button
                key={p}
                type="button"
                onClick={() => onPoolType(p)}
                aria-pressed={poolType === p}
                className={poolType === p ? 'active' : ''}
              >
                {p}
              </button>
            ))}
          </div>
        }
      />

      {state.error ? (
        <PanelEmpty>Could not load personal bests.</PanelEmpty>
      ) : !rows ? (
        <PanelEmpty>{noDataText(state.loading, 'No personal bests in this pool.')}</PanelEmpty>
      ) : rows.length === 0 ? (
        <PanelEmpty>No results in this pool yet.</PanelEmpty>
      ) : (
        <div className="deep-pb-table">
          <div className="deep-pb-row deep-pb-row--head">
            <span>Event</span>
            <span>Best time</span>
            <span>Where and when</span>
            <span>Pts</span>
            <span>Δ club</span>
            <span>Δ Israel</span>
          </div>
          {rows.map((r) => (
            <div key={r.resultId} className="deep-pb-row">
              <EventPlate stroke={r.stroke} distance={r.distance} poolType={r.poolType} />
              <span className="deep-pb-time">
                <UI_SwimTime time={r.time ?? '—'} quality={r.quality} marker="chip" chipSize="sm" />
              </span>
              <span className="deep-pb-where" dir="auto">
                {r.competition.isChampionship && <span aria-hidden="true">🏆 </span>}
                {r.competition.name}
                <span className="deep-pb-date"> · {r.date}</span>
              </span>
              <span className="deep-pb-pts">{r.points ?? '—'}</span>
              <span><DeltaCell ms={r.deltaToClubBestMs} holds={r.holdsClubBest} /></span>
              <span>
                <DeltaCell ms={r.deltaToNationalAgeRecordMs} holds={r.holdsNationalAgeRecord} />
                {r.nationalAgeRecordQuality && (
                  <UI_SwimTime
                    time=""
                    quality={r.nationalAgeRecordQuality}
                    marker="icon"
                  />
                )}
              </span>
            </div>
          ))}
        </div>
      )}

      <div className="deep-legend deep-legend--block">
        «record» — the best time in our database belongs to this swimmer. Club deltas are
        computed from the meets we have imported, not from an official club record list.
      </div>
    </>
  );
}

/** Мини-график времени: вниз = быстрее. Без библиотеки — одна полилиния на SVG. */
function ProgressChart({ progress }: { progress: SwimmerProgress }) {
  const points = progress.points.filter((p) => p.timeMs != null && !p.quality);
  if (points.length < 2) return null;

  const W = 640;
  const H = 150;
  const PAD = 14;
  const times = points.map((p) => p.timeMs!);
  const min = Math.min(...times);
  const max = Math.max(...times);
  const span = max - min || 1;

  const xy = points.map((p, i) => {
    const x = PAD + (i * (W - PAD * 2)) / Math.max(1, points.length - 1);
    // Ось Y перевёрнута: быстрее (меньше мс) — выше.
    const y = PAD + ((p.timeMs! - min) / span) * (H - PAD * 2);
    return { x, y, p };
  });

  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="deep-chart" role="img" aria-label="Time progress">
      <polyline
        points={xy.map((c) => `${c.x},${c.y}`).join(' ')}
        fill="none"
        stroke="var(--deep-accent)"
        strokeWidth="2"
      />
      {xy.map((c) => (
        <circle
          key={c.p.resultId}
          cx={c.x}
          cy={c.y}
          r={c.p.isPb ? 5 : 3.5}
          fill={c.p.isPb ? 'var(--deep-gold)' : 'var(--deep-accent)'}
        >
          <title>{`${c.p.date} · ${c.p.time ?? ''}`}</title>
        </circle>
      ))}
    </svg>
  );
}

/** Таб Progress: выбор дистанции → график + все попытки. */
export function ProgressPanel({
  distances, selected, onSelect, progress, swimmerId, gender, state, progressState,
}: {
  distances: SwimmerBestTime[] | null;
  selected: string | null;
  onSelect: (key: string) => void;
  progress: SwimmerProgress | null;
  swimmerId: number;
  gender: 'male' | 'female';
  /** Загрузка списка дистанций (лучшие времена за карьеру). */
  state: PanelLoad;
  /** Загрузка самой истории выбранной дистанции — она едет вторым запросом. */
  progressState: PanelLoad;
}) {
  if (state.error) return <PanelEmpty>Could not load the list of events.</PanelEmpty>;
  // Раньше здесь стояло «No swims to build progress from yet» на ЛЮБОЙ пустой список —
  // и пока список ехал, панель уверяла, что заплывов нет вовсе.
  if (!distances) {
    return <PanelEmpty>{noDataText(state.loading, 'No events to build progress from.')}</PanelEmpty>;
  }
  if (distances.length === 0) return <PanelEmpty>No swims to build progress from yet.</PanelEmpty>;

  return (
    <>
      <PanelHead title="Progress" hint="every attempt at one distance, oldest first" />

      <div className="deep-discipline-row">
        {distances.map((d) => (
          <button
            key={d.disciplineKey}
            type="button"
            onClick={() => onSelect(d.disciplineKey)}
            aria-pressed={selected === d.disciplineKey}
            className={`deep-discipline${selected === d.disciplineKey ? ' deep-discipline--active' : ''}`}
          >
            <span className="deep-discipline__dist">{d.distance}</span>
            <span className="deep-discipline__stroke">{d.stroke}</span>
            <span className="deep-discipline__pool">{d.poolType}</span>
          </button>
        ))}
      </div>

      {progressState.error ? (
        <PanelEmpty>Could not load this event.</PanelEmpty>
      ) : !progress ? (
        <PanelEmpty>{noDataText(progressState.loading, 'Pick a distance.')}</PanelEmpty>
      ) : progress.points.length === 0 ? (
        <PanelEmpty>No attempts at this distance.</PanelEmpty>
      ) : (
        <>
          <ProgressChart progress={progress} />
          <div className="deep-list">
            {progress.points.map((p) => (
              <SwimmerResultRow
                key={p.resultId}
                swimmerId={swimmerId}
                gender={gender}
                row={{
                  stroke: progress.stroke,
                  distance: progress.distance,
                  poolType: progress.poolType,
                  time: p.time,
                  quality: p.quality,
                  points: p.points,
                  place: p.place,
                  heatType: p.heatType,
                  ageInSeason: p.ageInSeason,
                  isMasters: p.isMasters,
                  splits: null,
                  date: p.date,
                  competition: p.competition,
                  resultId: p.resultId,
                  badge: p.isPb ? 'pb' : null,
                }}
              />
            ))}
          </div>
        </>
      )}
    </>
  );
}

/** Таб History: сезоны и старты карьеры (тот же summary, но за всё время). */
export function HistoryPanel({
  career, swimmerId, state,
}: { career: SwimmerSummary | null; swimmerId: number; state: PanelLoad }) {
  if (state.error) return <PanelEmpty>Could not load the career history.</PanelEmpty>;
  if (!career) return <PanelEmpty>{noDataText(state.loading, 'No career data yet.')}</PanelEmpty>;
  if (career.competitions.length === 0) return <PanelEmpty>No competitions yet.</PanelEmpty>;

  // Группировка по сезону — из даты старта: сезон начинается осенью, поэтому месяцы
  // сентября и позже принадлежат сезону года начала (SeasonMath на сервере, тот же счёт).
  const seasonOf = (date: string) => {
    const [y, m] = date.split('-').map(Number);
    return m >= 9 ? y : y - 1;
  };
  const groups = new Map<number, SwimmerCompetition[]>();
  career.competitions.forEach((meet) => {
    const s = seasonOf(meet.date);
    if (!groups.has(s)) groups.set(s, []);
    groups.get(s)!.push(meet);
  });

  return (
    <>
      <PanelHead title="Career" hint={`${groups.size} seasons · ${career.competitionCount} meets`} />
      {[...groups.entries()].sort((a, b) => b[0] - a[0]).map(([season, meets]) => (
        <div key={season} className="deep-history-season">
          <div className="deep-history-season__label">{seasonLabel(season)}</div>
          <div className="deep-list">
            {meets.map((meet) => (
              <CompetitionRow key={`${meet.eventId ?? 'c'}-${meet.competitionId}`} meet={meet} swimmerId={swimmerId} />
            ))}
          </div>
        </div>
      ))}
    </>
  );
}

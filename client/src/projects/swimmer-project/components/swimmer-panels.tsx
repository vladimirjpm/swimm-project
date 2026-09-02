import React, { useState } from 'react';
import UI_SwimTime from '../../components/mix/swim-time/swim-time';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import SwimmerResultRow, { type ResultRowData } from './swimmer-result-row';
import SwimRow, { swimRowStrokeLabel } from '../../components/swim-row/swim-row';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PoolIcon from '../../components/mix/pool-icon/pool-icon';
import UI_SeasonNotice from '../../components/mix/season-notice/season-notice';
import { MIN_PEERS_FOR_RANK } from '../../components/mix/rank-of-peers/rank-of-peers';
import { useFavoritesContext } from '../../../hooks/favorites-context';
import UI_H2HMiniCard from '../../components/mix/h2h/h2h-mini-card';
import UI_H2HEmptySlot from '../../components/mix/h2h/h2h-empty-slot';
import UI_H2HCompareHeader from '../../components/mix/h2h/h2h-compare-header';
import UI_H2HEventCard from '../../components/mix/h2h/h2h-event-card';
import UI_H2HPoolRow from '../../components/mix/h2h/h2h-pool-row';
import UI_H2HDivider from '../../components/mix/h2h/h2h-divider';
import UI_H2HRivalPicker from '../../components/mix/h2h/h2h-rival-picker';
import { routes } from '../../../utils/routes';
import { peerGroupLabel, seasonLabel } from '../../../utils/helpers/season-helper';
import type {
  SwimmerBestTime, SwimmerCompare, SwimmerCompareSwim, SwimmerCompetition, SwimmerDisciplineRank,
  SwimmerPersonalBest, SwimmerProgress, SwimmerSearchHit, SwimmerSeasonRanks, SwimmerSummary,
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
/** Порог один на продукт и живёт рядом с подписью «alone», которую он же включает. */
const MIN_PEERS_FOR_SB = MIN_PEERS_FOR_RANK;

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

/**
 * Сезон старта из его даты: сезон начинается осенью, поэтому сентябрь и позже принадлежат
 * сезону года начала. Тот же счёт, что у SeasonMath на сервере.
 */
const seasonOfDate = (date: string) => {
  const [y, m] = date.split('-').map(Number);
  return m >= 9 ? y : y - 1;
};

/**
 * Таб Season: сводка периода + его старты (❄/☀ выведены из самих соревнований).
 *
 * В режиме ∞ (карусель на «все сезоны») старты разложены ПО СЕЗОНАМ — это всё, чем таб
 * History отличался от этой панели, и ради одной группировки второй таб с тем же запросом
 * не нужен (решение Влада 2026-09-01).
 */
export function SeasonPanel({
  summary, swimmerId, state,
}: { summary: SwimmerSummary | null; swimmerId: number; state: PanelLoad }) {
  if (state.error) return <PanelEmpty>Could not load this season.</PanelEmpty>;
  if (!summary) return <PanelEmpty>{noDataText(state.loading, 'No data for this season.')}</PanelEmpty>;

  const career = summary.season == null;
  const groups = new Map<number, SwimmerCompetition[]>();
  if (career) {
    summary.competitions.forEach((meet) => {
      const s = seasonOfDate(meet.date);
      if (!groups.has(s)) groups.set(s, []);
      groups.get(s)!.push(meet);
    });
  }

  return (
    <>
      <SeasonBanner summary={summary} />
      <PanelHead
        title={career ? 'Career' : 'Competitions'}
        hint={career
          ? `${groups.size} season${groups.size === 1 ? '' : 's'} · ${summary.competitionCount} meets`
            + ` · ${summary.personalBests} personal bests`
          : `${summary.competitionCount} meets · ${summary.personalBests} personal bests`}
      />
      {summary.competitions.length === 0 ? (
        <PanelEmpty>{career ? 'No competitions yet.' : 'No swims in this season yet.'}</PanelEmpty>
      ) : career ? (
        [...groups.entries()].sort((a, b) => b[0] - a[0]).map(([season, meets]) => (
          <div key={season} className="deep-history-season">
            <div className="deep-history-season__label">{seasonLabel(season)}</div>
            <div className="deep-list">
              {meets.map((meet) => (
                <CompetitionRow
                  key={`${meet.eventId ?? 'c'}-${meet.competitionId}`}
                  meet={meet}
                  swimmerId={swimmerId}
                />
              ))}
            </div>
          </div>
        ))
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

/* Отставание от лидера форматирует сама строка (`swimRowGapLabel`) — формат один на продукт. */

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

  // Пустая панель в сентябре — это не «нет заплывов», а «сезон ещё не открыт»: оговорка
  // обязана стоять и здесь, иначе пловец читает пустоту как поломку.
  const seasonNote = <UI_SeasonNotice notice={ranks.season_notice} season={season} />;

  if (rows.length === 0) {
    return (
      <>
        {seasonNote}
        <PanelEmpty>No swims in this season yet.</PanelEmpty>
      </>
    );
  }

  const byKey = new Map(rows.map((r) => [r.disciplineKey, r]));
  const ranked = ranks.rows.filter((r) => byKey.has(r.disciplineKey));
  // Пол группы сверстников — вход дуги уровня: нормативы у мужчин и женщин разные,
  // а в ответе он приходит свободной строкой.
  const rankGender: 'male' | 'female' | 'none' =
    ranks.gender === 'male' || ranks.gender === 'female' ? ranks.gender : 'none';

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

      {/* Сентябрь–февраль: витрина ещё держит прошлый сезон. Без этой строки пловец видит
          прошлогодние места как сегодняшние — или пустоту, если выбрал новый сезон руками
          (docs/season-boundary-rule.md). */}
      {seasonNote}

      {ranked.length === 0 ? (
        <PanelEmpty>No comparable swims in this season yet.</PanelEmpty>
      ) : (
        <div className="deep-list">
          {ranked.map((rank) => {
            const row = byKey.get(rank.disciplineKey)!;
            const holds = holdsSeasonBest(rank);
            return (
              // Вся строка — ссылка на список этой связки: адрес несёт сезон, возраст, пол,
              // стиль, дистанцию и бассейн, чтобы страница открылась сразу на нужном срезе,
              // а не на своих умолчаниях.
              <SwimRow
                key={rank.disciplineKey}
                className="deep-swim-row"
                href={routes.seasonBest({
                  season,
                  age: ranks.age,
                  gender: ranks.gender,
                  stroke: row.stroke,
                  distance: row.distance,
                  poolType: row.poolType,
                  swimmerId,
                })}
                stroke={row.stroke ?? ''}
                distance={row.distance}
                poolType={row.poolType}
                time={row.time}
                quality={row.quality}
                splits={row.splits}
                // Место среди СВЕРСТНИКОВ, а не в протоколе: медальный кружок тут соврал бы.
                // «of 36» стоит ПОД местом, а не во второй линии: две цифры читаются только
                // вместе — «#1» без круга не отличает первого из 36 от первого из двух.
                place={{
                  kind: 'rank',
                  value: rank.rank,
                  isFirst: holds,
                  peerCount: rank.peerCount,
                }}
                badge={holds ? 'sb' : null}
                competition={{
                  name: row.competition.name,
                  isChampionship: row.competition.isChampionship,
                }}
                meetPlacement="line2"
                date={row.date}
                points={row.points ?? null}
                gapMs={rank.gapToLeaderMs}
                level={{
                  gender: rankGender,
                  ageInSeason: row.ageInSeason,
                  isMasters: !!row.isMasters,
                }}
              />
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
          <SwimRow
            key={`${r.regionCode}-${r.category}-${r.ageKey}-${r.stroke}-${r.distance}-${i}`}
            className="deep-swim-row deep-record-row"
            stroke={r.stroke ?? ''}
            distance={r.distance}
            poolType={r.poolType}
            time={r.time}
            quality={r.quality}
            // Места у записи справочника нет: это не заплыв протокола, а строка реестра.
            place={{ kind: 'none' }}
            // «🏆 ISR · age 12» — область и ступень рекорда встают на место старта: именно
            // они отвечают на вопрос «чей это рекорд».
            competition={{ name: recordScope(r), isChampionship: true }}
            meetPlacement="line1"
            date={r.date}
          />
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
  rows, poolType, onPoolType, records, gender, age, state,
}: {
  rows: SwimmerPersonalBest[] | null;
  poolType: string;
  onPoolType: (pool: string) => void;
  records?: SwimmerHeldRecord[] | null;
  /** Нормативы у мужчин и женщин разные — без пола дуга уровня врёт. */
  gender: 'male' | 'female';
  /** Возраст в витринном сезоне — тот же, по которому сервер считал обе дельты. */
  age?: number | null;
  state: PanelLoad;
}) {
  // Круг сравнения называется ОДИН РАЗ — в подписи панели (решение Влада 2026-08-27).
  // Он один и тот же у ОБЕИХ дельт и у всех строк, и повторять «girls 12» дважды в каждой
  // строке — шум. Формулировка та же, что у панели Season best и шапки /season-best.
  const peers = peerGroupLabel({ age, gender });
  const hint = peers
    ? `career best per distance · deltas among ${peers}`
    : 'career best per distance';

  return (
    <>
      {records && records.length > 0 && <HeldRecordsSection records={records} />}

      <PanelHead
        title="Personal bests"
        hint={hint}
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
        <div className="deep-list">
          {rows.map((r) => (
            <SwimRow
              key={r.resultId}
              className="deep-swim-row"
              stroke={r.stroke ?? ''}
              distance={r.distance}
              poolType={r.poolType}
              time={r.time}
              quality={r.quality}
              // У личного рекорда места нет вовсе — прочерк держит колонку, чтобы плитки
              // дисциплин стояли в ряд.
              place={{ kind: 'none' }}
              competition={{
                name: r.competition.name,
                isChampionship: r.competition.isChampionship,
              }}
              meetPlacement="line2"
              date={r.date}
              points={r.points ?? null}
              // Возраста заплыва в личниках нет (это лучшее за КАРЬЕРУ), но обычной шкале
              // нормативов он и не нужен — она зависит только от пола, бассейна и дистанции.
              level={{ gender }}
              // Дельты — свойство ВРЕМЕНИ, поэтому едут в компонент времени и встают
              // строками под ним. Там же пустые отбрасываются: нет эталона — нет строки.
              deltas={[
                {
                  label: 'Δ club',
                  ms: r.deltaToClubBestMs,
                  holds: r.holdsClubBest,
                  title: peers
                    ? `Compared with the best time in the club among ${peers}`
                    : 'Compared with the best time in the club among swimmers of the same age and gender',
                },
                {
                  label: 'Δ Israel',
                  ms: r.deltaToNationalAgeRecordMs,
                  holds: r.holdsNationalAgeRecord,
                  quality: r.nationalAgeRecordQuality,
                  title: peers
                    ? `Compared with the national record for ${peers}`
                    : 'Compared with the national age record',
                },
              ]}
            />
          ))}
        </div>
      )}

      <div className="deep-legend deep-legend--block">
        Both deltas compare within the same age and gender. «record» means the best time in
        our database belongs to this swimmer. Club deltas are computed from the meets we have
        imported, not from an official club record list.
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

/**
 * Таб H2H (head-to-head) — макет 1b из `!design_handoff/design_handoff_h2h/`.
 *
 * Экран собран из семейства `UI_H2H*` (`components/mix/h2h/`): шапка сравнения с двумя
 * зеркальными мини-карточками и статами, карточки заплывов с полосой на каждый бассейн,
 * разделитель «only one swimmer» и выбор соперника (избранное + поиск).
 *
 * Соперник выбирается ВРУЧНУЮ (решение Влада 2026-09-01): таб отвечает на вопрос «как я
 * против ВОТ ЭТОГО пловца», а не «кто мои соперники», — автосписка соседей по времени тут
 * нет. Сравнивать можно с кем угодно, включая другой год рождения и другой пол.
 */
export function H2HPanel({
  compare, query, onQuery, hits, hitsState, onPick, onClear, rivalId, swimmerId, profileName,
  state,
}: {
  compare: SwimmerCompare | null;
  /** Строка поиска — состояние живёт на странице, чтобы переживать смену сезона. */
  query: string;
  onQuery: (q: string) => void;
  hits: SwimmerSearchHit[] | null;
  hitsState: PanelLoad;
  onPick: (id: number) => void;
  onClear: () => void;
  /** Выбранный соперник; null — показываем слот выбора. */
  rivalId: number | null;
  /** Хозяин страницы: его самого из избранного убираем — сравнивать с собой нечего. */
  swimmerId: number;
  /** Имя хозяина страницы: слот «vs» рисуется ещё до того, как приедет сравнение. */
  profileName: string;
  state: PanelLoad;
}) {
  const { isAuthenticated, favorites, favoriteSwimmerIds, toggleFavoriteSwimmer } =
    useFavoritesContext();
  // Пустой слот — кнопка «выбрать»: попапа у него нет (выбор и так стоит под ним), поэтому
  // клик просто уводит курсор в поиск. Иначе слот выглядел бы нажимаемым и не делал ничего.
  const searchRef = React.useRef<HTMLInputElement>(null);

  const favoriteRivals = favorites
    .filter((f) => f.target_type === 'swimmer' && f.swimmer_id != null && f.swimmer_id !== swimmerId)
    // Порядок пользователя: «Me» первым, дальше его сортировка.
    .sort((a, b) => Number(b.is_primary) - Number(a.is_primary) || a.sort_order - b.sort_order)
    .map((f) => ({ id: f.swimmer_id!, name: f.swimmer_name ?? `#${f.swimmer_id}` }));

  const picker = (
    <UI_H2HRivalPicker
      favorites={favoriteRivals}
      query={query}
      onQuery={onQuery}
      hits={hits}
      loading={hitsState.loading}
      error={hitsState.error}
      onPick={onPick}
      inputRef={searchRef}
    />
  );

  // Соперник не выбран: слева хозяин страницы, справа пустой слот. Состояния «никто не
  // выбран» здесь не бывает — левый всегда известен, это его страница.
  if (rivalId == null) {
    return (
      <>
        <PanelHead title="Compare" hint="pick a swimmer to put your best times side by side" />
        <div className="h2h-row" style={{ marginBottom: 12 }}>
          <UI_H2HMiniCard
            swimmer={{ id: swimmerId, name: profileName }}
            align="left"
            isFavorite={isAuthenticated ? favoriteSwimmerIds.has(swimmerId) : null}
            onToggleFavorite={() => toggleFavoriteSwimmer(swimmerId)}
          />
          <div className="h2h-vs">vs</div>
          <UI_H2HEmptySlot onClick={() => searchRef.current?.focus()} />
        </div>
        {picker}
      </>
    );
  }

  if (state.error) {
    return (
      <>
        <PanelHead title="Compare" />
        {picker}
        <PanelEmpty>Could not load this comparison.</PanelEmpty>
      </>
    );
  }
  if (!compare) {
    return (
      <>
        <PanelHead title="Compare" />
        {picker}
        <PanelEmpty>{noDataText(state.loading, 'No comparison yet.')}</PanelEmpty>
      </>
    );
  }

  const scope = compare.season == null ? 'career bests' : `best times of season ${compare.label}`;
  const shared = compare.sharedCount;
  const both = compare.rows.filter((r) => r.pools.some((p) => p.deltaMs != null));
  const oneSided = compare.rows.filter((r) => !r.pools.some((p) => p.deltaMs != null));

  const side = (s: SwimmerCompare['mine'], own: boolean) => ({
    swimmer: {
      id: s.id,
      name: s.name || `#${s.id}`,
      club: s.clubName,
      // Чип макета: «9 y · 2017». Возраст без года рождения не показываем — его нечем
      // проверить читателю.
      ageLabel: s.ageInSeason != null && (s.birthYear ?? 0) > 0
        ? `${s.ageInSeason} y · ${s.birthYear}`
        : (s.birthYear ?? 0) > 0 ? `b. ${s.birthYear}` : null,
    },
    seasonBests: s.seasonBests,
    medals: s.medals,
    bestPoints: s.bestPoints,
    isFavorite: isAuthenticated ? favoriteSwimmerIds.has(s.id) : null,
    onToggleFavorite: () => toggleFavoriteSwimmer(s.id),
    own,
  });

  const eventCard = (row: SwimmerCompare['rows'][number], isOneSided: boolean) => (
    <UI_H2HEventCard
      key={row.key}
      stroke={row.stroke}
      distance={row.distance}
      oneSided={isOneSided}
    >
      {row.pools.map((pool) => (
        <UI_H2HPoolRow
          key={pool.poolType}
          poolType={pool.poolType}
          left={pool.mine ? {
            time: pool.mine.time,
            date: pool.mine.date,
            quality: pool.mine.quality,
            badge: badgeOf(pool.mine),
          } : null}
          right={pool.rival ? {
            time: pool.rival.time,
            date: pool.rival.date,
            quality: pool.rival.quality,
            badge: badgeOf(pool.rival),
          } : null}
          deltaMs={pool.deltaMs}
          // Клик по полосе ведёт в протокол заплыва — того из двух, чей он: у общей пары
          // берём свой (страница принадлежит хозяину).
          href={swimHref(pool.mine ?? pool.rival, pool.mine ? compare.mine.id : compare.rival.id)}
        />
      ))}
    </UI_H2HEventCard>
  );

  return (
    <>
      <PanelHead
        title="Compare"
        hint={shared > 0
          ? `${scope} · ${shared} comparable swim${shared === 1 ? '' : 's'}`
          : `${scope} · nothing you both swam in the same pool`}
        right={(
          <button type="button" className="h2h-clear" onClick={onClear}>
            Clear
          </button>
        )}
      />

      <UI_H2HCompareHeader
        left={side(compare.mine, true)}
        right={side(compare.rival, false)}
        leftFaster={compare.mineFaster}
        rightFaster={compare.rivalFaster}
        ties={compare.ties}
        // За карьеру мест среди сверстников нет — строка не рисуется, а не показывает 0:0.
        showSeasonBests={compare.season != null}
      />

      {compare.rows.length === 0 ? (
        <PanelEmpty>Neither of you has a counted swim in this period.</PanelEmpty>
      ) : (
        <div className="deep-list" style={{ marginTop: 12 }}>
          {both.map((row) => eventCard(row, false))}

          {oneSided.length > 0 && <UI_H2HDivider text="only one swimmer" />}
          {oneSided.map((row) => eventCard(row, true))}
        </div>
      )}

      <div className="deep-legend deep-legend--block">
        One card per distance, one line per pool: the best time each of you swam there. Short
        course and long course are never compared with each other — a 25m time is faster by the
        pool alone. The gap is yours minus theirs, so a negative number means you are faster.
        SB marks the fastest time among swimmers born the same year; REC means the time is not
        slower than the national record of that age. Relays, DSQ and flagged swims are left out.
      </div>

      <div style={{ marginTop: 12 }}>{picker}</div>
    </>
  );
}

/** Бейдж строки: рекорд весомее места среди сверстников, поэтому REC перебивает SB. */
function badgeOf(swim: SwimmerCompareSwim): 'SB' | 'REC' | null {
  if (swim.holdsRecord) return 'REC';
  return swim.isSeasonBest ? 'SB' : null;
}

/** Ссылка на протокол заплыва — тот же адрес, что открывают строки остальных табов. */
function swimHref(swim: SwimmerCompareSwim | null | undefined, swimmerId: number): string | undefined {
  const competitionId = swim?.competition?.id;
  return competitionId != null
    ? `/results?competitionId=${competitionId}&tab=swims&swimmerId=${swimmerId}`
    : undefined;
}

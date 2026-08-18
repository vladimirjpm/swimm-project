import React, { useState } from 'react';
import UI_SwimTime from '../../components/mix/swim-time/swim-time';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import SwimmerResultRow, { type ResultRowData } from './swimmer-result-row';
import type {
  SwimmerBestTime, SwimmerCompetition, SwimmerPersonalBest, SwimmerProgress, SwimmerSummary,
} from '../use-swimmer-page';

/**
 * Панели табов страницы спортсмена (BLOCKS.md §5–9). Каждая — независимый блок: свои данные,
 * никаких собственных фильтров, кроме тумблера 25m/50m в PB (единственное исключение §7).
 *
 * Пустое состояние — норма, а не край: панель рендерится с текстом «нет заплывов в этом
 * сезоне», карусель и табы остаются на месте.
 */

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
export function SeasonPanel({ summary, swimmerId }: { summary: SwimmerSummary | null; swimmerId: number }) {
  if (!summary) return <PanelEmpty>Loading…</PanelEmpty>;

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

/** Таб Results: одна дистанция — одна строка, лучшее время за выбранный сезон. */
export function ResultsPanel({
  rows, swimmerId, gender,
}: { rows: SwimmerBestTime[] | null; swimmerId: number; gender: 'male' | 'female' }) {
  if (!rows) return <PanelEmpty>Loading…</PanelEmpty>;
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
          const row: ResultRowData = { ...r, badge: 'best' };
          return <SwimmerResultRow key={r.resultId} row={row} swimmerId={swimmerId} gender={gender} />;
        })}
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

/** Таб Records & PB: личные рекорды + дельты. Тумблер бассейна — единственный локальный фильтр. */
export function PersonalBestsPanel({
  rows, poolType, onPoolType,
}: {
  rows: SwimmerPersonalBest[] | null;
  poolType: string;
  onPoolType: (pool: string) => void;
}) {
  return (
    <>
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

      {!rows ? (
        <PanelEmpty>Loading…</PanelEmpty>
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
              <span className="deep-pb-event">{r.distance} {r.stroke}</span>
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
  distances, selected, onSelect, progress, swimmerId, gender,
}: {
  distances: SwimmerBestTime[] | null;
  selected: string | null;
  onSelect: (key: string) => void;
  progress: SwimmerProgress | null;
  swimmerId: number;
  gender: 'male' | 'female';
}) {
  if (!distances || distances.length === 0) {
    return <PanelEmpty>No swims to build progress from yet.</PanelEmpty>;
  }

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

      {!progress ? (
        <PanelEmpty>Pick a distance.</PanelEmpty>
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
export function HistoryPanel({ career, swimmerId }: { career: SwimmerSummary | null; swimmerId: number }) {
  if (!career) return <PanelEmpty>Loading…</PanelEmpty>;
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
          <div className="deep-history-season__label">
            {season}/{String((season + 1) % 100).padStart(2, '0')}
          </div>
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

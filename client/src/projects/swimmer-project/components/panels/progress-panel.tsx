import SwimmerResultRow from '../swimmer-result-row';
import type { SwimmerBestTime, SwimmerProgress } from '../../use-swimmer-page';
import { type PanelLoad, noDataText, PanelEmpty, PanelHead } from '../swimmer-panels';

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
                  round: p.round,
                  timeFail: p.timeFail,
                  competitionIsAward: p.competitionIsAward,
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

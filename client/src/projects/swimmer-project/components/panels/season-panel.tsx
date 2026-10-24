import UI_MedalIcon from '../../../components/mix/medal-icon/medal-icon';
import { routes } from '../../../../utils/routes';
import { seasonLabel } from '../../../../utils/helpers/season-helper';
import HelperResults from '../../../../utils/helpers/helper-results';
import type { SwimmerCompetition, SwimmerSummary } from '../../use-swimmer-page';
import { type PanelLoad, noDataText, PanelEmpty, PanelHead } from '../swimmer-panels';

/** Шапка панели: очки сезона слева, медали справа (BLOCKS.md §5). */
export function SeasonBanner({ summary }: { summary: SwimmerSummary }) {
  return (
    <div className="deep-season-banner">
      <div>
        <div className="deep-season-banner__points">{summary.points.toLocaleString('en-US')}</div>
        <div className="deep-season-banner__sub">
          {summary.season == null ? 'career' : `season ${summary.label}`}
          {' · '}{HelperResults.swimsLabel(summary.swims, summary.entered ?? summary.swims)} swims · {summary.events} events
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
      href={routes.competitionSwims(meet.competitionId, { swimmerId, eventId: meet.eventId })}
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
        {HelperResults.swimsLabel(meet.swims, meet.entered ?? meet.swims)} swims · {meet.points} pts
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

import DeepViewChips from '../../../components/deep/view-chips';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';
import SwimmerResultRow, { type ResultRowData } from '../swimmer-result-row';
import type { SwimmerBestTime, SwimmerUnderReviewSwim } from '../../use-swimmer-page';
import { type PanelLoad, noDataText, PanelEmpty, PanelHead } from '../swimmer-panels';

/**
 * Фильтры внутри таба Results. Раньше это были отдельные табы «Records & PB» и «Progress»:
 * шесть плиток не помещались в мобайл, а по смыслу все четыре отвечают на один вопрос —
 * «как я плыву», отличаясь только точкой отсчёта (сезон, сверстники, карьера, время).
 *
 * `sb` (первое место среди сверстников) намеренно живёт НЕ здесь, а бейджем на строке:
 * фильтр — это точка зрения, а не достижение.
 */
export type ResultsView = 'best' | 'season-best' | 'records' | 'progress' | 'under-review';

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
  {
    id: 'under-review',
    icon: '⚠',
    label: 'Under review',
    caption: 'shown as printed in the protocol, but not counted in bests, records or levels',
  },
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
 *
 * `underReviewCount` (`profile.underReviewCount`) — помеченных ошибок протокола за карьеру.
 * Чип «Under review» с числом есть ТОЛЬКО когда они есть: у почти всех пловцов их ноль, и
 * пустой вид был бы шумом. Собраны отдельно, чтобы их заметили (решение Влада 06.10.2026).
 */
export function ResultsFilters({
  view, onView, recordsHeld, underReviewCount,
}: {
  view: ResultsView;
  onView: (next: ResultsView) => void;
  recordsHeld?: number | null;
  underReviewCount?: number | null;
}) {
  return (
    <DeepViewChips<ResultsView>
      ariaLabel="Results view"
      active={view}
      onSelect={onView}
      chips={RESULTS_VIEWS
        .filter((v) => v.id !== 'under-review' || (underReviewCount ?? 0) > 0)
        .map((v) => ({
          id: v.id,
          icon: v.icon,
          label: v.id === 'records' ? recordsViewLabel(recordsHeld) : v.label,
          // Бейдж — у «Records & PB», когда рекорды есть, и у «Under review».
          badge: v.id === 'records' ? recordsHeld : v.id === 'under-review' ? underReviewCount : null,
          caption: v.caption,
        }))}
    />
  );
}

/**
 * Помеченное время рядом с лучшим — во второй линии строки Best time: «01:53.09 ⚠ Under
 * review». Время — только через `UI_SwimTime` (он же даёт объяснялку по клику на чип).
 */
function UnderReviewBeside({ swim }: { swim: SwimmerUnderReviewSwim }) {
  if (!swim.time) return null;
  return (
    <span className="swimmer-under-review" title={`${swim.competition.name} · ${swim.date}`}>
      <UI_SwimTime
        time={swim.time}
        quality={swim.quality}
        marker="chip"
        chipSize="sm"
        className="swimmer-under-review__time"
      />
    </span>
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
            // Строка из одного помеченного заплыва лучшей не бывает — чипа нет.
            badge: r.quality ? null : sbKeys?.has(r.disciplineKey) ? 'sb' : 'best',
          };
          return (
            <SwimmerResultRow
              key={r.resultId}
              row={row}
              swimmerId={swimmerId}
              gender={gender}
              extras={r.underReview ? <UnderReviewBeside swim={r.underReview} /> : undefined}
            />
          );
        })}
      </div>
    </>
  );
}

/**
 * Вид «Under review»: все помеченные заплывы карьеры одним списком, от новых к старым.
 * Строки — тот же `SwimmerResultRow`: время и очки как в протоколе + чип, без дуги и BEST
 * (Р76: данные протокола показываем, наши метки достижений — нет). Ссылка ведёт
 * в протокол, где видно, как это время напечатано.
 */
export function UnderReviewPanel({
  rows, swimmerId, gender, state,
}: {
  rows: SwimmerBestTime[] | null;
  swimmerId: number;
  gender: 'male' | 'female';
  state: PanelLoad;
}) {
  if (state.error) return <PanelEmpty>Could not load swims under review.</PanelEmpty>;
  if (!rows) return <PanelEmpty>{noDataText(state.loading, 'No swims under review.')}</PanelEmpty>;
  if (rows.length === 0) return <PanelEmpty>No swims under review.</PanelEmpty>;

  return (
    <>
      <PanelHead
        title="Under review"
        hint="all seasons · these times are printed in the meet protocol but look like errors there"
      />
      <div className="deep-list">
        {rows.map((r) => (
          <SwimmerResultRow key={r.resultId} row={{ ...r, badge: null }} swimmerId={swimmerId} gender={gender} />
        ))}
      </div>
    </>
  );
}

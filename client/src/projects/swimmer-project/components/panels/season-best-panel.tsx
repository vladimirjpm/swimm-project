import SwimRow from '../../../components/swim-row/swim-row';
import UI_SeasonNotice from '../../../components/mix/season-notice/season-notice';
import UI_SeasonTitle from '../../../components/mix/season-notice/season-title';
import { routes } from '../../../../utils/routes';
import type { SwimmerBestTime, SwimmerSeasonRanks } from '../../use-swimmer-page';
import { holdsSeasonBest, type PanelLoad, noDataText, PanelEmpty, PanelHead } from '../swimmer-panels';

/* Отставание от лидера форматирует сама строка (`swimRowGapLabel`) — формат один на продукт. */

/**
 * Фильтр Season best: не «моё лучшее время», а МЕСТО среди сверстников — пловцов того же
 * года рождения (с 18 лет — всех взрослых, Р84) и пола — по их лучшим временам сезона.
 *
 * Строки результатов берутся из того же ответа `/best-times`, что и фильтр Best time:
 * второго «лучшего времени сезона» на клиенте быть не должно. Дисциплины, которых нет в
 * ответе с местами, показываем БЕЗ места (прочерк), а не молча объявляем первыми.
 */
export function SeasonBestPanel({
  rows, ranks, swimmerId, season, isFallbackSeason = false, heldBack = false, state,
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
  /**
   * Карусель на НОВОМ сезоне, а места — за витринный (до зимнего чемпионата, решение Влада
   * 26.10.2026). На экране два сезона сразу, поэтому заголовок «Season 2025/26» — строкой
   * в шапке панели (`UI_SeasonTitle size="lg"`), а не мелкой оговоркой.
   */
  heldBack?: boolean;
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
  // Карусель на новом сезоне — заголовок «Season 2025/26» строкой (в нём и пояснение); иначе
  // прежняя оговорка.
  const seasonNote = heldBack
    ? <UI_SeasonTitle size="lg" notice={ranks.season_notice} season={season} />
    : <UI_SeasonNotice notice={ranks.season_notice} season={season} />;

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

  // Круг у каждой строки свой: мастерский заплыв меряется группой протокола («women 45-49»),
  // обычный — ровесниками того же года рождения. Шапка называет круг, только если он у всех
  // строк один; иначе — возрастной круг, а подсказка говорит, что мастерские строки
  // меряются группой протокола (ссылка строки всё равно ведёт в её собственный срез).
  const circles = new Set(ranked.map((r) => r.groupLabel ?? ranks.groupLabel));
  const oneCircle = circles.size === 1 ? [...circles][0] : null;
  const anyMasters = ranked.some((r) => r.ageGroup);
  const allMasters = ranked.length > 0 && ranked.every((r) => r.ageGroup);
  // С 18 лет круг — все взрослые на обычных стартах, а не ровесники (Р84).
  const adultCircle = ranks.peerAgeTo != null && ranks.peerAgeTo !== ranks.peerAgeFrom;
  const peersText = adultCircle ? 'all adult swimmers 18+' : 'swimmers born the same year';
  const circleHint = allMasters
    ? 'same masters age group of the meet protocol'
    : anyMasters
      ? `${peersText}; masters swims — their protocol age group`
      : peersText;

  return (
    <>
      {/* Другой сезон, чем у карусели, — заголовок первым, над шапкой панели. */}
      {heldBack && seasonNote}
      <PanelHead
        title={`Among ${oneCircle ?? ranks.groupLabel}`}
        hint={`place by best time of season ${ranks.label}, ${circleHint}`}
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
      {!heldBack && seasonNote}

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
                  // Мастерский заплыв ведёт в мастерский срез: место посчитано в группе
                  // протокола, и список обязан показать ровно этот круг.
                  // Обычный — в круг сверстников: год рождения, а с 18 лет — хвост «18+»
                  // (age=18&age_to=99, Р84). Одиночный age=24 открыл бы список, по которому
                  // место НЕ считалось.
                  age: rank.ageGroup ? null : (ranks.peerAgeFrom ?? ranks.age),
                  ageTo: rank.ageGroup || ranks.peerAgeTo === ranks.peerAgeFrom
                    ? null
                    : (ranks.peerAgeTo ?? null),
                  ageGroup: rank.ageGroup ?? null,
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
        «alone» means nobody else in the group swam this event in our data, so there is no
        place to award. From 18, the group is all adults in regular meets, not one birth year.
        Masters swims are placed within their age group of the meet protocol.
      </div>
    </>
  );
}

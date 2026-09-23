import React from 'react';
import '../../components/mix/h2h/h2h.css';
import UI_H2HEventCard from '../../components/mix/h2h/h2h-event-card';
import UI_H2HPoolRow from '../../components/mix/h2h/h2h-pool-row';
import UI_H2HDivider from '../../components/mix/h2h/h2h-divider';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import type { H2HPoolSide } from '../../components/mix/h2h/h2h-pool-row';
import type { RecordCompareRow, RecordCompareSide } from '../../../hooks/useRecordsCompare';
import { RK_STROKES, genderLabel } from '../rk-disciplines';

/**
 * Карточки дисциплин сравнения стран в вёрстке H2H: одна карточка — стиль × дистанция,
 * внутри по полосе на каждый бассейн (`UI_H2HPoolRow`).
 *
 * Бассейны разнесены по строкам по той же причине, что у пловцов: 25м и 50м несравнимы,
 * общий разрыв между ними врал бы. Ответ API уже приходит строкой на бассейн — здесь их
 * только собирают в карточку.
 *
 * ⚠ **Дисциплина, где рекорд есть лишь у одной стороны, не исчезает** (правило 11.3.3):
 * её карточка уезжает под разделитель «only one country» с пунктиром и приглушением —
 * тем же приёмом, каким H2H показывает заплыв, который второй не плавал. Спрятать её
 * значило бы подарить стороне с половинным покрытием чистый счёт.
 */
interface Props {
  rows: RecordCompareRow[];
  /** Разрез по полу выбран фильтром — тогда пол не печатается на каждой карточке. */
  genderFixed: boolean;
}

const NO_DATA_TITLE =
  'Only one of the countries has a record in this event — nothing to compare. '
  + 'It counts for neither side.';

/** Порядок стилей — как в лестнице дисциплин страницы, а не алфавитный. */
const STROKE_ORDER = new Map(RK_STROKES.map((s, i) => [s.key, i]));
/** «4X100m» → 100 плюс вес эстафеты: личные дистанции идут раньше эстафетных. */
const distanceOrder = (d: string) => {
  const relay = /^4X/i.test(d) ? 10_000 : 0;
  return relay + (parseInt(d.replace(/^4X/i, ''), 10) || 0);
};

interface EventGroup {
  key: string;
  stroke: string;
  distance: string;
  gender: string;
  rows: RecordCompareRow[];
  /** Ни в одной полосе нет пары — сравнивать нечего во всей карточке. */
  oneSided: boolean;
}

/** Строки API → карточки: стиль × дистанция × пол, внутри полосы бассейнов. */
function groupRows(rows: RecordCompareRow[]): EventGroup[] {
  const map = new Map<string, EventGroup>();

  for (const row of rows) {
    const key = `${row.style}|${row.distance}|${row.gender}`;
    let group = map.get(key);
    if (!group) {
      group = {
        key, stroke: row.style, distance: row.distance, gender: row.gender,
        rows: [], oneSided: true,
      };
      map.set(key, group);
    }
    group.rows.push(row);
    if (row.outcome !== 'no_data') group.oneSided = false;
  }

  const groups = [...map.values()];
  for (const g of groups) {
    // 50м раньше 25м: длинная вода — основная, и на ней же стоят мировые рекорды.
    g.rows.sort((x, y) => (x.pool_type === y.pool_type ? 0 : x.pool_type === '50m' ? -1 : 1));
  }
  groups.sort((x, y) =>
    (STROKE_ORDER.get(x.stroke) ?? 99) - (STROKE_ORDER.get(y.stroke) ?? 99)
    || distanceOrder(x.distance) - distanceOrder(y.distance)
    || x.gender.localeCompare(y.gender));

  return groups;
}

/** Сторона строки → ячейка времени H2H. Нет рекорда — ячейка пустая («—» рисует сама). */
const poolSide = (side: RecordCompareSide | null | undefined, won: boolean): H2HPoolSide | null => {
  if (!side) return null;
  return {
    time: side.time,
    // Дата обязательна рядом со временем: без неё рекорд 2002 года читается просто как
    // «медленнее», хотя может значить «старше» (11.3.3).
    date: side.record_date || null,
    quality: side.issue_reason ? { kind: 'record', reason: side.issue_reason } : null,
    who: side.holder_name ? { name: side.holder_name } : null,
    isWinner: won,
  };
};

/**
 * Разрыв СО ЗНАКОМ. API отдаёт `delta_ms` по модулю (знак ему не нужен — есть `outcome`),
 * а полосе H2H нужен знак: минус = быстрее левый. Ничья — ноль, он печатается как «=».
 */
const signedDelta = (row: RecordCompareRow): number | null => {
  if (row.outcome === 'no_data' || row.delta_ms == null) return null;
  if (row.outcome === 'tie') return 0;
  return row.outcome === 'a' ? -row.delta_ms : row.delta_ms;
};

const Card: React.FC<{ group: EventGroup; showGender: boolean }> = ({ group, showGender }) => (
  <UI_H2HEventCard
    stroke={group.stroke}
    distance={group.distance}
    oneSided={group.oneSided}
    // Пол печатается на карточке ТОЛЬКО когда он не выбран фильтром: иначе одна и та же
    // подпись повторялась бы над каждой карточкой, уже сказанная в полосе фильтров
    // (тот же довод, по которому у таба masters шапки карточек нет вовсе).
    head={showGender ? (
      <div className="h2h-event__icon">
        <UI_SwimmStyleIcon
          styleName={group.stroke}
          styleLen={group.distance}
          styleType="icon-len"
          lenPlacement="below"
          lenSize={30}
          className="src-rc-h2h-events"
        />
        <span className="rc-event__gender">{genderLabel(group.gender)}</span>
      </div>
    ) : undefined}
  >
    {group.rows.map((row) => (
      <UI_H2HPoolRow
        key={row.pool_type}
        poolType={row.pool_type}
        emptyLabel="no record"
        left={poolSide(row.a, row.outcome === 'a')}
        right={poolSide(row.b, row.outcome === 'b')}
        deltaMs={signedDelta(row)}
      />
    ))}
  </UI_H2HEventCard>
);

const RcH2HEvents: React.FC<Props> = ({ rows, genderFixed }) => {
  const groups = React.useMemo(() => groupRows(rows), [rows]);
  const compared = groups.filter((g) => !g.oneSided);
  const lonely = groups.filter((g) => g.oneSided);
  const showGender = !genderFixed;

  // Контейнер ширины (`h2h-scope`) ставит СТРАНИЦА: шапка, пикер и карточки должны
  // мериться одной и той же шириной, иначе ступени раскладки сработают вразнобой.
  return (
    <>
      <div className="h2h-events">
        {compared.map((g) => <Card key={g.key} group={g} showGender={showGender} />)}
      </div>

      {lonely.length > 0 && (
        <>
          <UI_H2HDivider text="only one country" />
          <div className="h2h-events" title={NO_DATA_TITLE}>
            {lonely.map((g) => <Card key={g.key} group={g} showGender={showGender} />)}
          </div>
        </>
      )}

      <span className="h2h-legend">
        25m and 50m are never compared with each other · the gap is left minus right ·
        events where only one country has a record count for neither side
      </span>
    </>
  );
};

export default RcH2HEvents;

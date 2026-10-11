import SwimRow, { swimRowStrokeLabel } from '../../../components/swim-row/swim-row';
import UI_H2HEventCard from '../../../components/mix/h2h/h2h-event-card';
import UI_H2HPoolRow from '../../../components/mix/h2h/h2h-pool-row';
import UI_RecordBadge from '../../../components/mix/record-badge/record-badge';
import type { RecordKind } from '../../../components/mix/record-badge/record-badge';
import { routes } from '../../../../utils/routes';
import { genderLabel } from '../../../../utils/helpers/helper-gender';
import { peerGroupLabel } from '../../../../utils/helpers/season-helper';
import { timeToMs } from '../../../../utils/helpers/recalculate-positions';
import type { SwimmerPersonalBest } from '../../use-swimmer-page';
import type { SwimmerHeldRecord } from '../../use-swimmer-profile';
import UI_RecordsChecked, { recordSources } from '../../../components/mix/records-checked/records-checked';
import { type PanelLoad, noDataText, PanelEmpty, PanelHead } from '../swimmer-panels';

/**
 * Класс рекорда по категории справочника — вход общего `UI_RecordBadge`. Правило одно на
 * продукт: золото носит только национальный (`open`), возрастные и мастерские — серебро.
 */
const recordKindOf = (category: string): RecordKind =>
  (category === 'open' ? 'national' : category === 'masters' ? 'masters' : 'age');

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
/**
 * Группировка рекордов для карточек: ступень (`ageKey` в рамках региона и категории) →
 * дисциплина (`stroke+distance`) → бассейн. Порядок сохраняется тот, в котором рекорды
 * пришли с сервера, — он там уже осмысленный (страна выше, дистанция по возрастанию).
 */
interface RecordDisciplineGroup {
  key: string;
  stroke: string;
  distance: string;
  rows: SwimmerHeldRecord[];
}

interface RecordAgeGroup {
  key: string;
  scope: string;
  /** Нижняя граница ступени — по ней и строится порядок групп. */
  minAge: number;
  kind: RecordKind;
  /**
   * Вид мирового эталона группы — от него подпись справа в шапке: «MASTERS WR» или
   * «WORLD JUNIOR». null — эталона в группе нет, и подпись не печатается: иначе шапка
   * обещает колонку, которой нет. Вид в группе один: ступень мастерс получает только
   * мастерский, возрастная — только юниорский.
   */
  worldKind: 'masters' | 'junior' | null;
  disciplines: RecordDisciplineGroup[];
}

/**
 * Нижняя граница возрастной ступени: «45-49» → 45, «16» → 16. У открытой категории ключа
 * нет — она уходит В КОНЕЦ: «open» это не возраст, и вклинивать её в лестницу между
 * ступенями значило бы читать её как ещё одну ступень.
 */
function stepMinAge(ageKey: string): number {
  const n = parseInt(ageKey, 10);
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}

function groupHeldRecords(records: SwimmerHeldRecord[]): RecordAgeGroup[] {
  const groups: RecordAgeGroup[] = [];
  const byKey = new Map<string, RecordAgeGroup>();

  for (const r of records) {
    const scope = recordScope(r);
    let group = byKey.get(scope);
    if (!group) {
      group = {
        key: scope, scope, minAge: stepMinAge(r.ageKey),
        kind: recordKindOf(r.category), worldKind: null, disciplines: [],
      };
      byKey.set(scope, group);
      groups.push(group);
    }
    if (r.worldRecord) group.worldKind = r.worldRecord.kind ?? 'masters';

    const discKey = `${r.stroke}-${r.distance}`;
    let disc = group.disciplines.find((d) => d.key === discKey);
    if (!disc) {
      disc = { key: discKey, stroke: r.stroke ?? '', distance: r.distance, rows: [] };
      group.disciplines.push(disc);
    }
    disc.rows.push(r);
  }

  // Порядок групп — ПО ВОЗРАСТУ ступени, а не по тому, как рекорды легли в справочнике:
  // сервер сортирует строки по региону и дистанции, и ступени приезжали вперемешку
  // (40-44 между 30-34 и 45-49). Возрастная лестница обязана читаться сверху вниз.
  return groups.sort((a, b) => a.minAge - b.minAge);
}

/** «14-17» → «14–17»: в подписи полоса читается диапазоном, а не вычитанием. */
const bandLabel = (band: string): string => band.replace('-', '–');

/**
 * Подпись мирового эталона: она же title правой ячейки — ссылки у WR нет. У World Junior в
 * подписи ПОЛОСА, а не ступень пловца: WJR один на всю полосу, и «World Junior record · 15»
 * читалось бы как мировой рекорд 15-летних, которого не существует (WJR-план §3).
 */
const worldRecordTitle = (r: SwimmerHeldRecord): string => {
  const event = `${r.distance} ${swimRowStrokeLabel(r.stroke ?? '')} ${r.poolType === '25m' ? 'SCM' : 'LCM'}`;
  const wr = r.worldRecord;
  if (wr?.kind === 'junior') {
    const who = genderLabel(r.gender); // mixed — смешанная эстафета (Э5), не «men»
    return `World Junior record · ${who} ${wr.band ? bandLabel(wr.band) : ''} · ${event}`;
  }
  return `Masters world record · ${r.ageKey} · ${event}`;
};

/**
 * Секция официальных рекордов НАД таблицей личников — показывается только тому, кто их
 * держит (решение Влада: есть рекорды → «Records & PB» и рекорды впереди отдельной секцией).
 *
 * Рисуется КАРТОЧКАМИ семьи `UI_H2H*` в варианте `record`: слева рекорд пловца, справа —
 * мировой эталон с держателем и флагом, посередине разрыв. Эталон двух видов: у мастерса —
 * мировой рекорд той же ступени, у возрастного — World Junior Record, если возраст в полосе
 * WJR (ж 14–17, м 15–18; полоса подписана плашкой «Ages 14–17»). Карточками идут ВСЕ
 * официальные рекорды (решение Влада 20.09.2026): вне полосы и у абсолюта страны правая
 * сторона пустая — официального мирового эталона там нет в принципе.
 *
 * ⚠ Держатель в справочнике записан СТРОКОЙ имени, `SwimmerId` у рекорда нет — тёзка заберёт
 * чужой рекорд. Подпись под секцией обязана это признавать, а не делать вид, что связь точная.
 */
function HeldRecordsSection({
  records, swimmerId, swimmerName, swimmerCountry,
}: {
  records: SwimmerHeldRecord[];
  swimmerId: number;
  swimmerName: string;
  swimmerCountry?: string | null;
}) {
  const groups = groupHeldRecords(records);

  return (
    <div className="deep-records-block">
      <PanelHead
        title={records.length === 1 ? 'Official record' : `Official records · ${records.length}`}
        hint="records where the federation register names this swimmer as the holder"
      />
      <div className="h2h-scope deep-list">
        {groups.map((group) => (
          <div className="h2h-group" key={group.key}>
            <div className="h2h-group__head">
              <span><span aria-hidden="true">🏆 </span>{group.scope.toUpperCase()}</span>
              <UI_RecordBadge kind={group.kind} />
              <span className="h2h-group__line" />
              {/* Только там, где эталон есть: иначе шапка обещает колонку, которой в группе нет. */}
              {group.worldKind && (
                <span className="h2h-group__wr">
                  {group.worldKind === 'junior' ? 'WORLD JUNIOR' : 'MASTERS WR'}
                </span>
              )}
            </div>

            {group.disciplines.map((disc) => (
              <UI_H2HEventCard
                key={disc.key}
                variant="record"
                stroke={disc.stroke}
                distance={disc.distance}
              >
                {disc.rows.map((r) => {
                  const wr = r.worldRecord ?? null;
                  // Разрыв считается на клиенте: у справочника рекордов миллисекунд нет.
                  // Тон всегда `behind` — пловец медленнее мирового, и это не проигрыш.
                  const ms = wr ? timeToMs(r.time) - timeToMs(wr.time) : null;
                  return (
                    <UI_H2HPoolRow
                      key={`${disc.key}-${r.poolType}`}
                      poolType={r.poolType}
                      deltaMs={ms != null && Number.isFinite(ms) ? ms : null}
                      deltaTone="behind"
                      left={{
                        time: r.time,
                        date: r.date,
                        quality: r.quality,
                        // Эстафетный рекорд держит КОМАНДА (Э5): слева клуб, а не сам пловец —
                        // иначе строка читается как его личный рекорд. Состав — в подсказке.
                        who: { name: r.relayTeam || swimmerName, countryCode: swimmerCountry },
                        // Плашка ВСЕГДА у пловца: это его рекорд, а не победа в сравнении.
                        // Без заливки и рамки: время и так золотое, а колонку очерчивают
                        // разделители строк (решение Влада 20.09.2026).
                        isWinner: true,
                        box: 'none',
                        // Нашёлся старт — время ведёт к заплывам пловца на нём.
                        href: r.meet
                          ? routes.competitionSwims(r.meet.competitionId, { swimmerId, eventId: r.meet.eventId })
                          : undefined,
                        // Время первого этапа эстафеты засчитывается личным: без подписи
                        // рекорд «не находится» среди личных заплывов (30.25, И-28).
                        extras: r.relayTeam ? (
                          <span
                            className="h2h-badge h2h-badge--relay"
                            title={r.relayHolders ? `Relay team: ${r.relayHolders}` : 'Relay team record'}
                          >
                            Relay team
                          </span>
                        ) : r.relayLeadOff ? (
                          <span
                            className="h2h-badge h2h-badge--relay"
                            title="Set as the first leg of a relay — a lead-off time counts as an individual record"
                          >
                            Relay lead-off
                          </span>
                        ) : undefined,
                      }}
                      right={wr ? {
                        time: wr.time,
                        date: wr.date,
                        quality: wr.quality,
                        who: wr.holder ? { name: wr.holder, countryCode: wr.countryCode } : null,
                        // Плашки у мирового нет НИКОГДА: разрыв всегда «+», и автоматический
                        // `rightWins` по знаку выдал бы её как победу в сравнении.
                        isWinner: false,
                        title: worldRecordTitle(r),
                        // Полоса WJR — видимо, не только в title: одно время стоит против
                        // нескольких ступеней, и 14-летняя не должна читать его как «рекорд 14».
                        extras: wr.kind === 'junior' && wr.band ? (
                          <span
                            className="h2h-badge h2h-badge--band"
                            title="World Junior records are kept for one age band, not per age"
                          >
                            Ages {bandLabel(wr.band)}
                          </span>
                        ) : undefined,
                      } : null}
                    />
                  );
                })}
              </UI_H2HEventCard>
            ))}
          </div>
        ))}
      </div>
      <div className="deep-legend deep-legend--block">
        The register stores the holder as a name, not as a swimmer id, so a namesake can show
        up here.
        <UI_RecordsChecked
          // Источники считает общий хелпер: тот же список показывают стена клуба и попапы.
          sources={recordSources(records.map((r) => ({
            category: r.category,
            worldKind: r.worldRecord?.kind === 'junior' ? 'junior' : r.worldRecord ? 'masters' : null,
          })))}
          className="mt-1"
        />
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
  rows, poolType, onPoolType, records, swimmerId, swimmerName, swimmerCountry, gender, age, state,
}: {
  rows: SwimmerPersonalBest[] | null;
  poolType: string;
  onPoolType: (pool: string) => void;
  records?: SwimmerHeldRecord[] | null;
  /** Для ссылки строки рекорда на заплывы пловца в найденном старте. */
  swimmerId: number;
  /** Имя и страна пловца — левая сторона карточки рекорда (кто держит время). */
  swimmerName: string;
  swimmerCountry?: string | null;
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
      {records && records.length > 0 && (
        <HeldRecordsSection
          records={records}
          swimmerId={swimmerId}
          swimmerName={swimmerName}
          swimmerCountry={swimmerCountry}
        />
      )}

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
                  // Ступень приходит с сервера («age 14», «masters 45-49», «open»): у
                  // взрослых эталон — полоса или ОТКРЫТЫЙ рекорд страны, и подпись про
                  // «возрастной рекорд» там врала бы.
                  title: r.nationalRecordScope
                    ? `Compared with the national record — ${r.nationalRecordScope}`
                    : (peers
                      ? `Compared with the national record for ${peers}`
                      : 'Compared with the national record'),
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

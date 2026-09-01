import React, { useState } from 'react';
import Helper from '../../../utils/helpers/data-helper';
import RecordsHelper, { maxUpdatedAtLabel } from '../../../utils/helpers/records-helper';
import { HOME_REGION } from '../../../utils/constants/home-region';
import { recordStepAge, ageInSeason } from '../../../utils/helpers/season-helper';
import useSeasonBest, { SeasonBestApiItem } from '../../../hooks/useSeasonBest';
import UI_SeasonNotice from '../../components/mix/season-notice/season-notice';
import type { ShowcaseSeasonNotice } from '../../../utils/helpers/season-helper';
import UI_GenderAgeTable, { GenderAgeEntry, GenderAgeRow } from '../../components/mix/gender-age-table/gender-age-table';
import { timeToMs } from '../../../utils/helpers/recalculate-positions';

interface AgeRecord {
  time: string;
  /** Открытая претензия к записи справочника (И11 для рекордов). */
  issue_reason?: string | null;
  name: string;
  club: string;
  country: string;
  record_date: string;
  updated_at?: string;
  /** Подробности держателя — приходят только при включённой опции ShowAgeRecordsDetails. */
  holder_birth_year?: number | null;
  holder_age?: number | null;
  holder_source?: string | null;
}

/** AgeRecord → строка единой таблицы UI_GenderAgeTable (значение = время, дата = record_date). */
function recordToEntry(r: AgeRecord): GenderAgeEntry {
  return {
    firstName: r.name, club: r.club, value: r.time, date: r.record_date,
    quality: r.issue_reason ? { kind: 'record', reason: r.issue_reason } : null,
    note: holderNote(r),
  };
}

/**
 * Подпись под датой рекорда — только при включённой опции ShowAgeRecordsDetails
 * (сервер тогда присылает holder_birth_year, иначе поля пустые и подписи нет).
 *
 * Показываем ровно то, ради чего опцию заводили: год рождения держателя и сколько ему было
 * в год рекорда. Из этой пары видно, почему запись стоит в ступени 10, хотя пловчихе того
 * же года рождения в сезоне уже 11 (docs/data-integrity.md §13).
 *
 * «?» у источника `name` — держателя опознали по совпадению имени, а не по найденному
 * заплыву: тёзка с тем же именем ошибку бы дал, и об этом честнее предупредить.
 */
function holderNote(r: AgeRecord): string | null {
  if (!r.holder_birth_year) return null;

  const age = r.holder_age ? ` · age ${r.holder_age}` : '';
  const uncertain = r.holder_source === 'name' ? ' ?' : '';
  return `born ${r.holder_birth_year}${age}${uncertain}`;
}

type CardTab = 'records' | 'season';

interface NormativeAgeRecordsProps {
  gender: string;
  poolType: string;
  styleName: string;
  styleLen: string | number;
  age: string; // 'all' or birth year like '2015'
  /**
   * Год НАЧАЛА сезона соревнования — для таба «Season best». Не задан — текущий сезон.
   * Берётся от даты открытого протокола: страница чаще всего про прошедший старт.
   */
  season?: number | null;
}

/**
 * Год рождения → ступень в справочнике. Ось — федерации (календарный год), а не наш
 * возраст в сезоне: витрина показывает их таблицу, см. recordStepAge.
 */
function birthYearToAge(birthYear: string): string | null {
  const age = recordStepAge(birthYear);
  return age === null ? null : String(age);
}

function getDistanceData(
  data: any,
  genderKey: string,
  poolType: string,
  styleName: string,
  distanceKey: string,
): Record<string, AgeRecord> | null {
  // Determine pool keys to search
  const poolKeys =
    poolType === 'all'
      ? ['25m_pool', '50m_pool']
      : [Helper.resolvePoolType(poolType)];

  for (const pk of poolKeys) {
    const result = data.normatives?.[genderKey]?.[pk]?.[styleName]?.[distanceKey];
    if (result) return result as Record<string, AgeRecord>;
  }
  return null;
}

/** Resolve which gender keys to display */
function resolveGenderKeys(gender: string): string[] {
  const resolved = Helper.resolveGender(gender);
  if (gender === 'all' || resolved === 'none') return ['male', 'female'];
  return [resolved];
}

const CARD_SURFACE = 'bg-white dark:bg-[#161b24]';
const CARD_SHADOW = { boxShadow: '0 1px 3px rgba(20,28,45,0.05)' };

/** Ступени возраста, для которых есть хоть один рекорд (ключи вида "10"), по возрастанию. */
function ageKeysOf(
  maleData: Record<string, AgeRecord> | null,
  femaleData: Record<string, AgeRecord> | null,
): string[] {
  const ageSet = new Set<string>();
  Object.keys(maleData ?? {}).forEach(k => /^\d+$/.test(k) && ageSet.add(k));
  Object.keys(femaleData ?? {}).forEach(k => /^\d+$/.test(k) && ageSet.add(k));
  return Array.from(ageSet).sort((a, b) => Number(a) - Number(b));
}

/** Ступени → строки таблицы. */
function ageRowsOf(
  maleData: Record<string, AgeRecord> | null,
  femaleData: Record<string, AgeRecord> | null,
): GenderAgeRow[] {
  return ageKeysOf(maleData, femaleData).map(a => ({
    age: `${a}y`,
    male: maleData?.[a] ? recordToEntry(maleData[a]) : undefined,
    female: femaleData?.[a] ? recordToEntry(femaleData[a]) : undefined,
  }));
}

/** Год начала сезона → дата внутри него: ageInSeason считает возраст от даты. */
function competitionDate(season?: number | null): Date {
  return season ? new Date(season, 9, 1) : new Date();
}

/** Ответ /api/season-best → строки таблицы (по одной на возраст, оба пола рядом). */
function seasonBestRows(items: SeasonBestApiItem[]): GenderAgeRow[] {
  const byAge = new Map<number, GenderAgeRow>();
  items.forEach((it) => {
    const row = byAge.get(it.age) ?? { age: `${it.age}y` };
    const entry: GenderAgeEntry = {
      firstName: it.name,
      club: it.club ?? undefined,
      value: it.time,
      date: it.date,
      swimmerId: it.swimmer_id,
    };
    if (it.gender === 'male') row.male = entry; else row.female = entry;
    byAge.set(it.age, row);
  });
  return Array.from(byAge.entries()).sort((a, b) => a[0] - b[0]).map(([, row]) => row);
}

/** Лучшее (наименьшее) время среди показанных строк — для свёрнутого таба. */
function bestTimeOf(rows: GenderAgeRow[], gender: 'male' | 'female'): string | null {
  let best: string | null = null;
  let bestMs = Infinity;
  rows.forEach((r) => {
    const entry = r[gender] as GenderAgeEntry | undefined;
    if (!entry?.value) return;
    const ms = timeToMs(entry.value);
    if (ms < bestMs) { bestMs = ms; best = entry.value; }
  });
  return best;
}

/** Сводка свёрнутого таба: «время ♂ · время ♀», без имени, клуба и даты. */
function TabSummary({ rows, size }: { rows: GenderAgeRow[]; size: 'mobile' | 'desktop' }) {
  const male = bestTimeOf(rows, 'male');
  const female = bestTimeOf(rows, 'female');
  if (!male && !female) return null;

  const time = size === 'mobile' ? 'text-[11.5px]' : 'text-[13px]';
  const dot = size === 'mobile' ? 'text-[9px]' : 'text-[10px]';
  return (
    <span className={`flex items-center gap-[7px] tabular-nums ${size === 'mobile' ? 'mt-[3px] justify-center' : ''}`}>
      {male && <span className={`${time} font-extrabold text-[#1e6fd6] dark:text-[#5aa2f5]`}>{male}</span>}
      {male && female && <span className={`${dot} text-[#c9cfd9]`}>·</span>}
      {female && <span className={`${time} font-extrabold text-[#d6417f] dark:text-[#f072a6]`}>{female}</span>}
    </span>
  );
}

/**
 * Панель табов «🏅 ISR Age Records | ⏱ Season best».
 *
 * `active = null` — карточка свёрнута целиком (состояние по умолчанию, когда возрастов
 * много): видны только подписи и лучшие времена ♂ · ♀ у обоих табов, таблицы нет. Тап по
 * табу открывает его, повторный тап по открытому — сворачивает обратно. Так карточка не
 * занимает пол-экрана над таблицей результатов, ради чего свёрнутый вид и заводили.
 */
function CardTabs({ active, onChange, recordRows, seasonRows, seasonLabel }: {
  active: CardTab | null;
  onChange: (tab: CardTab) => void;
  recordRows: GenderAgeRow[];
  seasonRows: GenderAgeRow[];
  seasonLabel?: string;
}) {
  const tab = (isActive: boolean) =>
    `flex-1 sm:flex-none sm:whitespace-nowrap cursor-pointer select-none rounded-[9px] px-3 py-[7px] text-center sm:text-left ${
      isActive
        ? 'bg-[var(--theme-agetabs-active-bg)] text-[#1a1a1a] dark:text-[#dbe8fb] shadow-[0_1px_2px_rgba(20,28,45,.10)]'
        : 'bg-transparent text-[#8a93a3]'
    }`;

  return (
    <div className="flex w-full gap-1 rounded-xl bg-[var(--theme-agetabs-panel-bg)] p-1 sm:w-fit sm:gap-1.5">
      <div role="button" onClick={() => onChange('records')} className={tab(active === 'records')}>
        <div className="flex items-center justify-center gap-[5px] text-[12px] font-extrabold sm:justify-start sm:gap-[7px] sm:text-[14px]">
          <span>🏅</span>
          <span>{`${HOME_REGION} Age Records`}</span>
          {active !== 'records' && <span className="hidden sm:inline-flex"><TabSummary rows={recordRows} size="desktop" /></span>}
        </div>
        {active !== 'records' && <span className="flex sm:hidden"><TabSummary rows={recordRows} size="mobile" /></span>}
      </div>
      <div role="button" onClick={() => onChange('season')} className={tab(active === 'season')}>
        <div className="flex items-center justify-center gap-[5px] text-[12px] font-extrabold sm:justify-start sm:gap-[7px] sm:text-[14px]">
          <span>⏱</span>
          <span>Season best{seasonLabel ? <span className="hidden sm:inline">{` ${seasonLabel}`}</span> : null}</span>
          {active !== 'season' && <span className="hidden sm:inline-flex"><TabSummary rows={seasonRows} size="desktop" /></span>}
        </div>
        {active !== 'season' && <span className="flex sm:hidden"><TabSummary rows={seasonRows} size="mobile" /></span>}
      </div>
    </div>
  );
}

/** Карточка с табами: панель сверху, под ней таблица открытого таба (или ничего, если свёрнута). */
function renderTabbedCard(
  recordRows: GenderAgeRow[],
  seasonRows: GenderAgeRow[],
  seasonLabel: string | undefined,
  active: CardTab | null,
  onChange: (tab: CardTab) => void,
  seasonNotice: ShowcaseSeasonNotice | null,
  season: number | null | undefined,
) {
  const rows = active === 'season' ? seasonRows : recordRows;
  return (
    <div className={`${CARD_SURFACE} border border-[#e9edf3] dark:border-[#28344a] rounded-2xl mb-4 p-3 sm:p-[18px_20px]`} style={CARD_SHADOW}>
      <CardTabs
        active={active}
        onChange={onChange}
        recordRows={recordRows}
        seasonRows={seasonRows}
        seasonLabel={seasonLabel}
      />
      {/* Оговорка нужна только у открытого таба Season best: в свёрнутом виде видны одни
          подписи, и целый абзац под ними был бы шумом.

          show="pending" — потому что сезон здесь диктует ОТКРЫТОЕ СОРЕВНОВАНИЕ, а не
          умолчание витрины: у протокола 2025/26 таб честно показывает свой сезон, и
          «показываем прошлый» было бы неправдой. Объяснять нужно только пустоту в новом
          сезоне (docs/season-boundary-rule.md). */}
      {active === 'season' && (
        <UI_SeasonNotice
          notice={seasonNotice}
          season={season}
          show="pending"
          className="mt-3 rounded-xl border border-dashed border-[#e9edf3] dark:border-[#28344a] bg-[#f7f9fc] dark:bg-[#182235] px-3 py-2 text-[11.5px] font-bold leading-snug text-[#6b7686] dark:text-[#93a4bd]"
        />
      )}

      {active && (
      <div className="mt-3 sm:mt-4">
        <UI_GenderAgeTable
          rows={rows}
          showDate
          menLabel="♂ MAN"
          womenLabel="♀ WOMAN"
          ageColWidth={52}
          ageColWidthMobile={34}
        />
      </div>
      )}
    </div>
  );
}

/** Одиночный возраст — та же таблица UI_GenderAgeTable (одна строка), в простой карточке. */
function renderSingleAgeCard(ageLabel: string, male?: AgeRecord, female?: AgeRecord) {
  return (
    <div className={`${CARD_SURFACE} border border-[#e9edf3] dark:border-[#28344a] rounded-2xl mb-4 p-3.5 sm:p-5`} style={CARD_SHADOW}>
      <div className="mb-3 flex items-center gap-2">
        <span className="text-[15px] sm:text-lg shrink-0">🏅</span>
        <span className="text-[13.5px] sm:text-[16px] font-extrabold text-[#1a1a1a] dark:text-[#dbe8fb]">{`${HOME_REGION} Age Records`}</span>
      </div>
      <UI_GenderAgeTable
        rows={[{ age: ageLabel, male: male ? recordToEntry(male) : undefined, female: female ? recordToEntry(female) : undefined }]}
        showDate
        menLabel="♂ MAN"
        womenLabel="♀ WOMAN"
        ageColWidth={52}
        ageColWidthMobile={34}
      />
    </div>
  );
}

/**
 * «Много рекордов» (age === 'all') — свёрнутая карточка, тап заголовка разворачивает
 * таблицу. Имя/клуб/дата показаны сразу в строке (не по тапу) — строки выше, чем
 * раньше, но без скрытого состояния. Один вариант и для мобилки, и для десктопа.
 */
function renderManyAges(
  maleData: Record<string, AgeRecord> | null,
  femaleData: Record<string, AgeRecord> | null,
  isOpen: boolean,
  onToggle: () => void,
) {
  const ages = ageKeysOf(maleData, femaleData);
  if (ages.length === 0) return null;

  const rangeLabel = ages.length > 1 ? `${ages[0]}–${ages[ages.length - 1]}y` : `${ages[0]}y`;
  const showMale = !!maleData;
  const showFemale = !!femaleData;
  const updated = maxUpdatedAtLabel([
    ...Object.values(maleData ?? {}).map(r => r.updated_at),
    ...Object.values(femaleData ?? {}).map(r => r.updated_at),
  ]);

  return (
    <div className={`${CARD_SURFACE} border border-[#e9edf3] dark:border-[#28344a] rounded-2xl mb-4`} style={CARD_SHADOW}>
      <div
        role="button"
        onClick={onToggle}
        className="min-h-11 px-3.5 sm:px-5 py-2.5 sm:py-3 flex items-center gap-2 cursor-pointer select-none"
      >
        <span className="text-[15px] sm:text-lg shrink-0">🏅</span>
        <span className="flex-1 text-[13.5px] sm:text-[16px] font-extrabold text-[#1a1a1a] dark:text-[#dbe8fb]">{`${HOME_REGION} Age Records`}</span>
        {showMale && <span className="text-[10px] sm:text-[11px] font-extrabold text-[#1e6fd6] dark:text-[#5aa2f5] bg-[#eaf2fd] dark:bg-[rgba(90,162,245,0.16)] px-2 py-0.5 rounded-full shrink-0">♂</span>}
        {showFemale && <span className="text-[10px] sm:text-[11px] font-extrabold text-[#d6417f] dark:text-[#f072a6] bg-[#fdeff5] dark:bg-[rgba(240,114,166,0.16)] px-2 py-0.5 rounded-full shrink-0">♀</span>}
        <span className="text-[10px] sm:text-[11px] font-bold text-[#aab0bd] shrink-0 whitespace-nowrap">{rangeLabel}</span>
        {updated && (
          <span className="text-[9px] sm:text-[10px] font-semibold text-[#aab0bd] shrink-0 whitespace-nowrap">updated {updated}</span>
        )}
        <span
          className={`text-[#8a93a3] text-[11px] sm:text-[12px] shrink-0 transition-transform duration-150 ease-out motion-reduce:transition-none ${isOpen ? 'rotate-180' : ''}`}
        >
          ▾
        </span>
      </div>

      {isOpen && (
        <div className="border-t border-[#eef1f6] dark:border-[#232b3a] px-3.5 sm:px-5 pt-3 sm:pt-4 pb-3 sm:pb-4">
          <UI_GenderAgeTable
            rows={ageRowsOf(maleData, femaleData)}
            showDate
            menLabel="♂ MAN"
            womenLabel="♀ WOMAN"
            ageColWidth={52}
            ageColWidthMobile={34}
          />
          <div className="text-[10px] sm:text-[11px] text-[#aab0bd] mt-2.5 sm:mt-3">ⓘ Tap the header to collapse</div>
        </div>
      )}
    </div>
  );
}

function NormativeAgeRecords({ gender, poolType, styleName, styleLen, age, season }: NormativeAgeRecordsProps) {
  const [isOpen, setIsOpen] = useState(false);
  // null — карточка свёрнута. При многих возрастах она такая по умолчанию (решение Влада
  // 2026-08-23): раскрытая таблица на десяток ступеней занимает пол-экрана над результатами,
  // ради которых человек и пришёл. При одном возрасте строка всего одна — прятать нечего.
  const [activeTab, setActiveTab] = useState<CardTab | null>(null);
  // Хук — до любых ранних return: правило хуков React.
  const seasonBest = useSeasonBest({
    styleName, styleLen, poolType, season,
    enabled: !!styleName && !!styleLen,
  });

  // Only show if style and distance are selected
  if (!styleName || !styleLen) return null;

  const data = RecordsHelper.getAgeRecords();
  if (!data?.normatives) return null;

  const distanceKey = `${styleLen}m`;
  const genderKeys = resolveGenderKeys(gender);

  const isSingleAge = age && age !== 'all';
  const resolvedAge = isSingleAge ? birthYearToAge(age) : null;

  const distanceByGender: Partial<Record<string, Record<string, AgeRecord>>> = {};
  genderKeys.forEach(gk => {
    const d = getDistanceData(data, gk, poolType, styleName, distanceKey);
    if (d) distanceByGender[gk] = d;
  });

  // Есть season best → заголовок карточки заменяется панелью табов; сворачивания нет
  // (открыт всегда ровно один таб). Нет — всё как раньше.
  const recordRows: GenderAgeRow[] = (isSingleAge && resolvedAge)
    ? [{
        age: `${resolvedAge}y`,
        male: distanceByGender.male?.[resolvedAge] ? recordToEntry(distanceByGender.male[resolvedAge]) : undefined,
        female: distanceByGender.female?.[resolvedAge] ? recordToEntry(distanceByGender.female[resolvedAge]) : undefined,
      }]
    : ageRowsOf(distanceByGender.male ?? null, distanceByGender.female ?? null);

  // У КАЖДОГО таба свои ступени: справочник федерации ведётся с 10 лет, а наши восьми- и
  // девятилетние плавают — season best честно начинается с 8. Подгонять таблицы друг под
  // друга нечем: это разные источники, и объединение давало бы у рекордов пустые строки 8-9,
  // которых там не бывает по определению.
  //
  // Общий у них только ПОТОЛОК: последняя ступень справочника (18). Дальше у федерации идут
  // adults/masters, masters в этот таб не входит, и без потолка снизу season best повисал бы
  // хвост случайных взрослых стартов до 60+.
  //
  // ⚠ При выбранном годе рождения ступени в табах РАЗНЫЕ по построению: у рекордов ось
  // календарная (recordStepAge), у season best — сезонная (правило Влада 2026-08-22).
  const ageNumberOf = (label: string) => Number(label.replace(/\D+$/, ''));
  const maxAge = recordRows.length > 0
    ? Math.max(...recordRows.map(r => ageNumberOf(r.age)))
    : 18;
  const seasonAge = isSingleAge ? ageInSeason(age, competitionDate(season)) : null;
  const allSeasonRows = seasonBest ? seasonBestRows(seasonBest.data) : [];

  const seasonRows: GenderAgeRow[] = isSingleAge
    ? (seasonAge === null ? [] : allSeasonRows.filter(r => r.age === `${seasonAge}y`))
    : allSeasonRows.filter(r => ageNumberOf(r.age) <= maxAge);

  // Панель табов появляется, только если сезон вообще чем-то наполнен: таблица из одних
  // прочерков — не повод менять шапку карточки.
  const hasSeasonBest = seasonRows.some(r => r.male || r.female);
  if (hasSeasonBest) {
    if (recordRows.length === 0) return null;
    return renderTabbedCard(
      recordRows, seasonRows, seasonBest?.season_label,
      // Один возраст — карточка сразу раскрыта на табе рекордов.
      isSingleAge ? (activeTab ?? 'records') : activeTab,
      // Тап по открытому табу сворачивает карточку обратно; по другому — переключает.
      (tab) => setActiveTab(prev => (prev === tab ? null : tab)),
      // Сезон витрины ещё не переключился на новый — таб обязан это объяснить, иначе
      // «Season best 2025/26» в разгар 2026/27 читается как протухшие данные.
      seasonBest?.season_notice ?? null,
      season,
    );
  }

  let rendered: React.ReactNode = null;
  if (isSingleAge && resolvedAge) {
    const maleRecord = distanceByGender.male?.[resolvedAge];
    const femaleRecord = distanceByGender.female?.[resolvedAge];
    rendered = (maleRecord || femaleRecord)
      ? renderSingleAgeCard(`${resolvedAge}y`, maleRecord, femaleRecord)
      : null;
  } else {
    rendered = renderManyAges(
      distanceByGender.male ?? null,
      distanceByGender.female ?? null,
      isOpen,
      () => setIsOpen(v => !v),
    );
  }

  if (!rendered) return null;

  // Та же ширина/центровка, что у таблицы результатов
  return <>{rendered}</>;
}

export default NormativeAgeRecords;

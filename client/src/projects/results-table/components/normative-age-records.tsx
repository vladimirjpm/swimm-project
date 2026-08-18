import React, { useState } from 'react';
import Helper from '../../../utils/helpers/data-helper';
import RecordsHelper, { maxUpdatedAtLabel } from '../../../utils/helpers/records-helper';
import { HOME_REGION } from '../../../utils/constants/home-region';
import UI_GenderAgeTable, { GenderAgeEntry } from '../../components/mix/gender-age-table/gender-age-table';

interface AgeRecord {
  time: string;
  /** Открытая претензия к записи справочника (И11 для рекордов). */
  issue_reason?: string | null;
  name: string;
  club: string;
  country: string;
  record_date: string;
  updated_at?: string;
}

/** AgeRecord → строка единой таблицы UI_GenderAgeTable (значение = время, дата = record_date). */
function recordToEntry(r: AgeRecord): GenderAgeEntry {
  return {
    firstName: r.name, club: r.club, value: r.time, date: r.record_date,
    quality: r.issue_reason ? { kind: 'record', reason: r.issue_reason } : null,
  };
}

interface NormativeAgeRecordsProps {
  gender: string;
  poolType: string;
  styleName: string;
  styleLen: string | number;
  age: string; // 'all' or birth year like '2015'
}

/** Convert birth year to age based on current year */
function birthYearToAge(birthYear: string): string | null {
  const year = Number(birthYear);
  if (!year || year < 1900 || year > 2100) return null;
  const currentYear = new Date().getFullYear();
  return String(currentYear - year);
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
  const ageSet = new Set<string>();
  Object.keys(maleData ?? {}).forEach(k => /^\d+$/.test(k) && ageSet.add(k));
  Object.keys(femaleData ?? {}).forEach(k => /^\d+$/.test(k) && ageSet.add(k));
  const ages = Array.from(ageSet).sort((a, b) => Number(a) - Number(b));
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
            rows={ages.map(a => ({
              age: `${a}y`,
              male: maleData?.[a] ? recordToEntry(maleData[a]) : undefined,
              female: femaleData?.[a] ? recordToEntry(femaleData[a]) : undefined,
            }))}
            showDate
            menLabel="♂ MAN"
            womenLabel="♀ WOMAN"
            ageColWidth={52}
          />
          <div className="text-[10px] sm:text-[11px] text-[#aab0bd] mt-2.5 sm:mt-3">ⓘ Tap the header to collapse</div>
        </div>
      )}
    </div>
  );
}

function NormativeAgeRecords({ gender, poolType, styleName, styleLen, age }: NormativeAgeRecordsProps) {
  const [isOpen, setIsOpen] = useState(false);

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

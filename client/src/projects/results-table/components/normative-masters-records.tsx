import React from 'react';
import Helper from '../../../utils/helpers/data-helper';
import RecordsHelper, { maxUpdatedAtLabel } from '../../../utils/helpers/records-helper';
import { HOME_REGION } from '../../../utils/constants/home-region';
import UI_GenderAgeTable, { GenderAgeEntry } from '../../components/mix/gender-age-table/gender-age-table';

interface MastersRecord {
  time: string;
  name: string;
  club: string;
  record_date: string;
  updated_at?: string;
}

/** MastersRecord → строка единой таблицы UI_GenderAgeTable (значение = время, дата = record_date). */
function recordToEntry(r: MastersRecord): GenderAgeEntry {
  return { firstName: r.name, club: r.club, value: r.time, date: r.record_date };
}

interface NormativeMastersRecordsProps {
  gender: string;
  poolType: string;
  styleName: string;
  styleLen: string | number;
  age: string; // 'all' or birth year like '1992'
}

/** Convert birth year to age based on current year */
function birthYearToAge(birthYear: string): number | null {
  const year = Number(birthYear);
  if (!year || year < 1900 || year > 2100) return null;
  const currentYear = new Date().getFullYear();
  return currentYear - year;
}

/** Find the masters age group key (e.g. "30-34") for a given age */
function findAgeGroup(age: number, distanceData: Record<string, MastersRecord>): string | null {
  for (const key of Object.keys(distanceData)) {
    const match = key.match(/^(\d+)-(\d+)$/);
    if (!match) continue;
    const lo = Number(match[1]);
    const hi = Number(match[2]);
    if (age >= lo && age <= hi) return key;
  }
  return null;
}

function getDistanceData(
  data: any,
  genderKey: string,
  poolType: string,
  styleName: string,
  distanceKey: string,
): Record<string, MastersRecord> | null {
  const poolKeys =
    poolType === 'all'
      ? ['25m_pool', '50m_pool']
      : [Helper.resolvePoolType(poolType)];

  for (const pk of poolKeys) {
    const result = data.normatives?.[genderKey]?.[pk]?.[styleName]?.[distanceKey];
    if (result) return result as Record<string, MastersRecord>;
  }
  return null;
}

/** Resolve which gender keys to display (♀ слева, ♂ справа) */
function resolveGenderKeys(gender: string): string[] {
  const resolved = Helper.resolveGender(gender);
  if (gender === 'all' || resolved === 'none') return ['female', 'male'];
  return [resolved];
}

const CARD_SURFACE = 'bg-white dark:bg-[#161b24]';
const CARD_SHADOW = { boxShadow: '0 1px 3px rgba(20,28,45,0.05)' };

/** Одиночная возрастная группа — та же таблица UI_GenderAgeTable (одна строка), в карточке. */
function renderSingleGroupCard(label: string, male?: MastersRecord, female?: MastersRecord) {
  return (
    <div className={`${CARD_SURFACE} border border-[#e9edf3] dark:border-[#28344a] rounded-2xl mb-4 p-3.5 sm:p-5`} style={CARD_SHADOW}>
      <div className="mb-3 flex items-center gap-2">
        <span className="text-[15px] sm:text-lg shrink-0">🏅</span>
        <span className="text-[13.5px] sm:text-[16px] font-extrabold text-[#1a1a1a] dark:text-[#dbe8fb]">{`${HOME_REGION} Masters Records`}</span>
      </div>
      <UI_GenderAgeTable
        rows={[{ age: label, male: male ? recordToEntry(male) : undefined, female: female ? recordToEntry(female) : undefined }]}
        showDate
        ageBrackets
        menLabel="♂ MAN"
        womenLabel="♀ WOMAN"
        ageColWidth={68}
      />
    </div>
  );
}

/**
 * Ячейка результата: время у края, ближнего к колонке AGE (по центру таблицы),
 * имя+клуб+дата — блоком с внешней стороны, в одну строку с временем (не под ним);
 * тот же приём, что и в normative-age-records.tsx renderGenderCell.
 */
/**
 * «Много возрастных групп» (age === 'all') — свёрнутая карточка, тап заголовка
 * разворачивает таблицу; та же механика и структура (Man | возраст | Woman), что
 * у normative-age-records.tsx renderManyAges, только вместо одиночного возраста —
 * возрастная группа мастерс в квадратных скобках («[40-44]»).
 */
function renderManyAgeGroups(
  maleData: Record<string, MastersRecord> | null,
  femaleData: Record<string, MastersRecord> | null,
  isOpen: boolean,
  onToggle: () => void,
) {
  const groupSet = new Set<string>();
  Object.keys(maleData ?? {}).forEach(k => /^\d+-\d+$/.test(k) && groupSet.add(k));
  Object.keys(femaleData ?? {}).forEach(k => /^\d+-\d+$/.test(k) && groupSet.add(k));
  const groups = Array.from(groupSet).sort((a, b) => Number(a.split('-')[0]) - Number(b.split('-')[0]));
  if (groups.length === 0) return null;

  const rangeLabel = groups.length > 1 ? `${groups[0]}…${groups[groups.length - 1]}` : groups[0];
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
        <span className="flex-1 text-[13.5px] sm:text-[16px] font-extrabold text-[#1a1a1a] dark:text-[#dbe8fb]">{`${HOME_REGION} Masters Records`}</span>
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
            rows={groups.map(g => ({
              age: g,
              male: maleData?.[g] ? recordToEntry(maleData[g]) : undefined,
              female: femaleData?.[g] ? recordToEntry(femaleData[g]) : undefined,
            }))}
            showDate
            ageBrackets
            menLabel="♂ MAN"
            womenLabel="♀ WOMAN"
            ageColWidth={68}
          />
        </div>
      )}
    </div>
  );
}

function NormativeMastersRecords({ gender, poolType, styleName, styleLen, age }: NormativeMastersRecordsProps) {
  const [isOpen, setIsOpen] = React.useState(false);

  if (!styleName || !styleLen) return null;

  const data = RecordsHelper.getMastersRecords();
  if (!data?.normatives) return null;

  const distanceKey = `${styleLen}m`;
  const genderKeys = resolveGenderKeys(gender);

  const isSingleAge = age && age !== 'all';
  const isAgeGroupKey = isSingleAge && /^\d+-\d+$/.test(age);
  const resolvedAge = isSingleAge && !isAgeGroupKey ? birthYearToAge(age) : null;

  const distanceByGender: Partial<Record<string, Record<string, MastersRecord>>> = {};
  genderKeys.forEach(gk => {
    const d = getDistanceData(data, gk, poolType, styleName, distanceKey);
    if (d) distanceByGender[gk] = d;
  });

  let rendered: React.ReactNode = null;

  if (isAgeGroupKey) {
    // Конкретная возрастная группа выбрана явно — одна строка (обе половины).
    const male = distanceByGender.male?.[age];
    const female = distanceByGender.female?.[age];
    rendered = (male || female) ? renderSingleGroupCard(age, male, female) : null;
  } else if (isSingleAge && resolvedAge) {
    // Возраст введён (год рождения) — резолвим в группу мастерс для каждого пола.
    const mDd = distanceByGender.male;
    const fDd = distanceByGender.female;
    const mGroup = mDd ? findAgeGroup(resolvedAge, mDd) : null;
    const fGroup = fDd ? findAgeGroup(resolvedAge, fDd) : null;
    const male = mGroup ? mDd![mGroup] : undefined;
    const female = fGroup ? fDd![fGroup] : undefined;
    rendered = (male || female) ? renderSingleGroupCard(mGroup || fGroup || '', male, female) : null;
  } else {
    // Возраст не выбран — все группы сразу, встроенная разворачиваемая таблица
    // (Man | [возрастная группа] | Woman), как у обычных ISR Age Records.
    rendered = renderManyAgeGroups(
      distanceByGender.male ?? null,
      distanceByGender.female ?? null,
      isOpen,
      () => setIsOpen(v => !v),
    );
  }

  if (!rendered) return null;

  // Та же ширина/центровка, что у таблицы результатов.
  return <>{rendered}</>;
}

export default NormativeMastersRecords;

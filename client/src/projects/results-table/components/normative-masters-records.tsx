import React from 'react';
import Helper from '../../../utils/helpers/data-helper';
import { rootActions, useAppDispatch } from '../../../store/store';
import { Enums } from '../../../utils/interfaces/enums';

interface MastersRecord {
  time: string;
  name: string;
  club: string;
  record_date: string;
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

const GENDER_LABELS: Record<string, string> = {
  male: '♂ Man',
  female: '♀ Woman',
};

/**
 * Гендерные токены дизайна (design_handoff_age_records/README.md, свет+тьма) —
 * те же, что у ISR Age Records, для визуальной согласованности обеих карточек рекордов.
 */
const GENDER_STYLES: Record<string, {
  accent: string;
  accentBg: string;
  deep: string;
  soft: string;
  border: string;
}> = {
  male: {
    accent: 'text-[#1e6fd6] dark:text-[#5aa2f5]',
    accentBg: 'bg-[#1e6fd6] dark:bg-[#5aa2f5]',
    deep: 'text-[#123a70] dark:text-[#dbe8fb]',
    soft: 'bg-[#eaf2fd] dark:bg-[rgba(90,162,245,0.16)]',
    border: 'border-[#d3e3f8] dark:border-[#28344a]',
  },
  female: {
    accent: 'text-[#d6417f] dark:text-[#f072a6]',
    accentBg: 'bg-[#d6417f] dark:bg-[#f072a6]',
    deep: 'text-[#7a1f4b] dark:text-[#fbdcec]',
    soft: 'bg-[#fdeff5] dark:bg-[rgba(240,114,166,0.16)]',
    border: 'border-[#f6d3e3] dark:border-[#412234]',
  },
};

const CARD_SURFACE = 'bg-white dark:bg-[#161b24]';
const CARD_SHADOW = { boxShadow: '0 1px 3px rgba(20,28,45,0.05)' };

function getStyles(genderKey: string) {
  return GENDER_STYLES[genderKey] ?? GENDER_STYLES.male;
}

/** Render a single age-group detail row */
function renderSingleAgeGroup(
  genderKey: string,
  ageGroup: string,
  record: MastersRecord,
  showGenderLabel: boolean,
) {
  const s = getStyles(genderKey);

  return (
    <div
      key={genderKey}
      className={`relative overflow-hidden ${CARD_SURFACE} border ${s.border} rounded-[18px] pl-[26px] pr-[22px] py-4 mb-4 flex items-center gap-3 flex-wrap`}
      style={CARD_SHADOW}
    >
      <div className={`absolute left-0 top-0 bottom-0 w-[5px] ${s.accentBg}`} />
      <div className={`w-[30px] h-[30px] rounded-[9px] ${s.soft} flex items-center justify-center text-base shrink-0`}>🏅</div>
      <span className={`text-[15px] font-extrabold ${s.deep} tracking-[-0.2px]`}>
        {showGenderLabel ? `${GENDER_LABELS[genderKey] || genderKey} · ` : ''}ISR Masters Record ({ageGroup})
      </span>
      <span className={`text-[19px] font-extrabold ${s.deep} tabular-nums tracking-[-0.5px]`}>{record.time}</span>
      <span className="text-[13px] text-[#6b7280]" dir="rtl">{record.name}</span>
      <span className="text-[13px] text-[#9098a4]" dir="rtl">{record.club}</span>
      <span className="text-[11px] text-[#9098a4] tabular-nums ml-auto">{record.record_date}</span>
    </div>
  );
}

/** Render all age-groups as a clickable card that opens a popup with details */
function renderAllAgeGroupsLabel(
  genderKey: string,
  distanceData: Record<string, MastersRecord>,
  showGenderLabel: boolean,
  poolType: string,
  onClick: () => void,
) {
  const ageGroupKeys = Object.keys(distanceData)
    .filter(k => /^\d+-\d+$/.test(k));

  if (ageGroupKeys.length === 0) return null;

  const s = getStyles(genderKey);

  return (
    <div
      key={genderKey}
      className={`${CARD_SURFACE} border ${s.border} rounded-2xl px-[18px] py-4 mb-4 cursor-pointer transition-transform duration-[120ms] ease-out hover:-translate-y-[1px]`}
      style={CARD_SHADOW}
      onClick={onClick}
    >
      <div className="flex items-center gap-2.5">
        <div className={`w-[30px] h-[30px] rounded-[9px] ${s.soft} flex items-center justify-center text-base shrink-0`}>🏅</div>
        <div className="flex items-center gap-2 flex-1 min-w-0">
          <span className={`text-[15px] font-extrabold ${s.deep} tracking-[-0.2px]`}>ISR Masters Records</span>
          {showGenderLabel && (
            <span className={`text-[11px] font-bold ${s.accent} ${s.soft} px-[9px] py-[3px] rounded-full`}>{GENDER_LABELS[genderKey]}</span>
          )}
        </div>
        <span className={`text-xs font-bold ${s.accent} shrink-0`}>▶</span>
      </div>
    </div>
  );
}

function NormativeMastersRecords({ gender, poolType, styleName, styleLen, age }: NormativeMastersRecordsProps) {
  const dispatch = useAppDispatch();

  if (!styleName || !styleLen) return null;

  const data = (window as any).normative_masters_record;
  if (!data?.normatives) return null;

  const distanceKey = `${styleLen}m`;
  const genderKeys = resolveGenderKeys(gender);
  const showGenderLabel = genderKeys.length > 1;

  const isSingleAge = age && age !== 'all';
  const isAgeGroupKey = isSingleAge && /^\d+-\d+$/.test(age);
  const resolvedAge = isSingleAge && !isAgeGroupKey ? birthYearToAge(age) : null;

  const openPopup = (popupItems: any[]) => {
    dispatch(rootActions.updateState({
      isPopup: true,
      popUpType: Enums.PopupType.mastersRecords,
      popUpObj: popupItems,
    }));
  };

  const rendered = genderKeys.map(gk => {
    const distanceData = getDistanceData(data, gk, poolType, styleName, distanceKey);
    if (!distanceData) return null;

    if (isAgeGroupKey) {
      const record = distanceData[age];
      if (!record) return null;
      return renderSingleAgeGroup(gk, age, record, showGenderLabel);
    }

    if (isSingleAge && resolvedAge) {
      const ageGroup = findAgeGroup(resolvedAge, distanceData);
      if (!ageGroup) return null;
      const record = distanceData[ageGroup];
      if (!record) return null;
      return renderSingleAgeGroup(gk, ageGroup, record, showGenderLabel);
    }

    // Multiple age groups — open popup for this specific gender
    const popupItem = { genderKey: gk, distanceData, styleName, styleLen, poolType };

    return renderAllAgeGroupsLabel(gk, distanceData, showGenderLabel, poolType, () => openPopup([popupItem]));
  }).filter(Boolean);

  if (rendered.length === 0) return null;

  // Та же ширина/центровка, что у таблицы результатов.
  // Оба пола — в одну строку (♀ слева, ♂ справа); один — во всю ширину.
  return (
    <div className={`lg:max-w-[1180px] lg:mx-auto ${rendered.length > 1 ? 'grid gap-x-4 sm:grid-cols-2' : ''}`}>
      {rendered}
    </div>
  );
}

export default NormativeMastersRecords;

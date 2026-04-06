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

/** Resolve which gender keys to display */
function resolveGenderKeys(gender: string): string[] {
  const resolved = Helper.resolveGender(gender);
  if (gender === 'all' || resolved === 'none') return ['male', 'female'];
  return [resolved];
}

const GENDER_LABELS: Record<string, string> = {
  male: '♂',
  female: '♀',
};

const GENDER_STYLES: Record<string, { bg: string; border: string; title: string; bold: string; age: string }> = {
  male: { bg: 'bg-blue-50', border: 'border-blue-200', title: 'text-blue-700', bold: 'text-blue-900', age: 'text-blue-800' },
  female: { bg: 'bg-pink-50', border: 'border-pink-200', title: 'text-pink-700', bold: 'text-pink-900', age: 'text-pink-800' },
};

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
    <div key={genderKey} className={`${s.bg} border ${s.border} rounded px-3 py-2 mb-2 text-sm`}>
      <span className={`font-semibold ${s.title}`}>
        🏅 {showGenderLabel ? `${GENDER_LABELS[genderKey] || genderKey} ` : ''}ISR Masters Record ({ageGroup}):
      </span>{' '}
      <span className={`font-bold ${s.bold}`}>{record.time}</span>
      <span className="mx-1">—</span>
      <span>{record.name}</span>
      <span className="mx-1">|</span>
      <span className="text-gray-600">{record.club}</span>
      <span className="mx-1">|</span>
      <span className="text-gray-500">{record.record_date}</span>
    </div>
  );
}

/** Render all age-groups as a clickable label that opens a popup */
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
      className={`${s.bg} border ${s.border} rounded px-3 py-2 mb-2 text-sm cursor-pointer hover:opacity-80`}
      onClick={onClick}
    >
      <span className={`font-semibold ${s.title}`}>
        🏅 {showGenderLabel ? `${GENDER_LABELS[genderKey] || genderKey} ` : ''}ISR Masters Records
      </span>{' '}
      <span className="text-gray-400 text-xs">▶</span>
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

  return <>{rendered}</>;
}

export default NormativeMastersRecords;

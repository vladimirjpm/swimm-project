import React from 'react';
import Helper from '../../../utils/helpers/data-helper';

interface AgeRecord {
  time: string;
  name: string;
  club: string;
  country: string;
  record_date: string;
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

/** Render a single-age detail row */
function renderSingleAge(
  genderKey: string,
  resolvedAge: string,
  distanceData: Record<string, AgeRecord>,
  showGenderLabel: boolean,
) {
  const record = distanceData[resolvedAge];
  if (!record) return null;
  const s = getStyles(genderKey);

  return (
    <div key={genderKey} className={`${s.bg} border ${s.border} rounded px-3 py-2 mb-2 text-sm`}>
      <span className={`font-semibold ${s.title}`}>
        🏅 {showGenderLabel ? `${GENDER_LABELS[genderKey] || genderKey} ` : ''}ISR Record ({resolvedAge}y):
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

/** Render all-ages compact row */
function renderAllAges(
  genderKey: string,
  distanceData: Record<string, AgeRecord>,
  showGenderLabel: boolean,
) {
  const numericAges = Object.keys(distanceData)
    .filter(k => /^\d+$/.test(k))
    .sort((a, b) => Number(a) - Number(b));

  if (numericAges.length === 0) return null;

  const s = getStyles(genderKey);

  return (
    <div key={genderKey} className={`${s.bg} border ${s.border} rounded px-3 py-2 mb-2 text-sm overflow-x-auto`}>
      <span className={`font-semibold ${s.title}`}>
        🏅 {showGenderLabel ? `${GENDER_LABELS[genderKey] || genderKey} ` : ''}ISR Age Records:
      </span>{' '}
      <span className="whitespace-nowrap">
        {numericAges.map((ageKey, idx) => {
          const rec = distanceData[ageKey];
          return (
            <React.Fragment key={ageKey}>
              {idx > 0 && <span className="text-gray-400 mx-1">|</span>}
              <span className={`${s.age} font-medium`}>{ageKey}y:</span>{' '}
              <span>{rec.time}</span>
            </React.Fragment>
          );
        })}
      </span>
    </div>
  );
}

function NormativeAgeRecords({ gender, poolType, styleName, styleLen, age }: NormativeAgeRecordsProps) {
  // Only show if style and distance are selected
  if (!styleName || !styleLen) return null;

  const data = (window as any).normative_age_record;
  if (!data?.normatives) return null;

  const distanceKey = `${styleLen}m`;
  const genderKeys = resolveGenderKeys(gender);
  const showGenderLabel = genderKeys.length > 1;

  const isSingleAge = age && age !== 'all';
  const resolvedAge = isSingleAge ? birthYearToAge(age) : null;

  const rendered = genderKeys.map(gk => {
    const distanceData = getDistanceData(data, gk, poolType, styleName, distanceKey);
    if (!distanceData) return null;

    if (isSingleAge && resolvedAge) {
      return renderSingleAge(gk, resolvedAge, distanceData, showGenderLabel);
    }
    return renderAllAges(gk, distanceData, showGenderLabel);
  }).filter(Boolean);

  if (rendered.length === 0) return null;

  return <>{rendered}</>;
}

export default NormativeAgeRecords;

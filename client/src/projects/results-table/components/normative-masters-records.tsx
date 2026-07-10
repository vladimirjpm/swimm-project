import React from 'react';
import Helper from '../../../utils/helpers/data-helper';
import RecordsHelper, { maxUpdatedAtLabel } from '../../../utils/helpers/records-helper';

interface MastersRecord {
  time: string;
  name: string;
  club: string;
  record_date: string;
  updated_at?: string;
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
  borderBg: string;
}> = {
  male: {
    accent: 'text-[#1e6fd6] dark:text-[#5aa2f5]',
    accentBg: 'bg-[#1e6fd6] dark:bg-[#5aa2f5]',
    deep: 'text-[#123a70] dark:text-[#dbe8fb]',
    soft: 'bg-[#eaf2fd] dark:bg-[rgba(90,162,245,0.16)]',
    border: 'border-[#d3e3f8] dark:border-[#28344a]',
    borderBg: 'bg-[#d3e3f8] dark:bg-[#28344a]',
  },
  female: {
    accent: 'text-[#d6417f] dark:text-[#f072a6]',
    accentBg: 'bg-[#d6417f] dark:bg-[#f072a6]',
    deep: 'text-[#7a1f4b] dark:text-[#fbdcec]',
    soft: 'bg-[#fdeff5] dark:bg-[rgba(240,114,166,0.16)]',
    border: 'border-[#f6d3e3] dark:border-[#412234]',
    borderBg: 'bg-[#f6d3e3] dark:bg-[#412234]',
  },
};

const CARD_SURFACE = 'bg-white dark:bg-[#161b24]';
const CARD_SHADOW = { boxShadow: '0 1px 3px rgba(20,28,45,0.05)' };

function getStyles(genderKey: string) {
  return GENDER_STYLES[genderKey] ?? GENDER_STYLES.male;
}

/**
 * Одиночный рекорд (выбраны и возраст, и стиль) — та же «лента», что у обычных
 * ISR Age Records (renderOneAge в normative-age-records.tsx), для визуальной
 * согласованности; вместо «Xy» в чипе — возрастная группа мастерс (напр. «40-44»).
 */
function renderSingleAgeGroup(
  genderKey: string,
  ageGroup: string,
  record: MastersRecord,
) {
  const s = getStyles(genderKey);
  const chip = `${GENDER_LABELS[genderKey] || genderKey} · ${ageGroup}`;
  const updated = maxUpdatedAtLabel([record.updated_at]);

  return (
    <div
      key={genderKey}
      className={`relative overflow-hidden ${CARD_SURFACE} rounded-2xl sm:rounded-[18px] pl-[18px] sm:pl-[26px] pr-3.5 sm:pr-6 py-3 sm:py-5 mb-4 flex items-center gap-3.5 sm:gap-6`}
      style={CARD_SHADOW}
    >
      <div className={`absolute left-0 top-0 bottom-0 w-1 sm:w-[5px] ${s.accentBg}`} />

      <div className="flex-1 min-w-0">
        <div dir="rtl" className={`text-[13px] sm:text-[19px] font-bold ${s.deep} text-left whitespace-nowrap overflow-hidden text-ellipsis`}>{record.name}</div>
        <div dir="rtl" className="text-[11px] sm:text-[14px] text-[#8a93a3] text-left whitespace-nowrap overflow-hidden text-ellipsis mt-[3px]">{record.club}</div>
        <div className="text-[10px] sm:text-[13px] text-[#aab0bd] sm:text-[#9098a4] tabular-nums mt-[3px] sm:mt-1.5">📅 {record.record_date || '—'}</div>
      </div>

      <span className={`text-xs sm:text-[15px] font-extrabold ${s.accent} ${s.soft} px-[11px] sm:px-[18px] py-[5px] sm:py-2 rounded-full whitespace-nowrap shrink-0`}>
        {chip}
      </span>

      <div className={`w-px self-stretch ${s.borderBg}`} />

      <div className="shrink-0 text-right">
        <div className="text-[9px] sm:text-[12px] font-extrabold tracking-[0.08em] sm:tracking-[0.09em] uppercase text-[#9098a4] mb-[3px] sm:mb-1">Masters Record</div>
        <div className={`text-[30px] sm:text-[40px] font-extrabold ${s.deep} tabular-nums tracking-[-1px] sm:tracking-[-1.5px] leading-none`}>
          {record.time}
        </div>
        {updated && (
          <div className="text-[9px] sm:text-[11px] text-[#aab0bd] mt-[2px] sm:mt-1">updated {updated}</div>
        )}
      </div>
    </div>
  );
}

/**
 * Ячейка результата: время у края, ближнего к колонке AGE (по центру таблицы),
 * имя+клуб+дата — блоком с внешней стороны, в одну строку с временем (не под ним);
 * тот же приём, что и в normative-age-records.tsx renderGenderCell.
 */
function renderGenderCell(genderKey: string, rec: MastersRecord) {
  const isMale = genderKey === 'male';
  const s = getStyles(genderKey);
  const updated = maxUpdatedAtLabel([rec.updated_at]);

  const time = (
    <div className={`text-[15px] sm:text-[17px] font-extrabold ${s.deep} tabular-nums leading-tight shrink-0`}>
      {rec.time}
    </div>
  );
  const info = (
    <div className="min-w-0 flex-1">
      <div dir="rtl" className={`text-[11px] sm:text-[12.5px] font-bold ${s.deep} truncate`}>{rec.name}</div>
      <div dir="rtl" className="text-[10px] sm:text-[11px] text-[#8a93a3] truncate">{rec.club}</div>
      <div className="text-[9px] sm:text-[10px] text-[#aab0bd] tabular-nums">{rec.record_date || '—'}</div>
    </div>
  );

  return (
    <div
      className={`${s.soft} rounded-lg px-2.5 py-[5px] sm:py-2 flex items-center gap-2`}
      title={updated ? `updated ${updated}` : undefined}
    >
      {isMale ? <>{info}{time}</> : <>{time}{info}</>}
    </div>
  );
}

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
        <span className="flex-1 text-[13.5px] sm:text-[16px] font-extrabold text-[#1a1a1a] dark:text-[#dbe8fb]">ISR Masters Records</span>
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
          <div
            className="grid gap-x-2 sm:gap-x-4 gap-y-1.5 sm:gap-y-2 items-center"
            style={{ gridTemplateColumns: `${showMale ? '1fr' : ''} 68px ${showFemale ? '1fr' : ''}`.trim() }}
          >
            {showMale && <div className="text-[10px] sm:text-[11px] font-extrabold text-[#1e6fd6] dark:text-[#5aa2f5] text-right">♂ MAN</div>}
            <div className="text-[10px] sm:text-[11px] font-extrabold text-[#9098a4] text-center">AGE</div>
            {showFemale && <div className="text-[10px] sm:text-[11px] font-extrabold text-[#d6417f] dark:text-[#f072a6]">♀ WOMAN</div>}
            {groups.map(g => {
              const mRec = maleData?.[g];
              const fRec = femaleData?.[g];
              return (
                <React.Fragment key={g}>
                  {showMale && (mRec ? renderGenderCell('male', mRec) : <div />)}
                  <div className="text-[10px] sm:text-[11px] font-extrabold text-[#5b6470] text-center whitespace-nowrap">[{g}]</div>
                  {showFemale && (fRec ? renderGenderCell('female', fRec) : <div />)}
                </React.Fragment>
              );
            })}
          </div>
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
    // Конкретная возрастная группа выбрана явно — одна «лента» на пол.
    const cards = genderKeys
      .map(gk => {
        const record = distanceByGender[gk]?.[age];
        return record ? renderSingleAgeGroup(gk, age, record) : null;
      })
      .filter(Boolean);
    rendered = cards.length > 0
      ? <div className={cards.length > 1 ? 'grid gap-x-4 sm:grid-cols-2' : ''}>{cards}</div>
      : null;
  } else if (isSingleAge && resolvedAge) {
    // Возраст введён (год рождения) — резолвим в группу мастерс для каждого пола.
    const cards = genderKeys
      .map(gk => {
        const dd = distanceByGender[gk];
        if (!dd) return null;
        const ageGroup = findAgeGroup(resolvedAge, dd);
        const record = ageGroup ? dd[ageGroup] : null;
        return ageGroup && record ? renderSingleAgeGroup(gk, ageGroup, record) : null;
      })
      .filter(Boolean);
    rendered = cards.length > 0
      ? <div className={cards.length > 1 ? 'grid gap-x-4 sm:grid-cols-2' : ''}>{cards}</div>
      : null;
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
  return <div className="lg:max-w-[1180px] lg:mx-auto">{rendered}</div>;
}

export default NormativeMastersRecords;

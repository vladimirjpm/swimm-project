import React, { useState } from 'react';
import Helper from '../../../utils/helpers/data-helper';
import RecordsHelper, { maxUpdatedAtLabel } from '../../../utils/helpers/records-helper';
import { HOME_REGION } from '../../../utils/constants/home-region';

interface AgeRecord {
  time: string;
  name: string;
  club: string;
  country: string;
  record_date: string;
  updated_at?: string;
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

/**
 * Гендерные токены дизайна (design_handoff_age_records/README.md, свет+тьма).
 * Это статус-цвета пола, не тема — захардкожены с dark:-вариантами
 * (dark завязан на data-mode через @custom-variant в index.css).
 */
const GENDER_STYLES: Record<string, {
  label: string;
  accent: string;      // текст акцента
  accentBg: string;    // заливка акцентной кромки
  deep: string;        // основной текст
  soft: string;        // фон пилюли/чипа медали
  borderBg: string;    // вертикальный разделитель
}> = {
  male: {
    label: '♂ Man',
    accent: 'text-[#1e6fd6] dark:text-[#5aa2f5]',
    accentBg: 'bg-[#1e6fd6] dark:bg-[#5aa2f5]',
    deep: 'text-[#123a70] dark:text-[#dbe8fb]',
    soft: 'bg-[#eaf2fd] dark:bg-[rgba(90,162,245,0.16)]',
    borderBg: 'bg-[#d3e3f8] dark:bg-[#28344a]',
  },
  female: {
    label: '♀ Woman',
    accent: 'text-[#d6417f] dark:text-[#f072a6]',
    accentBg: 'bg-[#d6417f] dark:bg-[#f072a6]',
    deep: 'text-[#7a1f4b] dark:text-[#fbdcec]',
    soft: 'bg-[#fdeff5] dark:bg-[rgba(240,114,166,0.16)]',
    borderBg: 'bg-[#f6d3e3] dark:bg-[#412234]',
  },
};

function getStyles(genderKey: string) {
  return GENDER_STYLES[genderKey] ?? GENDER_STYLES.male;
}

const CARD_SURFACE = 'bg-white dark:bg-[#161b24]';
const CARD_SHADOW = { boxShadow: '0 1px 3px rgba(20,28,45,0.05)' };
const FALLBACK_RECORD: AgeRecord = { time: '—', name: '', club: '', country: '', record_date: '—' };

/**
 * «Один рекорд» (single age, single gender) — горизонтальная лента.
 * Один вариант разметки для всех ширин экрана — только responsive-размеры шрифта
 * (sm: = десктоп), без отдельной desktop/mobile логики.
 */
function renderOneAge(genderKey: string, resolvedAge: string, record: AgeRecord) {
  const s = getStyles(genderKey);
  const chip = `${s.label} · ${resolvedAge}y`;
  const updated = maxUpdatedAtLabel([record.updated_at]);

  return (
    <div
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
        <div className="text-[9px] sm:text-[12px] font-extrabold tracking-[0.08em] sm:tracking-[0.09em] uppercase text-[#9098a4] mb-[3px] sm:mb-1">Nat. Record</div>
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
 * «Два рекорда» (single age, gender=all) — одна карточка, две колонки.
 * Один вариант для всех ширин — компактнее, чем прежняя desktop-версия.
 */
function renderTwoAges(resolvedAge: string, maleRecord: AgeRecord, femaleRecord: AgeRecord) {
  const m = getStyles('male');
  const f = getStyles('female');
  const updated = maxUpdatedAtLabel([maleRecord.updated_at, femaleRecord.updated_at]);

  return (
    <div className={`${CARD_SURFACE} border border-[#e9edf3] dark:border-[#28344a] rounded-2xl sm:rounded-[18px] p-3.5 sm:p-5 mb-4`} style={CARD_SHADOW}>
      <div className="flex items-center justify-center gap-1.5 sm:gap-2 mb-3 sm:mb-4">
        <span className="text-sm sm:text-base">🏅</span>
        <span className="text-[10px] sm:text-[12px] font-extrabold tracking-[0.09em] uppercase text-[#9098a4]">National Record · {resolvedAge}y</span>
        {updated && (
          <span className="text-[9px] sm:text-[10px] font-semibold text-[#aab0bd] normal-case tracking-normal">· updated {updated}</span>
        )}
      </div>
      <div className="grid gap-3 sm:gap-5 items-stretch" style={{ gridTemplateColumns: '1fr 1px 1fr' }}>
        <div className="flex flex-col items-center gap-[5px] sm:gap-2 text-center">
          <span className={`text-[10px] sm:text-[12px] font-extrabold ${m.accent} ${m.soft} px-2 sm:px-3 py-0.5 sm:py-1 rounded-full`}>{m.label}</span>
          <div className={`text-[28px] sm:text-[34px] font-extrabold ${m.deep} tabular-nums tracking-[-1px] sm:tracking-[-1.2px] leading-none`}>{maleRecord.time}</div>
          <div dir="rtl" className={`text-[12.5px] sm:text-[14px] font-bold ${m.deep} whitespace-nowrap overflow-hidden text-ellipsis max-w-full`}>{maleRecord.name}</div>
          <div className="text-[10px] sm:text-[12px] text-[#aab0bd] sm:text-[#9098a4] tabular-nums">{maleRecord.record_date || '—'}</div>
        </div>
        <div className={m.borderBg} />
        <div className="flex flex-col items-center gap-[5px] sm:gap-2 text-center">
          <span className={`text-[10px] sm:text-[12px] font-extrabold ${f.accent} ${f.soft} px-2 sm:px-3 py-0.5 sm:py-1 rounded-full`}>{f.label}</span>
          <div className={`text-[28px] sm:text-[34px] font-extrabold ${f.deep} tabular-nums tracking-[-1px] sm:tracking-[-1.2px] leading-none`}>{femaleRecord.time}</div>
          <div dir="rtl" className={`text-[12.5px] sm:text-[14px] font-bold ${f.deep} whitespace-nowrap overflow-hidden text-ellipsis max-w-full`}>{femaleRecord.name}</div>
          <div className="text-[10px] sm:text-[12px] text-[#aab0bd] sm:text-[#9098a4] tabular-nums">{femaleRecord.record_date || '—'}</div>
        </div>
      </div>
    </div>
  );
}

/**
 * Ячейка результата: время у края, ближнего к колонке AGE (по центру таблицы),
 * имя+клуб+дата — блоком с внешней стороны, в одну строку с временем (не под ним).
 */
function renderGenderCell(genderKey: 'male' | 'female', rec: AgeRecord) {
  const isMale = genderKey === 'male';
  const color = isMale ? 'text-[#123a70] dark:text-[#dbe8fb]' : 'text-[#7a1f4b] dark:text-[#fbdcec]';
  const bg = isMale ? 'bg-[#f6faff] dark:bg-[#1a2436]' : 'bg-[#fff7fb] dark:bg-[#2a1a24]';
  const updated = maxUpdatedAtLabel([rec.updated_at]);

  const time = (
    <div className={`text-[15px] sm:text-[17px] font-extrabold ${color} tabular-nums leading-tight shrink-0`}>
      {rec.time}
    </div>
  );
  const info = (
    <div className="min-w-0 flex-1">
      <div dir="rtl" className={`text-[11px] sm:text-[12.5px] font-bold ${color} truncate`}>{rec.name}</div>
      <div dir="rtl" className="text-[10px] sm:text-[11px] text-[#8a93a3] truncate">{rec.club}</div>
      <div className="text-[9px] sm:text-[10px] text-[#aab0bd] tabular-nums">{rec.record_date || '—'}</div>
    </div>
  );

  return (
    <div
      className={`${bg} rounded-lg px-2.5 py-[5px] sm:py-2 flex items-center gap-2`}
      title={updated ? `updated ${updated}` : undefined}
    >
      {isMale ? <>{info}{time}</> : <>{time}{info}</>}
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
          <div
            className="grid gap-x-2 sm:gap-x-4 gap-y-1.5 sm:gap-y-2 items-center"
            style={{ gridTemplateColumns: `${showMale ? '1fr' : ''} 52px ${showFemale ? '1fr' : ''}`.trim() }}
          >
            {showMale && <div className="text-[10px] sm:text-[11px] font-extrabold text-[#1e6fd6] dark:text-[#5aa2f5] text-right">♂ MAN</div>}
            <div className="text-[10px] sm:text-[11px] font-extrabold text-[#9098a4] text-center">AGE</div>
            {showFemale && <div className="text-[10px] sm:text-[11px] font-extrabold text-[#d6417f] dark:text-[#f072a6]">♀ WOMAN</div>}
            {ages.map(a => {
              const mRec = maleData?.[a] ?? FALLBACK_RECORD;
              const fRec = femaleData?.[a] ?? FALLBACK_RECORD;
              return (
                <React.Fragment key={a}>
                  {showMale && renderGenderCell('male', mRec)}
                  <div className="text-[11px] sm:text-[12px] font-extrabold text-[#5b6470] text-center">{a}y</div>
                  {showFemale && renderGenderCell('female', fRec)}
                </React.Fragment>
              );
            })}
          </div>
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
    if (genderKeys.length === 2 && maleRecord && femaleRecord) {
      rendered = renderTwoAges(resolvedAge, maleRecord, femaleRecord);
    } else {
      const gk = genderKeys.find(g => distanceByGender[g]?.[resolvedAge]);
      const record = gk ? distanceByGender[gk]![resolvedAge] : null;
      rendered = gk && record ? renderOneAge(gk, resolvedAge, record) : null;
    }
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
  return <div className="lg:max-w-[1180px] lg:mx-auto">{rendered}</div>;
}

export default NormativeAgeRecords;

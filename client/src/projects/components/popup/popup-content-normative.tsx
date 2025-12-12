import React, { useMemo, useState } from 'react';
import { useAppSelector } from '../../../store/store';
import Helper from '../../../utils/helpers/data-helper';
import UI_NormativeLevelIcon from '../mix/normative-level-icon/normative-level-icon';
import UI_SwimmStyleIcon from '../mix/swimm-style-icon/swimm-style-icon';

// ==== Типы данных ====
type PoolType = '25m_pool' | '50m_pool';
type Gender = 'male' | 'female';
type LevelsMap = Record<string, number>;
type DistancesMap = Record<string, LevelsMap>;
type PoolMapLoose = Record<string, DistancesMap>;
type Normatives = {
  normatives: Record<Gender, Record<PoolType, PoolMapLoose>>;
};

type RecordCell = {
  time: number | null;
  name: string | null;
  country?: string | null;
  record_date?: string | null;
};
type RecordsTree = {
  normatives: Record<
    Gender,
    Record<
      PoolType,
      Record<
        string, // stroke
        Record<
          string, // distance
          {
            ISR?: RecordCell;
            WR?: RecordCell;
          }
        >
      >
    >
  >;
};

// Данные теперь приходят из глобальных переменных,
// заданных в public/data/normative.js и normative-records.js
// normative.js:          window.normative = { ... }
// normative-record.js:  window.normative_record = { ... }
// normatives-masters.js: window.normatives_masters = { ... }

const norms = (window as any).normative as Normatives;
const normsMasters = (window as any).normatives_masters ||
  (window as any).normativesMasters ||
  (window as any).normative_masters ||
  (window as any).normativeMasters ||
  null;
const recordsTree = (window as any).normative_record as RecordsTree;

const strokeLabel: Record<string, string> = {
  freestyle: 'Freestyle',
  butterfly: 'Butterfly',
  breaststroke: 'Breaststroke',
  backstroke: 'Backstroke',
  individual_medley: 'Individual Medley',
};

const unionKeys = (a: string[], b: string[]) => Array.from(new Set([...a, ...b]));

// ===== Нормализация значений из popUpObj =====
const derivePoolType = (val?: string): PoolType => {
  // If already a canonical pool type like '50m_pool' or '25m_pool', use it
  if (!val) return '25m_pool';
  const v = String(val).toLowerCase();
  if (v.includes('50')) return '50m_pool';
  if (v.includes('25')) return '25m_pool';
  // default fallback
  return '25m_pool';
};

const deriveStroke = (styleName?: string): string => {
  const key = (styleName || '').toLowerCase().trim();
  return Object.keys(strokeLabel).includes(key) ? key : 'freestyle';
};

// 50 -> 50m
const normalizeDistance = (v?: string) => {
  if (!v) return '';
  const s = String(v)
    .trim()
    .toLowerCase()
    .replace(/м/g, 'm')
    .replace(/\s+/g, '');
  const m = s.match(/^(\d+)(m)?$/);
  return m ? `${m[1]}m` : s;
};

// ===== helpers для рекордов =====
const safeFmt = (val: number | null | undefined) =>
  Number.isFinite(val as number)
    ? Helper.formatSecondsToTimeString(val as number)
    : '—';

const getRecordCell = (
  gender: Gender,
  poolType: PoolType,
  stroke: string,
  distance: string,
  kind: 'ISR' | 'WR',
): RecordCell | null => {
  const poolNode =
    recordsTree?.normatives?.[gender]?.[poolType] ||
    // fallback на 50м, если 25м нет
    recordsTree?.normatives?.[gender]?.['50m_pool'];
  const byStroke = poolNode?.[stroke];
  const byDist = byStroke?.[distance];
  return byDist?.[kind] ?? null;
};

const PopupContentNormative: React.FC = () => {
  const { levelName = '', styleName = '', styleLen = '', poolLen = '', poolType: popUpPoolType, isMasters = false, normativeAgeGroup } =
    useAppSelector((state) => state.popUpObj);

  // Приведение isMasters к булеву типу (строгое сравнение + строка/число)
  const isMastersBool = isMasters === true || isMasters === 'true' || isMasters === 1;

  console.log('PopupContentNormative :', levelName, styleName, styleLen, poolLen, popUpPoolType, isMasters, 'isMastersBool:', isMastersBool, normativeAgeGroup);

  const [poolType, setPoolType] = useState<PoolType>(derivePoolType(popUpPoolType ?? poolLen));
  const [stroke, setStroke] = useState<string>(deriveStroke(styleName));
  // selectedAgeGroup только если masters-режим, иначе пусто
  const [selectedAgeGroup, setSelectedAgeGroup] = useState<string>(isMastersBool ? (normativeAgeGroup ?? '25-29') : '');
  const normalizedStyleLen = useMemo(
    () => normalizeDistance(styleLen),
    [styleLen],
  );

  // Получаем список доступных возрастных групп из normatives-masters (только если isMastersBool)
  const availableAgeGroups = useMemo(() => {
    if (!isMastersBool || !normsMasters) return [];
    const malePool = normsMasters.normatives?.male?.[poolType];
    if (!malePool) return [];
    const firstStroke = Object.keys(malePool)[0];
    if (!firstStroke) return [];
    const firstDist = Object.keys(malePool[firstStroke])[0];
    if (!firstDist) return [];
    const ageGroups = Object.keys(malePool[firstStroke][firstDist]);
    return ageGroups.sort((a, b) => {
      const numA = parseInt(a.split('-')[0], 10);
      const numB = parseInt(b.split('-')[0], 10);
      return numA - numB;
    });
  }, [isMastersBool, poolType]);

  // Выбираем источник нормативов и структуру только если isMastersBool
  let maleStyle: DistancesMap = {};
  let femaleStyle: DistancesMap = {};
  if (isMastersBool && normsMasters) {
    console.log('[PopupContentNormative] Используется masters-логика, isMasters:', isMasters);
    const malePool = normsMasters.normatives.male[poolType] || {};
    const femalePool = normsMasters.normatives.female[poolType] || {};
    // Для masters структура: stroke -> distance -> ageGroup -> levels
    const resolveStyleData = (poolData: PoolMapLoose): DistancesMap => {
      const styleData = poolData[stroke] ?? {};
      if (!selectedAgeGroup) return styleData;
      const firstDistKey = Object.keys(styleData)[0];
      if (!firstDistKey) return styleData;
      const firstDistData = styleData[firstDistKey];
      if (!firstDistData || typeof firstDistData !== 'object') return styleData;
      const subKeys = Object.keys(firstDistData);
      const looksLikeAgeGroups = subKeys.length > 0 && subKeys.every(k => /^\d{2}-\d{2}$/.test(k));
      if (!looksLikeAgeGroups) return styleData;
      const result: DistancesMap = {};
      for (const dist of Object.keys(styleData)) {
        const distData = styleData[dist];
        if (distData && typeof distData === 'object' && distData[selectedAgeGroup]) {
          result[dist] = distData[selectedAgeGroup] as unknown as LevelsMap;
        }
      }
      return result;
    };
    maleStyle = resolveStyleData(malePool);
    femaleStyle = resolveStyleData(femalePool);
  } else {
    if (isMasters) {
      // Если сюда попали, значит isMasters не строго true (например, строка/число)
      console.warn('[PopupContentNormative] isMasters не строго true, используем обычные нормативы. Значение:', isMasters, typeof isMasters);
    }
    // Обычные нормативы
    const malePool = norms.normatives.male[poolType] || {};
    const femalePool = norms.normatives.female[poolType] || {};
    maleStyle = malePool[stroke] ?? {};
    femaleStyle = femalePool[stroke] ?? {};
  }

  const normToSeconds = (v: unknown): number => {
    if (typeof v === 'number' && isFinite(v)) return v;
    return Helper.parseTimeToSeconds(String(v ?? ''));
  };

  const sortDistances = (a: string, b: string) => {
    const numA = parseInt(a);
    const numB = parseInt(b);
    return numA - numB;
  };

  const allDistanceKeys = useMemo(() => {
    const keys = unionKeys(Object.keys(maleStyle), Object.keys(femaleStyle));
    return keys.sort(sortDistances);
  }, [maleStyle, femaleStyle]);

  // Приоритет уровней для сортировки (от слабого к сильному)
  const levelPriority: Record<string, number> = {
    'III_youth': 1,
    'II_youth': 2,
    'I_youth': 3,
    'III_adult': 4,
    'II_adult': 5,
    'I_adult': 6,
    // Masters levels
    '3': 4,
    '2': 5,
    '1': 6,
    'KMS': 7,
    'MS': 8,
    'MSMK': 9,
  };

  const levelKeys = useMemo(() => {
    const levelSet = new Set<string>();
    allDistanceKeys.forEach((dist) => {
      Object.keys(maleStyle[dist] || {}).forEach((k) => levelSet.add(k));
      Object.keys(femaleStyle[dist] || {}).forEach((k) => levelSet.add(k));
    });
    // Сортируем по приоритету (от сильного к слабому для отображения)
    return Array.from(levelSet).sort((a, b) => {
      const pa = levelPriority[a] ?? 0;
      const pb = levelPriority[b] ?? 0;
      return pb - pa; // от сильного к слабому
    });
  }, [allDistanceKeys, maleStyle, femaleStyle]);

  const isSelectedDistance = (dist: string) =>
    !!normalizedStyleLen && normalizeDistance(dist) === normalizedStyleLen;
  const isSelectedLevel = (lvl: string) =>
    !!levelName && lvl.toLowerCase() === levelName.toLowerCase();

  const msMkIndex = useMemo(() => {
    const i = levelKeys.findIndex((l) => l.toUpperCase() === 'MSMK');
    return i >= 0 ? i : levelKeys.length;
  }, [levelKeys]);


  return (
    <div className="max-h-[70vh] overflow-auto">
      <h2 className="text-xl font-bold mb-2">
        Normative Info
        {isMastersBool && selectedAgeGroup && (
          <span className="ml-2 text-base font-normal text-gray-600">
            for age group <span className="font-semibold">{selectedAgeGroup}</span>
          </span>
        )}
      </h2>

      {/* Фильтр бассейна */}
      <div className="mb-3 flex gap-2">
        {(['25m_pool', '50m_pool'] as PoolType[]).map((pt) => (
          <button
            key={pt}
            onClick={() => setPoolType(pt)}
            className={`px-3 py-1 rounded border text-sm ${
              poolType === pt
                ? 'bg-black text-white border-black'
                : 'bg-white text-gray-800 border-gray-300 hover:bg-gray-50'
            }`}
            aria-pressed={poolType === pt}
            title={pt === '25m_pool' ? '25m' : '50m'}
          >
            {pt === '25m_pool' ? '25m' : '50m'}
          </button>
        ))}
      </div>

      {/* Фильтр стиля */}
      <div className="mb-4 flex gap-3 flex-wrap">
        {Object.keys(strokeLabel).map((s) => (
          <button
            key={s}
            onClick={() => setStroke(s)}
            className={`flex items-center justify-center rounded-xl border p-1 transition shadow-sm ${
              stroke === s
                ? 'ring-2 ring-blue-500 border-blue-500 bg-blue-50'
                : 'border-gray-300 hover:bg-gray-50'
            }`}
            aria-pressed={stroke === s}
            title={strokeLabel[s]}
          >
            <UI_SwimmStyleIcon
              styleName={s}
              styleType="icon-notext"
              className="w-12 h-12"
            />
            <span className="sr-only">{strokeLabel[s]}</span>
          </button>
        ))}
      </div>

      {/* Фильтр возрастных групп для masters */}
      {isMastersBool && availableAgeGroups.length > 0 && (
        <div className="mb-4">
          <div className="text-sm text-gray-600 mb-2">Age Group:</div>
          <div className="flex gap-2 flex-wrap">
            {availableAgeGroups.map((ag) => (
              <button
                key={ag}
                onClick={() => setSelectedAgeGroup(ag)}
                className={`px-3 py-1 rounded border text-sm ${
                  selectedAgeGroup === ag
                    ? 'bg-blue-600 text-white border-blue-600'
                    : 'bg-white text-gray-800 border-gray-300 hover:bg-gray-50'
                }`}
                aria-pressed={selectedAgeGroup === ag}
              >
                {ag}
              </button>
            ))}
          </div>
        </div>
      )}

      <p className="mb-4 text-sm text-gray-600">
        In each cell: <span className="font-semibold">top</span> —
        <span className="inline-block ml-1 rounded px-2 py-0.5 bg-blue-100 text-blue-900 font-semibold">Men</span>
        <span className="mx-2">(blue),</span>
        <span className="font-semibold">bottom</span> —
        <span className="inline-block ml-1 rounded px-2 py-0.5 bg-pink-100 text-pink-900 font-semibold">Women</span>
        <span className="mx-2">(pink).</span>
      </p>

      <table className="min-w-full border border-gray-300 text-sm">
        <thead className="bg-gray-100">
          <tr>
            <th className="border p-2 text-left">Distance</th>

            {levelKeys.slice(0, msMkIndex).map((level) => (
              <th
                key={`lvl-pre-${level}`}
                className={`border p-2 text-center ${
                  isSelectedLevel(level) ? 'bg-yellow-100' : ''
                }`}
                title={level}
              >
                <UI_NormativeLevelIcon
                  levelName={level}
                  styleType="style-1"
                  styleSize="size-2"
                  className="flex justify-center"
                />
              </th>
            ))}

            <th className="border p-2 text-center" title="ISR national record">
              <div className="text-xs font-semibold">World-record</div>
            </th>
            <th className="border p-2 text-center" title="World record">
              <div className="text-xs font-semibold">ISR-record</div>
            </th>

            {levelKeys.slice(msMkIndex).map((level) => (
              <th
                key={`lvl-post-${level}`}
                className={`border p-2 text-center ${
                  isSelectedLevel(level) ? 'bg-yellow-100' : ''
                }`}
                title={level}
              >
                <UI_NormativeLevelIcon
                  levelName={level}
                  styleType="style-1"
                  styleSize="size-2"
                  className="flex justify-center"
                />
              </th>
            ))}
          </tr>
        </thead>

        <tbody>
          {allDistanceKeys.map((dist) => (
            <tr key={dist}>
              <td
                className={`border p-2 font-medium ${
                  isSelectedDistance(dist) ? 'bg-yellow-100' : ''
                }`}
              >
                {dist}
              </td>

              {levelKeys.slice(0, msMkIndex).map((level) => (
                <td
                  key={`cell-pre-${dist}-${level}`}
                  className={`border p-2 ${
                    isSelectedDistance(dist) && isSelectedLevel(level)
                      ? 'ring-2 bg-yellow-100'
                      : ''
                  }`}
                >
                  <div className="flex flex-col gap-1">
                    <div className="rounded px-2 py-1 text-center bg-blue-100 text-blue-900">
                      {Helper.formatSecondsToTimeString(
                        normToSeconds(maleStyle[dist]?.[level] ?? NaN),
                      )}
                    </div>
                    <div className="rounded px-2 py-1 text-center bg-pink-100 text-pink-900">
                      {Helper.formatSecondsToTimeString(
                        normToSeconds(femaleStyle[dist]?.[level] ?? NaN),
                      )}
                    </div>
                  </div>
                </td>
              ))}

              <td className="border p-2">
                <div className="flex flex-col gap-1">
                  {(['male', 'female'] as Gender[]).map((g) => {
                    const rawTime = getRecordCell(
                      g,
                      poolType,
                      stroke,
                      dist,
                      'WR',
                    )?.time;
                    const secs = Helper.parseTimeToSeconds(
                      String(rawTime ?? ''),
                    );
                    return (
                      <div
                        key={g}
                        className={`rounded px-2 py-1 text-center ${
                          g === 'male'
                            ? 'bg-blue-100 text-blue-900'
                            : 'bg-pink-100 text-pink-900'
                        }`}
                        title={`${
                          g === 'male' ? 'Men' : 'Women'
                        } World record`}
                      >
                        {Helper.formatSecondsToTimeString(secs)}
                      </div>
                    );
                  })}
                </div>
              </td>

              <td className="border p-2">
                <div className="flex flex-col gap-1">
                  {(['male', 'female'] as Gender[]).map((g) => {
                    const isrRaw = getRecordCell(
                      g,
                      poolType,
                      stroke,
                      dist,
                      'ISR',
                    )?.time;
                    const wrRaw = getRecordCell(
                      g,
                      poolType,
                      stroke,
                      dist,
                      'WR',
                    )?.time;

                    const isr = normToSeconds(isrRaw);
                    const wr = normToSeconds(wrRaw);

                    const diff =
                      Number.isFinite(isr) && Number.isFinite(wr)
                        ? (isr - wr).toFixed(2)
                        : null;

                    return (
                      <div
                        key={g}
                        className={`rounded px-2 py-1 text-center ${
                          g === 'male'
                            ? 'bg-blue-100 text-blue-900'
                            : 'bg-pink-100 text-pink-900'
                        }`}
                        title={`${
                          g === 'male' ? 'Men' : 'Women'
                        } ISR record`}
                      >
                        {Helper.formatSecondsToTimeString(isr)}
                        {diff !== null && (
                          <sup className="ml-1 text-xs">+{diff}</sup>
                        )}
                      </div>
                    );
                  })}
                </div>
              </td>

              {levelKeys.slice(msMkIndex).map((level) => (
                <td
                  key={`cell-post-${dist}-${level}`}
                  className={`border p-2 ${
                    isSelectedDistance(dist) && isSelectedLevel(level)
                      ? 'ring-2 bg-yellow-100'
                      : ''
                  }`}
                >
                  <div className="flex flex-col gap-1">
                    <div className="rounded px-2 py-1 text-center bg-blue-100 text-blue-900">
                      {Helper.formatSecondsToTimeString(
                        normToSeconds(maleStyle[dist]?.[level] ?? NaN),
                      )}
                    </div>
                    <div className="rounded px-2 py-1 text-center bg-pink-100 text-pink-900">
                      {Helper.formatSecondsToTimeString(
                        normToSeconds(femaleStyle[dist]?.[level] ?? NaN),
                      )}
                    </div>
                  </div>
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default PopupContentNormative;

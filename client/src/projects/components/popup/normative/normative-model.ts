import Helper from '../../../../utils/helpers/data-helper';
import { type AgeRecordsTree } from '../../../../utils/helpers/records-helper';

// Модель попапа нормативов: типы деревьев нормативов/рекордов, разбор popUpObj, лестница
// уровней, клетки рекордов. Без React — только данные.

// ==== Типы данных ====
export type PoolType = '25m_pool' | '50m_pool';
export type Gender = 'male' | 'female';
export type LevelsMap = Record<string, number>;
export type DistancesMap = Record<string, LevelsMap>;
export type PoolMapLoose = Record<string, DistancesMap>;
export type Normatives = {
  normatives: Record<Gender, Record<PoolType, PoolMapLoose>>;
};

export type RecordCell = {
  time: number | null;
  name: string | null;
  country?: string | null;
  record_date?: string | null;
  updated_at?: string;
};
export type RecordsTree = {
  normatives: Record<
    Gender,
    Record<
      PoolType,
      Record<
        string, // stroke
        Record<
          string, // distance
          {
            NR?: RecordCell;
            WR?: RecordCell;
          }
        >
      >
    >
  >;
};

export const strokeLabel: Record<string, string> = {
  freestyle: 'Freestyle',
  butterfly: 'Butterfly',
  breaststroke: 'Breaststroke',
  backstroke: 'Backstroke',
  individual_medley: 'Individual Medley',
};

// ===== Нормализация значений из popUpObj =====
export const derivePoolType = (val?: string): PoolType => {
  // If already a canonical pool type like '50m_pool' or '25m_pool', use it
  if (!val) return '25m_pool';
  const v = String(val).toLowerCase();
  if (v.includes('50')) return '50m_pool';
  if (v.includes('25')) return '25m_pool';
  // default fallback
  return '25m_pool';
};

export const deriveStroke = (styleName?: string): string => {
  const key = (styleName || '').toLowerCase().trim();
  return Object.keys(strokeLabel).includes(key) ? key : 'freestyle';
};

// 50 -> 50m
export const normalizeDistance = (v?: string) => {
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
export const getRecordCell = (
  recordsTree: RecordsTree,
  gender: Gender,
  poolType: PoolType,
  stroke: string,
  distance: string,
  kind: 'NR' | 'WR',
): RecordCell | null => {
  const poolNode =
    recordsTree?.normatives?.[gender]?.[poolType] ||
    // fallback на 50м, если 25м нет
    recordsTree?.normatives?.[gender]?.['50m_pool'];
  const byStroke = poolNode?.[stroke];
  const byDist = byStroke?.[distance];
  return byDist?.[kind] ?? null;
};

// Общий минимальный тип клетки рекорда — то немногое, что реально использует fmtRecord
// (OpenRecordCell и AgeRecordCell из records-helper.ts несовместимы целиком, но оба несут
// эти три поля).
export type RecordCellMin = { time: string | number | null; name: string | null; updated_at?: string };

// Клетка мастерского рекорда своей полосы: gender -> poolType -> stroke -> distance ->
// ageGroup, БЕЗ фоллбека на 50м бассейн (решение 2 этапа 11.4 — нет полосы, значит пусто).
export const getMastersRecordCell = (
  tree: AgeRecordsTree,
  gender: Gender,
  poolType: PoolType,
  stroke: string,
  distance: string,
  ageGroup: string,
): RecordCellMin | null => {
  const cell = tree?.normatives?.[gender]?.[poolType]?.[stroke]?.[distance]?.[ageGroup];
  return cell ?? null;
};

// Категория уровня — по ней красится медальон (палитра — normative-tokens.ts); rec — рекорды.
export type Cat = 'pro' | 'adult' | 'youth' | 'rec';

// Лестница уровней (сильный → слабый, слева направо)
export const LADDER: { key: string; label: string; cat: Cat }[] = [
  { key: 'MSMK', label: 'MSMK', cat: 'pro' },
  { key: 'MS', label: 'MS', cat: 'pro' },
  { key: 'KMS', label: 'KMS', cat: 'pro' },
  { key: 'I_adult', label: 'I', cat: 'adult' },
  { key: 'II_adult', label: 'II', cat: 'adult' },
  { key: 'III_adult', label: 'III', cat: 'adult' },
  { key: 'I_youth', label: '1y', cat: 'youth' },
  { key: 'II_youth', label: '2y', cat: 'youth' },
  { key: 'III_youth', label: '3y', cat: 'youth' },
];

// Приоритет уровней для сортировки masters-лестницы (от слабого к сильному)
export const LEVEL_PRIORITY: Record<string, number> = {
  'III_youth': 1, 'II_youth': 2, 'I_youth': 3,
  'III_adult': 4, 'II_adult': 5, 'I_adult': 6,
  // Masters levels
  '3': 4, '2': 5, '1': 6,
  'KMS': 7, 'MS': 8, 'MSMK': 9,
};

export const LEVEL_LABELS: Record<string, string> = {
  MSMK: 'MSMK', MS: 'MS', KMS: 'KMS',
  I_adult: 'I', II_adult: 'II', III_adult: 'III',
  I_youth: '1y', II_youth: '2y', III_youth: '3y',
  '1': 'I', '2': 'II', '3': 'III',
};

export const catOfLevel = (key: string): Cat => {
  const k = key.toLowerCase();
  if (k.includes('youth')) return 'youth';
  if (k === 'kms' || k === 'ms' || k === 'msmk') return 'pro';
  return 'adult';
};

// Норматив бывает числом секунд или строкой «1:02.35» — к секундам и обратно в текст.
export const normToSeconds = (v: unknown): number => {
  if (typeof v === 'number' && isFinite(v)) return v;
  return Helper.parseTimeToSeconds(String(v ?? ''));
};
export const fmt = (v: unknown) => Helper.formatSecondsToTimeString(normToSeconds(v));

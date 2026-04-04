/**
 * Хелпер для работы с нормативами
 */
import { NormativeLevelInfo } from '../interfaces/normative-level-info';
import HelperTime from './helper-time';
import { Gender } from './helper-gender';

export type PoolType = '25m_pool' | '50m_pool';

type Normatives = {
  normatives: Record<
    Gender,
    Record<
      PoolType,
      Record<
        string, // styleName
        Record<string, unknown> // distance -> level -> time
      >
    >
  >;
};

// Загрузка нормативов из window
const normsDefault = (window as any).normative as Normatives;
const normsMasters =
  (window as any).normatives_masters ||
  (window as any).normativesMasters ||
  (window as any).normative_masters ||
  (window as any).normativeMasters ||
  null;

export default class HelperNormative {
  /** Minimum age to qualify as masters */
  static readonly MIN_MASTERS_AGE = 25;

  /**
   * Determines if a specific result qualifies as masters.
   * Even if the source is masters, results with age below MIN_MASTERS_AGE are not masters.
   */
  static isResultMasters(isMastersSource: boolean, eventStyleAge?: string | number | null): boolean {
    if (!isMastersSource) return false;
    if (eventStyleAge == null || eventStyleAge === '') return false;
    const age = Number(eventStyleAge);
    if (isNaN(age)) return false;
    return age >= HelperNormative.MIN_MASTERS_AGE;
  }

  /**
   * Нормализует тип бассейна
   */
  static resolvePoolType(poolType: unknown): PoolType {
    const value = poolType === null || poolType === undefined
      ? ''
      : String(poolType).trim().toLowerCase();

    if (value === '25' || value === '25m' || value === '25m_pool') return '25m_pool';
    if (value === '50' || value === '50m' || value === '50m_pool') return '50m_pool';
    if (value.includes('25')) return '25m_pool';
    return '50m_pool';
  }

  /**
   * Получает информацию об уровне норматива для результата
   */
  static getNormativeLevelInfo({
    gender,
    poolType,
    styleName,
    distance,
    time,
    isMaster = false,
    ageGroup,
    event_style_age,
  }: {
    gender: Gender;
    poolType: PoolType;
    styleName: string;
    distance: string;
    time: number;
    isMaster?: boolean;
    ageGroup?: string | null;
    event_style_age?: string | null;
  }): NormativeLevelInfo {
    const source = isMaster && normsMasters ? normsMasters : normsDefault;

    const poolData = source?.normatives[gender]?.[poolType];
    if (!poolData) {
      return defaultResult();
    }

    const styleData = poolData[styleName as keyof typeof poolData];
    if (!styleData) {
      return defaultResult();
    }

    // For masters normative files the structure may be distance -> event_style_age -> levels
    let styleNorms: Record<string, unknown> | undefined = styleData[distance as keyof typeof styleData];
    // Ключ возрастной группы из normative-masters (например "25-29")
    let resolvedNormativeAgeGroup: string | null = null;

    if (isMaster && normsMasters && styleNorms && typeof styleNorms === 'object') {
      // Detect if styleNorms keys look like age groups (e.g. "25-29")
      const keys = Object.keys(styleNorms);
      const looksLikeAgeGroups = keys.length > 0 && keys.every(k => /^\d{2}-\d{2}$/.test(k));
      if (looksLikeAgeGroups) {
        const agRaw = event_style_age ?? ageGroup ?? '';
        const ag = String(agRaw).trim();
        // If there is no age info provided for masters age-grouped norms, treat as no level
        if (!ag) {
          return defaultResult();
        }

        // prefer exact match, else try to find a key that contains the age (e.g. "25-29")
        if (styleNorms[ag]) {
          resolvedNormativeAgeGroup = ag;
          styleNorms = styleNorms[ag] as Record<string, unknown>;
        } else {
          // try to match numeric age within ranges
          const ageNum = parseInt(ag, 10);
          if (!Number.isNaN(ageNum)) {
            const foundKey = keys.find((k) => {
              const [a, b] = k.split('-').map((n) => parseInt(n, 10));
              return Number.isFinite(a) && Number.isFinite(b) && ageNum >= a && ageNum <= b;
            });
            if (foundKey) {
              resolvedNormativeAgeGroup = foundKey;
              styleNorms = styleNorms[foundKey] as Record<string, unknown>;
            }
          }
        }
      }
    }

    if (!styleNorms) {
      return defaultResult();
    }

    const numericStyleNorms = Object.fromEntries(
      Object.entries(styleNorms as Record<string, unknown>).map(([lvl, v]) => {
        if (typeof v === 'number') return [lvl, v];
        const sec = HelperTime.parseTimeToSeconds(String(v));
        return [lvl, Number.isFinite(sec) ? sec : Infinity];
      }),
    ) as Record<string, number>;

    const sortedLevels = Object.entries(numericStyleNorms).sort(
      ([, a], [, b]) => a - b,
    );

    let currentLevel: string = '—';
    let currentLevelTime: number | null = null;
    let nextLevel: string | null = null;
    let nextTime: number | null = null;

    for (let i = 0; i < sortedLevels.length; i++) {
      const [level, normTime] = sortedLevels[i];

      if (time <= normTime) {
        currentLevel = level;
        currentLevelTime = normTime;
        nextLevel = sortedLevels[i - 1]?.[0] ?? null;
        nextTime = sortedLevels[i - 1]?.[1] ?? null;
        break;
      }
    }

    if (currentLevel === '—') {
      const [weakestLevel, weakestTime] = sortedLevels[sortedLevels.length - 1];
      currentLevel = '—';
      currentLevelTime = null;
      nextLevel = weakestLevel;
      nextTime = weakestTime;
    }

    let progressToNextLevel: number | null = null;
    if (
      currentLevel !== '—' &&
      currentLevelTime !== null &&
      nextTime !== null &&
      currentLevelTime !== nextTime
    ) {
      progressToNextLevel =
        ((currentLevelTime - time) / (currentLevelTime - nextTime)) * 100;
      progressToNextLevel = Math.min(Math.max(progressToNextLevel, 0), 100);
      progressToNextLevel = parseFloat(progressToNextLevel.toFixed(1));
    }

    return {
      currentLevel,
      currentLevelTime:
        currentLevelTime !== null
          ? HelperTime.formatSecondsToTimeString(currentLevelTime)
          : '—',
      nextLevel,
      nextTime:
        nextTime !== null ? HelperTime.formatSecondsToTimeString(nextTime) : null,
      time: HelperTime.formatSecondsToTimeString(time),
      progressToNextLevel,
      normativeAgeGroup: isMaster ? resolvedNormativeAgeGroup : null,
    };

    function defaultResult(): NormativeLevelInfo {
      return {
        currentLevel: '—',
        currentLevelTime: '—',
        nextLevel: null,
        nextTime: null,
        time: time ? `${time}` : null,
        progressToNextLevel: null,
        normativeAgeGroup: null,
      };
    }
  }

  /**
   * Checks if the swimmer's name matches the record holder for their event.
   * Works for both age records and masters records.
   */
  static isRecordHolder({
    swimmerName,
    gender,
    poolType,
    styleName,
    distance,
    age,
    isMasters,
  }: {
    swimmerName: string;
    gender: string;
    poolType: string;
    styleName: string;
    distance: string;
    age: string | number | null | undefined;
    isMasters: boolean;
  }): boolean {
    if (!swimmerName || !styleName || !distance) return false;

    const data = isMasters
      ? (window as any).normative_masters_record
      : (window as any).normative_age_record;
    if (!data?.normatives) return false;

    const resolvedPool = HelperNormative.resolvePoolType(poolType);
    const distanceData = data.normatives?.[gender]?.[resolvedPool]?.[styleName]?.[distance];
    if (!distanceData) return false;

    const ageStr = age != null ? String(age).trim() : '';
    if (!ageStr) return false;

    // For masters: age key is age-group like "25-29", for non-masters: age key like "12"
    let record = distanceData[ageStr];

    // If no exact match for masters, try to find the age group range
    if (!record && isMasters) {
      const ageNum = parseInt(ageStr, 10);
      if (!isNaN(ageNum)) {
        for (const key of Object.keys(distanceData)) {
          const m = key.match(/^(\d+)-(\d+)$/);
          if (m && ageNum >= Number(m[1]) && ageNum <= Number(m[2])) {
            record = distanceData[key];
            break;
          }
        }
      }
    }

    if (!record?.name) return false;

    const nameA = swimmerName.trim();
    const nameB = record.name.trim();
    if (nameA === nameB) return true;

    // Check reversed order: "First Last" vs "Last First"
    const partsA = nameA.split(/\s+/);
    if (partsA.length === 2) {
      const reversed = `${partsA[1]} ${partsA[0]}`;
      if (reversed === nameB) return true;
    }

    return false;
  }
}

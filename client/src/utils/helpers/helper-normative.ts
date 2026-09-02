/**
 * Хелпер для работы с нормативами
 */
import { NormativeLevelInfo } from '../interfaces/normative-level-info';
import HelperTime from './helper-time';
import { Gender } from './helper-gender';
import RecordsHelper from './records-helper';

export type PoolType = '25m_pool' | '50m_pool';

/**
 * Класс рекорда, с которым сверился заплыв: национальный (`open` в справочнике), возрастная
 * ступень или мастерская полоса. Совпадает с `RecordKind` бейджа `UI_RecordBadge` минус
 * мировой — мировые рекорды витрина протокола не проверяет.
 */
export type RecordMarkKind = 'national' | 'age' | 'masters';

export interface RecordMark {
  kind: RecordMarkKind;
  /** Ступень для подсказки: «14» у возрастной, «45-49» у мастерской, null у национального. */
  scope: string | null;
}

/**
 * Верх детской лестницы справочника: дальше только `open` и `masters`. Зеркало серверной
 * константы `SwimmerPageBuilder.TopChildStepAge` — разъедутся, и один и тот же заплыв
 * получит разные бейджи в протоколе и на странице пловца.
 */
const TOP_CHILD_STEP_AGE = 18;

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
    const normsDefault = RecordsHelper.getStandards() as unknown as Normatives;
    const normsMasters = RecordsHelper.getMastersStandards() as unknown as Normatives;
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
    let styleNorms: Record<string, unknown> | undefined = styleData[distance as keyof typeof styleData] as
      | Record<string, unknown>
      | undefined;
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
  /**
   * Finds the record entry for a given event (gender/pool/style/distance/age).
   * Shared lookup logic for isRecordHolder and isRecordTime.
   */
  private static findRecord(params: {
    gender: string;
    poolType: string;
    styleName: string;
    distance: string;
    age: string | number | null | undefined;
    isMasters: boolean;
  }) {
    return HelperNormative.findRecordSteps(params)[0]?.record ?? null;
  }

  /**
   * Ступени справочника, с которыми сверяется ЭТОТ заплыв, от старшего класса к младшему.
   *
   * Зеркало серверного `SwimmerPageBuilder.RecordStepsOf`: справочник трёхосевой, и ступень
   * зависит от заплыва, а не только от возраста пловца — у мастерского старта это полоса
   * «45-49», у обычного взрослого открытый рекорд страны (`open`), у ребёнка его возрастная
   * ступень. Пока спрашивалась одна ступень «возраст», взрослые не получали бейджа вовсе:
   * держатель национального рекорда выглядел в протоколе обычным первым номером
   * (поймано 02.09.2026 на 00:56.73 Горбенко, 100 спина 25м).
   *
   * ⚠ Открытый рекорд — эталон ВЗРОСЛОГО. Ребёнку он не эталон, а шум: детская лестница
   * справочника кончается на 18, и сверять с ним девятилетнюю бессмысленно (та же отсечка
   * `IsAdultSwim` на сервере).
   */
  private static findRecordSteps({
    gender,
    poolType,
    styleName,
    distance,
    age,
    isMasters,
  }: {
    gender: string;
    poolType: string;
    styleName: string;
    distance: string;
    age: string | number | null | undefined;
    isMasters: boolean;
  }): { kind: RecordMarkKind; scope: string | null; record: any }[] {
    if (!styleName || !distance) return [];

    const resolvedPool = HelperNormative.resolvePoolType(poolType);
    const ageStr = age != null ? String(age).trim() : '';
    const ageNum = parseInt(ageStr, 10);
    const isAdult = isMasters || (!isNaN(ageNum) && ageNum > TOP_CHILD_STEP_AGE);
    const steps: { kind: RecordMarkKind; scope: string | null; record: any }[] = [];

    // 1. Национальный (open) — старший класс, поэтому первым.
    if (isAdult) {
      const openCell = (RecordsHelper.getOpenRecords().normatives as any)
        ?.[gender]?.[resolvedPool]?.[styleName]?.[distance]?.NR;
      if (openCell) steps.push({ kind: 'national', scope: null, record: openCell });
    }

    // 2. Возрастная ступень (обычный старт) либо мастерская полоса (мастерский).
    const data = isMasters ? RecordsHelper.getMastersRecords() : RecordsHelper.getAgeRecords();
    const distanceData = (data?.normatives as any)?.[gender]?.[resolvedPool]?.[styleName]?.[distance];
    if (distanceData && ageStr) {
      let record = distanceData[ageStr];
      let scope: string | null = ageStr;

      // Полоса мастерса задана диапазоном («45-49») — точного ключа по возрасту в ней нет.
      if (!record && isMasters && !isNaN(ageNum)) {
        for (const key of Object.keys(distanceData)) {
          const m = key.match(/^(\d+)-(\d+)$/);
          if (m && ageNum >= Number(m[1]) && ageNum <= Number(m[2])) {
            record = distanceData[key];
            scope = key;
            break;
          }
        }
      }

      if (record) steps.push({ kind: isMasters ? 'masters' : 'age', scope, record });
    }

    return steps;
  }

  /**
   * Рекорд, который БЬЁТ это время, либо null. Класс один — старший из подходящих
   * (национальный > возрастной > мастерский): два бейджа на одном времени не показываем.
   */
  static recordMarkForTime({
    time,
    gender,
    poolType,
    styleName,
    distance,
    age,
    isMasters,
  }: {
    time: string;
    gender: string;
    poolType: string;
    styleName: string;
    distance: string;
    age: string | number | null | undefined;
    isMasters: boolean;
  }): RecordMark | null {
    if (!time) return null;
    const swimmerTime = HelperTime.parseTimeToSeconds(time);
    if (!isFinite(swimmerTime)) return null;

    for (const step of HelperNormative.findRecordSteps({ gender, poolType, styleName, distance, age, isMasters })) {
      const recordTime = HelperTime.parseTimeToSeconds(step.record?.time);
      if (isFinite(recordTime) && swimmerTime <= recordTime) {
        return { kind: step.kind, scope: step.scope };
      }
    }
    return null;
  }

  /** Рекорд, который ДЕРЖИТ этот пловец в этой дисциплине, либо null (старший класс). */
  static recordMarkForHolder({
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
  }): RecordMark | null {
    if (!swimmerName) return null;

    for (const step of HelperNormative.findRecordSteps({ gender, poolType, styleName, distance, age, isMasters })) {
      if (HelperNormative.sameHolder(swimmerName, step.record?.name)) {
        return { kind: step.kind, scope: step.scope };
      }
    }
    return null;
  }

  /** Имя пловца и имя держателя — с поправкой на порядок «фамилия имя» в справочнике. */
  private static sameHolder(swimmerName: string, holderName: string | null | undefined): boolean {
    if (!holderName) return false;
    const nameA = swimmerName.trim();
    const nameB = holderName.trim();
    if (nameA === nameB) return true;

    const parts = nameA.split(/\s+/);
    return parts.length === 2 && parts[1] + ' ' + parts[0] === nameB;
  }

  /**
   * Checks if the swimmer's name matches the record holder for their event.
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
    return HelperNormative.recordMarkForHolder({
      swimmerName, gender, poolType, styleName, distance, age, isMasters,
    }) != null;
  }

  /**
   * Checks if the swimmer's time matches or beats the record time for their event.
   */
  static isRecordTime({
    time,
    gender,
    poolType,
    styleName,
    distance,
    age,
    isMasters,
  }: {
    time: string;
    gender: string;
    poolType: string;
    styleName: string;
    distance: string;
    age: string | number | null | undefined;
    isMasters: boolean;
  }): boolean {
    return HelperNormative.recordMarkForTime({
      time, gender, poolType, styleName, distance, age, isMasters,
    }) != null;
  }

  /**
   * Returns all records held by a swimmer across both age records and masters records.
   */
  static getSwimmerRecords(swimmerName: string): SwimmerRecord[] {
    if (!swimmerName) return [];

    const results: SwimmerRecord[] = [];
    const nameA = swimmerName.trim();
    const partsA = nameA.split(/\s+/);
    const reversedA = partsA.length === 2 ? `${partsA[1]} ${partsA[0]}` : null;

    const matchesName = (recordName: string) => {
      const nameB = recordName.trim();
      if (nameA === nameB) return true;
      if (reversedA && reversedA === nameB) return true;
      return false;
    };

    const scanData = (data: any, isMasters: boolean) => {
      if (!data?.normatives) return;
      for (const gender of Object.keys(data.normatives)) {
        const pools = data.normatives[gender];
        if (!pools) continue;
        for (const pool of Object.keys(pools)) {
          const styles = pools[pool];
          if (!styles) continue;
          for (const style of Object.keys(styles)) {
            const distances = styles[style];
            if (!distances) continue;
            for (const distance of Object.keys(distances)) {
              const ageKeys = distances[distance];
              if (!ageKeys) continue;
              for (const ageKey of Object.keys(ageKeys)) {
                const record = ageKeys[ageKey];
                if (!record?.name) continue;
                if (matchesName(record.name)) {
                  results.push({
                    gender,
                    pool,
                    style,
                    distance,
                    ageKey,
                    time: record.time,
                    issueReason: record.issue_reason ?? null,
                    club: record.club,
                    recordDate: record.record_date,
                    isMasters,
                  });
                }
              }
            }
          }
        }
      }
    };

    scanData(RecordsHelper.getAgeRecords(), false);
    scanData(RecordsHelper.getMastersRecords(), true);

    return results;
  }
}

export interface SwimmerRecord {
  gender: string;
  pool: string;
  style: string;
  distance: string;
  ageKey: string;
  time: string;
  /** Открытая претензия к записи справочника (И11 для рекордов). */
  issueReason?: string | null;
  club: string;
  recordDate: string;
  isMasters: boolean;
}

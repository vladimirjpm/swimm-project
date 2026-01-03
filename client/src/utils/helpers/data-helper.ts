// import normative from '../../DATA/normative';
import { NormativeLevelInfo } from '../interfaces/normative-level-info';
import { Result, TrainingGroup } from '../interfaces/results';

// normative.js должен лежать в public и задавать:
//   window.normative = { normatives: { ... } }
type PoolType = '25m_pool' | '50m_pool';
type Gender = 'male' | 'female' | 'none';
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

// Default normative set (public/data/normative.js)
const normsDefault = (window as any).normative as Normatives;
// Optional masters normative set (public/data/normatives-masters.js)
const normsMasters =
  (window as any).normatives_masters ||
  (window as any).normativesMasters ||
  (window as any).normative_masters ||
  (window as any).normativeMasters ||
  null;
//console.log('Loaded normative masters:', normsMasters);
export default class Helper {
  static resolvePoolType(poolType: unknown): PoolType {
    const value =
      poolType === null || poolType === undefined
        ? ''
        : String(poolType).trim().toLowerCase();

    if (value === '25' || value === '25m' || value === '25m_pool') {
      return '25m_pool';
    }
    if (value === '50' || value === '50m' || value === '50m_pool') {
      return '50m_pool';
    }

    if (value.includes('25')) {
      return '25m_pool';
    }

    return '50m_pool';
  }
  // Универсальная сортировка по времени
  static sortByTime(arr: Result[]): Result[] {
    const parseTime = (timeStr: string): number => {
      if (!timeStr || typeof timeStr !== 'string') return Infinity;

      const parts = timeStr.split(':');

      if (parts.length === 2) {
        const [minPart, secPart] = parts;
        const minutes = parseInt(minPart, 10);
        const seconds = parseFloat(secPart.replace(',', '.'));
        return isNaN(minutes) || isNaN(seconds
        ) ? Infinity : minutes * 60 + seconds;
      }

      if (parts.length === 1) {
        const seconds = parseFloat(parts[0].replace(',', '.'));
        return isNaN(seconds) ? Infinity : seconds;
      }

      return Infinity;
    };

    return [...arr].sort((a, b) => parseTime(a.time) - parseTime(b.time));
  }

  // === Плоская таблица: одна группа со всеми элементами ===
  static showTrainingTable(results: Result[]): TrainingGroup[] {
    const date = results[0]?.date ?? '';
    return [
      {
        title: 'All results',
        date,
        items: results.slice(),
      },
    ];
  }

  // === ГРУППИРОВКА ПО ИМЕНИ + ДАТЕ, сортировка по set/order ===
  static groupTrainingByName(results: Result[]): TrainingGroup[] {
    const nameOf = (r: Result) =>
      `${r.first_name ?? ''}${r.last_name ? ' ' + r.last_name : ''}`.trim() ||
      '—';
    const dateOf = (r: Result) => r.date ?? '';

    const groups = new Map<string, Result[]>();

    for (const r of results) {
      const key = `${nameOf(r)}||${dateOf(r)}`;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(r);
    }

    const bySetOrder = (a: Result, b: Result) => {
      const sa = Number(a?.training?.set ?? 0);
      const sb = Number(b?.training?.set ?? 0);
      if (sa !== sb) return sa - sb;
      const oa = Number(a?.training?.order ?? 0);
      const ob = Number(b?.training?.order ?? 0);
      return oa - ob;
    };

    const arr: TrainingGroup[] = Array.from(groups.entries()).map(
      ([key, items]) => {
        const [name, date] = key.split('||');
        return {
          title: name,
          name,
          date,
          items: items.slice().sort(bySetOrder),
        };
      },
    );

    arr.sort((g1, g2) => {
      const byName = (g1.name ?? g1.title).localeCompare(
        g2.name ?? g2.title,
        'he',
      );
      if (byName !== 0) return byName;
      return (g1.date ?? '').localeCompare(g2.date ?? '');
    });

    return arr;
  }

  // === ГРУППИРОВКА ПО SET + ДАТЕ, сортировка: order → time → name ===
  static groupTrainingBySet(results: Result[]): TrainingGroup[] {
    const keyOf = (r: Result) => `${r?.training?.set ?? 0}||${r.date ?? ''}`;
    const map = new Map<string, Result[]>();

    for (const r of results) {
      const k = keyOf(r);
      if (!map.has(k)) map.set(k, []);
      map.get(k)!.push(r);
    }

    const toSec = (t?: string | null) => {
      const s = Helper.parseTimeToSeconds(t ?? '');
      return Number.isFinite(s) ? s : Number.POSITIVE_INFINITY;
    };

    const byOrderTimeName = (a: Result, b: Result) => {
      const oa = Number(a?.training?.order ?? 0);
      const ob = Number(b?.training?.order ?? 0);
      if (oa !== ob) return oa - ob;

      const dTime = toSec(a.time) - toSec(b.time);
      if (dTime !== 0) return dTime;

      const an = `${a.first_name ?? ''} ${a.last_name ?? ''}`.trim();
      const bn = `${b.first_name ?? ''} ${b.last_name ?? ''}`.trim();
      return an.localeCompare(bn, 'he');
    };

    const arr: TrainingGroup[] = Array.from(map.entries()).map(
      ([key, items]) => {
        const [setStr, date] = key.split('||');
        const set = Number(setStr);
        return {
          title: `Set ${set}`,
          set,
          date,
          items: items.slice().sort(byOrderTimeName),
        };
      },
    );

    arr.sort((g1, g2) => {
      const s1 = g1.set ?? Number(g1.title.replace(/\D+/g, '') || 0);
      const s2 = g2.set ?? Number(g2.title.replace(/\D+/g, '') || 0);
      if (s1 !== s2) return s1 - s2;
      return (g1.date ?? '').localeCompare(g2.date ?? '');
    });

    return arr;
  }

  static getDolphinDataByName(data: Result[], name: string): Result[] {
    return data.filter((item) => item.first_name === name);
  }

  static getDolphinDataGroupedByDate(
    data: Result[],
  ): Record<string, Result[]> {
    const grouped: Record<string, Result[]> = data.reduce(
      (acc, item) => {
        if (!acc[item.date]) {
          acc[item.date] = [];
        }
        acc[item.date].push(item);
        return acc;
      },
      {} as Record<string, Result[]>,
    );

    Object.keys(grouped).forEach((date) => {
      grouped[date] = this.sortByTime(grouped[date]);
    });

    return grouped;
  }

  static parseTimeToSeconds(time: string | number): number {
    if (time === null || time === undefined) return Infinity;
    if (typeof time === 'number' && isFinite(time)) return time;
    if (typeof time !== 'string') return Infinity;

    const parts = time.trim().split(':');

    if (parts.length === 2) {
  const [minPart, secPart] = parts;
  const minutes = parseInt(minPart, 10);
  const seconds = parseFloat(secPart.replace(',', '.'));

  return (isNaN(minutes) || isNaN(seconds))
    ? Infinity
    : minutes * 60 + seconds;
}
    if (parts.length === 1) {
      const seconds = parseFloat(parts[0].replace(',', '.'));
      return isNaN(seconds) ? Infinity : seconds;
    }

    return Infinity;
  }

  static formatSecondsToTimeString(totalSeconds: number): string {
    if (!isFinite(totalSeconds)) return '—';

    const minutes = Math.floor(totalSeconds / 60);
    const seconds = Math.floor(totalSeconds % 60);
    const hundredths = Math.round(
      (totalSeconds - minutes * 60 - seconds) * 100,
    );

    if (minutes > 0) {
      return `${minutes}:${seconds.toString().padStart(2, '0')}.${hundredths
        .toString()
        .padStart(2, '0')}`;
    } else {
      return `${seconds}.${hundredths.toString().padStart(2, '0')}`;
    }
  }

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
    // legacy parameter name
    ageGroup?: string | null;
    // preferred parameter coming from results: event_style_age
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
    if (!styleNorms) {
      return defaultResult();
    }

    const numericStyleNorms = Object.fromEntries(
      Object.entries(styleNorms as Record<string, unknown>).map(([lvl, v]) => {
        if (typeof v === 'number') return [lvl, v];
        const sec = Helper.parseTimeToSeconds(String(v));
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
      const [weakestLevel, weakestTime] =
        sortedLevels[sortedLevels.length - 1];
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
          ? this.formatSecondsToTimeString(currentLevelTime)
          : '—',
      nextLevel,
      nextTime:
        nextTime !== null ? this.formatSecondsToTimeString(nextTime) : null,
      time: this.formatSecondsToTimeString(time),
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

  static getBestResultsByStyle(
    results: Result[],
    selectedName: string,
  ): Array<
    Result & { levelInfo: ReturnType<typeof Helper.getNormativeLevelInfo> }
  > {
    const levelPriority: Record<string, number> = {
      MSMK: 9,
      MS: 8,
      KMS: 7,
      I_adult: 6,
      II_adult: 5,
      III_adult: 4,
      I_youth: 3,
      II_youth: 2,
      III_youth: 1,
      '—': 0,
      '-': 0,
    };

    const filteredResults = results.filter((res) => {
      const nameLower = selectedName.toLowerCase();
      const fullName = `${res.first_name}${res.last_name ?? ''}`.toLowerCase();
      const fullNameWithSpace = `${res.first_name} ${
        res.last_name ?? ''
      }`.toLowerCase();

      // Проверка основного имени
      const matchesMain =
        res.first_name.toLowerCase() === nameLower ||
        fullName === nameLower ||
        fullNameWithSpace === nameLower;

      if (matchesMain) return true;

      // Для эстафеты проверяем участников в relay_swimmers
      const isRelay = res.is_relay === true || String(res.is_relay) === 'true';
      if (isRelay && res.relay_swimmers && res.relay_swimmers.length > 0) {
        return res.relay_swimmers.some((swimmer) => {
          const relayFullName = `${swimmer.first_name}${swimmer.last_name ?? ''}`.toLowerCase();
          const relayFullNameWithSpace = `${swimmer.first_name} ${swimmer.last_name ?? ''}`.toLowerCase();
          return (
            swimmer.first_name?.toLowerCase() === nameLower ||
            relayFullName === nameLower ||
            relayFullNameWithSpace === nameLower
          );
        });
      }

      return false;
    });

    const sortedResults = Helper.sortByTime(filteredResults);

    const groupedMap = new Map<string, Result>();

    sortedResults.forEach((res) => {
      const key = `${res.event_style_name}_${res.event_style_len}_${res.pool_type}`;
      const existing = groupedMap.get(key);

      if (
        !existing ||
        Helper.parseTimeToSeconds(res.time) <
          Helper.parseTimeToSeconds(existing.time)
      ) {
        groupedMap.set(key, res);
      }
    });
    const sortedBestResults = Array.from(groupedMap.values())
      .map((res) => {
        const isMaster = String(res.is_masters) === 'true' || String(res.is_masters) === '1';
        const resolvedGender = Helper.resolveGender(res.event_style_gender);
        const levelInfo = Helper.getNormativeLevelInfo({
          gender: resolvedGender,
          poolType: Helper.resolvePoolType(res.pool_type),
          styleName: res.event_style_name,
          distance: `${res.event_style_len}m`,
          time: Helper.parseTimeToSeconds(res.time),
          isMaster,
          event_style_age: res.event_style_age,
        });

        return levelInfo ? { ...res, levelInfo } : null;
      })
      .filter(
        (
          res,
        ): res is Result & {
          levelInfo: ReturnType<typeof Helper.getNormativeLevelInfo>;
        } => res !== null,
      )
      .sort((a, b) => {
        const levelA = levelPriority[a.levelInfo.currentLevel ?? '-'] ?? 0;
        const levelB = levelPriority[b.levelInfo.currentLevel ?? '-'] ?? 0;

        if (levelB !== levelA) return levelB - levelA;

        const progressA = a.levelInfo.progressToNextLevel ?? 0;
        const progressB = b.levelInfo.progressToNextLevel ?? 0;

        return progressB - progressA;
      });

    return sortedBestResults;
  }

  static getMedalCountsByName(
    results: Result[],
    selectedName: string,
  ): {
    first: Result[];
    second: Result[];
    third: Result[];
  } {
    const nameLower = selectedName.toLowerCase();

    const filteredResults = results.filter((res) => {
      const fullName = `${res.first_name}${res.last_name ?? ''}`.toLowerCase();
      const fullNameWithSpace = `${res.first_name} ${
        res.last_name ?? ''
      }`.toLowerCase();

      return (
        res.first_name.toLowerCase() === nameLower ||
        fullName === nameLower ||
        fullNameWithSpace === nameLower
      );
    });

    const grouped = {
      first: [] as Result[],
      second: [] as Result[],
      third: [] as Result[],
    };

    filteredResults.forEach((res) => {
      if (res.position !== null && res.position !== undefined) {
        const pos = Number(res.position);
        const note = `${res.event_style_name} ${res.event_style_len}м`;

        const resultWithNote: Result = {
          ...res,
          note,
        };

        if (pos === 1) grouped.first.push(resultWithNote);
        else if (pos === 2) grouped.second.push(resultWithNote);
        else if (pos === 3) grouped.third.push(resultWithNote);
      }
    });

    return grouped;
  }

  static getInternationalPointsSumByName(
    results: Result[],
    selectedName: string,
  ): number {
    const nameLower = selectedName.toLowerCase();

    const filteredResults = results.filter((res) => {
      const fullName = `${res.first_name}${res.last_name ?? ''}`.toLowerCase();
      const fullNameWithSpace = `${res.first_name} ${
        res.last_name ?? ''
      }`.toLowerCase();

      return (
        res.first_name.toLowerCase() === nameLower ||
        fullName === nameLower ||
        fullNameWithSpace === nameLower
      );
    });

    return filteredResults.reduce((sum, res) => {
      const points =
        typeof res.international_points === 'number'
          ? res.international_points
          : Number(res.international_points) || 0;
      return sum + points;
    }, 0);
  }

  static getClubsSummary(
    results: Result[],
  ): {
    club: string;
    points: number;
    swimmerCount: number;
    successfulCount: number;
    gold: number;
    silver: number;
    bronze: number;
  }[] {
    const map = new Map<
      string,
      {
        points: number;
        swimmerSet: Set<string>;
        successfulCount: number;
        gold: number;
        silver: number;
        bronze: number;
      }
    >();

    results.forEach((item) => {
      const club = item.club?.trim();
      if (!club) return;

      const entry =
        map.get(club) ||
        {
          points: 0,
          swimmerSet: new Set<string>(),
          successfulCount: 0,
          gold: 0,
          silver: 0,
          bronze: 0,
        };

      if (item.international_points) {
        entry.points += item.international_points;
      }

      if (item.last_name) {
        entry.swimmerSet.add(item.last_name.trim());
      }

      if (item.international_points && item.international_points > 0) {
        entry.successfulCount += 1;
      }

      if (item.position === 1) {
        entry.gold += 1;
      } else if (item.position === 2) {
        entry.silver += 1;
      } else if (item.position === 3) {
        entry.bronze += 1;
      }

      map.set(club, entry);
    });

    return Array.from(map.entries())
      .map(([club, data]) => ({
        club,
        points: data.points,
        swimmerCount: data.swimmerSet.size,
        successfulCount: data.successfulCount,
        gold: data.gold,
        silver: data.silver,
        bronze: data.bronze,
      }))
      .sort((a, b) => b.points - a.points);
  }

  /**
   * Возвращает CSS класс фона в зависимости от пола
   * @param gender - пол: 'male' | 'female' | 'none'
   * @returns Tailwind CSS класс фона
   */
  static getGenderBgClass(gender: string): string {
    if (gender === 'none') return 'bg-gray-100';
    return gender === 'female' ? 'bg-pink-100' : 'bg-blue-100';
  }

  /**
   * Нормализует значение пола к типу Gender
   * @param gender - значение пола из данных
   * @returns 'male' | 'female' | 'none'
   */
  static resolveGender(gender: unknown): Gender | 'none' {
    const value = gender === null || gender === undefined
      ? ''
      : String(gender).trim().toLowerCase();
    
    if (value === 'female' || value === 'f' || value === 'w') return 'female';
    if (value === 'male' || value === 'm') return 'male';
    if (value === 'none' || value === '') return 'none';
    return 'male';
  }
}

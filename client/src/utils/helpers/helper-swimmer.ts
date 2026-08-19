/**
 * Хелпер для работы со спортсменами (статистика, медали, очки)
 */
import { Result } from '../interfaces/results';
import HelperNormative from './helper-normative';
import HelperTime from './helper-time';
import HelperGender from './helper-gender';
import { NormativeLevelInfo } from '../interfaces/normative-level-info';

/** "DD/MM/YYYY" → timestamp (невалидная дата → 0, чтобы не ломать сортировку). */
const parseDateDMY = (d?: string): number => {
  if (!d) return 0;
  const [day, month, year] = d.split('/').map(Number);
  const t = new Date(year, (month || 1) - 1, day || 1).getTime();
  return isNaN(t) ? 0 : t;
};

/**
 * Спортсмен участвовал в результате: как основной пловец ИЛИ как участник эстафеты.
 * Эстафета матчится сперва по структурному `relay_swimmers[]` (static-JSON источники),
 * иначе по строке `relay_swimmers_name` — для API-источника структурного массива нет,
 * в БД эстафета хранится ОДНОЙ строкой Result на "первого" пловца команды
 * (см. [[favorites-media-feature]] / server ResultDto: relay_swimmers[] отложен).
 */
const matchesSwimmerName = (res: Result, nameLower: string): boolean => {
  const fullName = `${res.first_name}${res.last_name ?? ''}`.toLowerCase();
  const fullNameWithSpace = `${res.first_name} ${res.last_name ?? ''}`.toLowerCase();

  if (
    res.first_name.toLowerCase() === nameLower ||
    fullName === nameLower ||
    fullNameWithSpace === nameLower
  ) {
    return true;
  }

  const isRelay = res.is_relay === true || String(res.is_relay) === 'true';
  if (!isRelay) return false;

  if (res.relay_swimmers && res.relay_swimmers.length > 0) {
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

  if (res.relay_swimmers_name) {
    return res.relay_swimmers_name
      .split(',')
      .some((segment) => segment.trim().toLowerCase() === nameLower);
  }

  return false;
};

export default class HelperSwimmer {
  /**
   * Приоритет уровней для сортировки
   */
  static readonly levelPriority: Record<string, number> = {
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

  /**
   * ЕДИНСТВЕННОЕ правильное место матчинга «этот заплыв принадлежит пловцу N» по id.
   * Эстафеты матчатся по составу ног member_swimmer_ids, НЕ по swimmer_id владельца
   * строки — у эстафеты swimmer_id это одна нога, остальные теряются (docs/relays.md,
   * чек-лист п.3; репро-баг: 4X50 комплекс Сабины пропадал из ?filter=favorites).
   * Новый фильтр/счётчик по пловцу — зови это, не сравнивай swimmer_id сам.
   */
  static resultBelongsToSwimmer(res: Result, swimmerId: number): boolean {
    return res.swimmer_id === swimmerId || (res.member_swimmer_ids?.includes(swimmerId) ?? false);
  }

  /** То же для набора пловцов (скоуп favorites и т.п.). */
  static resultBelongsToAny(res: Result, swimmerIds: Iterable<number>): boolean {
    for (const id of swimmerIds) {
      if (HelperSwimmer.resultBelongsToSwimmer(res, id)) return true;
    }
    return false;
  }

  /**
   * Все результаты спортсмена по имени (включая эстафеты, где он участник).
   */
  static filterResultsByName(results: Result[], selectedName: string): Result[] {
    const nameLower = selectedName.toLowerCase();
    return results.filter((res) => matchesSwimmerName(res, nameLower));
  }

  /**
   * ВСЕ заплывы спортсмена (без группировки по стилям) с levelInfo,
   * отсортированные по дате, затем по времени — для карточки в режиме «соревнование».
   */
  static getAllResultsByName(
    results: Result[],
    selectedName: string,
    isMasters = false,
  ): Array<Result & { levelInfo: NormativeLevelInfo }> {
    return HelperSwimmer.filterResultsByName(results, selectedName)
      .map((res) => {
        const isMaster = HelperNormative.isResultMasters(isMasters, res.event_style_age);
        const resolvedGender = HelperGender.resolveGender(res.event_style_gender);
        const levelInfo = HelperNormative.getNormativeLevelInfo({
          gender: resolvedGender,
          poolType: HelperNormative.resolvePoolType(res.pool_type),
          styleName: res.event_style_name,
          distance: `${res.event_style_len}m`,
          time: HelperTime.parseTimeToSeconds(res.time),
          isMaster,
          event_style_age: res.event_style_age,
        });
        return levelInfo ? { ...res, levelInfo } : null;
      })
      .filter(
        (res): res is Result & { levelInfo: NormativeLevelInfo } => res !== null,
      )
      .sort((a, b) => {
        const dateDiff = parseDateDMY(a.date) - parseDateDMY(b.date);
        if (dateDiff !== 0) return dateDiff;
        return HelperTime.parseTimeToSeconds(a.time) - HelperTime.parseTimeToSeconds(b.time);
      });
  }

  /**
   * Получает лучшие результаты спортсмена по каждому стилю
   */
  static getBestResultsByStyle(
    results: Result[],
    selectedName: string,
    isMasters = false,
  ): Array<Result & { levelInfo: NormativeLevelInfo }> {
    const filteredResults = HelperSwimmer.filterResultsByName(results, selectedName);

    // Сортировка по времени
    const sortedResults = [...filteredResults].sort(
      (a, b) => HelperTime.parseTimeToSeconds(a.time) - HelperTime.parseTimeToSeconds(b.time)
    );

    const groupedMap = new Map<string, Result>();

    sortedResults.forEach((res) => {
      const key = `${res.event_style_name}_${res.event_style_len}_${res.pool_type}`;
      const existing = groupedMap.get(key);

      if (
        !existing ||
        HelperTime.parseTimeToSeconds(res.time) < HelperTime.parseTimeToSeconds(existing.time)
      ) {
        groupedMap.set(key, res);
      }
    });

    const sortedBestResults = Array.from(groupedMap.values())
      .map((res) => {
        const isMaster = HelperNormative.isResultMasters(isMasters, res.event_style_age);
        const resolvedGender = HelperGender.resolveGender(res.event_style_gender);
        const levelInfo = HelperNormative.getNormativeLevelInfo({
          gender: resolvedGender,
          poolType: HelperNormative.resolvePoolType(res.pool_type),
          styleName: res.event_style_name,
          distance: `${res.event_style_len}m`,
          time: HelperTime.parseTimeToSeconds(res.time),
          isMaster,
          event_style_age: res.event_style_age,
        });

        return levelInfo ? { ...res, levelInfo } : null;
      })
      .filter(
        (res): res is Result & { levelInfo: NormativeLevelInfo } => res !== null,
      )
      .sort((a, b) => {
        const levelA = HelperSwimmer.levelPriority[a.levelInfo.currentLevel ?? '-'] ?? 0;
        const levelB = HelperSwimmer.levelPriority[b.levelInfo.currentLevel ?? '-'] ?? 0;

        if (levelB !== levelA) return levelB - levelA;

        const progressA = a.levelInfo.progressToNextLevel ?? 0;
        const progressB = b.levelInfo.progressToNextLevel ?? 0;

        return progressB - progressA;
      });

    return sortedBestResults;
  }

  /**
   * Подсчёт медалей по имени спортсмена
   */
  static getMedalCountsByName(
    results: Result[],
    selectedName: string,
    isAward = false,
  ): {
    first: Result[];
    second: Result[];
    third: Result[];
  } {
    const nameLower = selectedName.toLowerCase();

    const filteredResults = results.filter((res) => matchesSwimmerName(res, nameLower));

    const grouped = {
      first: [] as Result[],
      second: [] as Result[],
      third: [] as Result[],
    };

    filteredResults.forEach((res) => {
      if (res.position !== null && res.position !== undefined && isAward) {
        const pos = Number(res.position);
        const note = `${res.event_style_name} ${res.event_style_len}m`;

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

  /**
   * Сумма международных очков спортсмена
   */
  static getInternationalPointsSumByName(
    results: Result[],
    selectedName: string,
  ): number {
    const nameLower = selectedName.toLowerCase();

    const filteredResults = results.filter((res) => {
      const fullName = `${res.first_name}${res.last_name ?? ''}`.toLowerCase();
      const fullNameWithSpace = `${res.first_name} ${res.last_name ?? ''}`.toLowerCase();

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
}

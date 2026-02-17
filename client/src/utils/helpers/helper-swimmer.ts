/**
 * Хелпер для работы со спортсменами (статистика, медали, очки)
 */
import { Result } from '../interfaces/results';
import HelperNormative from './helper-normative';
import HelperTime from './helper-time';
import HelperGender from './helper-gender';
import { NormativeLevelInfo } from '../interfaces/normative-level-info';

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
   * Получает лучшие результаты спортсмена по каждому стилю
   */
  static getBestResultsByStyle(
    results: Result[],
    selectedName: string,
  ): Array<Result & { levelInfo: NormativeLevelInfo }> {
    const filteredResults = results.filter((res) => {
      const nameLower = selectedName.toLowerCase();
      const fullName = `${res.first_name}${res.last_name ?? ''}`.toLowerCase();
      const fullNameWithSpace = `${res.first_name} ${res.last_name ?? ''}`.toLowerCase();

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
        const isMaster = String(res.is_masters) === 'true' || String(res.is_masters) === '1';
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
  ): {
    first: Result[];
    second: Result[];
    third: Result[];
  } {
    const nameLower = selectedName.toLowerCase();

    const filteredResults = results.filter((res) => {
      const fullName = `${res.first_name}${res.last_name ?? ''}`.toLowerCase();
      const fullNameWithSpace = `${res.first_name} ${res.last_name ?? ''}`.toLowerCase();

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

    const grouped = {
      first: [] as Result[],
      second: [] as Result[],
      third: [] as Result[],
    };

    filteredResults.forEach((res) => {
      if (res.position !== null && res.position !== undefined && String(res.is_award) === 'true') {
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

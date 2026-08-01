/**
 * Хелпер для статистики по клубам
 */
import { Result } from '../interfaces/results';
import ClubPointsHelper from './club-points-helper';

export default class HelperClub {
  /**
   * Получает сводку по клубам: очки, пловцы, медали
   */
  static async getClubsSummary(
    results: Result[],
  ): Promise<{
    club: string;
    points: number;
    swimmerCount: number;
    successfulCount: number;
    gold: number;
    silver: number;
    bronze: number;
  }[]> {
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

    // Очки берём из ответа сервера (Э6): он единственный знает, какое правило привязано к
    // соревнованию. Локальный расчёт подбирал правило по дате и на manual-правилах давал
    // другую сумму, чем зачёт в Overview (event 7: 1673 против 1568).
    // ClubPointsHelper остаётся ФОЛЛБЕКОМ для старых статических источников без club_points.
    const resultsWithPoints = await Promise.all(
      results.map(async (item) => ({
        item,
        clubPoints: item.club_points ?? await ClubPointsHelper.getPointsForResult(item)
      }))
    );

    // Теперь агрегируем по клубам
    resultsWithPoints.forEach(({ item, clubPoints }) => {
      const club =
        item.club?.trim() ||
        item.relay_team_name?.trim() ||
        item.club_en?.trim();
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

      // Используем вычисленные очки из системы начисления
      entry.points += clubPoints;

      // Пловец — по swimmer_id; фамилия только фоллбек для старых статических источников,
      // где id нет. По одной фамилии однофамильцы клуба схлопывались в одного пловца.
      const swimmerKey = item.swimmer_id != null
        ? `id:${item.swimmer_id}`
        : `n:${item.first_name?.trim() ?? ''}|${item.last_name?.trim() ?? ''}`;
      if (swimmerKey !== 'n:|') {
        entry.swimmerSet.add(swimmerKey);
      }

      // successfulCount — заплывы, ПРИНЁСШИЕ очки, а не все заплывы клуба (в UI — «scoring swims»)
      if (clubPoints > 0) {
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
}

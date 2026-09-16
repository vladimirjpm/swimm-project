using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Сырые строки одной дисциплины → рейтинг: места и отставание от мирового
/// (этап 11.2.1, plans/records-all-countries-plan.md).
///
/// Вынесено чистой функцией из репозитория нарочно: расстановка мест с совпадениями и
/// арифметика отставания — то, что легко сломать незаметно, и то, что не требует ни базы,
/// ни EF, чтобы это проверить (образец — <see cref="ClubStandingCalculator"/>).
///
/// ⚠ Места считаются по ВСЕЙ дисциплине, страницу репозиторий отрезает уже от готового
/// рейтинга. Это не лень: в дисциплине не больше одной строки на страну (~215 максимум),
/// а нумерация с середины требовала бы знать время и место строки ПЕРЕД страницей — иначе
/// страница, начавшаяся посреди группы одинаковых времён, соврёт в первом же месте.
/// </summary>
public static class RecordRankingBuilder
{
    /// <summary>Строка на входе: только то, что нужно рейтингу.</summary>
    /// <param name="TimeMs">
    /// Уже разобранное время. <c>null</c> сюда не приходит — строки с неразобранным временем
    /// отсеивает запрос, а сколько их было, едет отдельным счётчиком
    /// (<see cref="RecordRankingDto.UnparsedSkipped"/>).
    /// </param>
    public sealed record Row(
        string RegionCode,
        string Time,
        int TimeMs,
        string? HolderName,
        string? RecordDate,
        string? IssueReason);

    /// <summary>
    /// Расставляет места и считает отставание.
    /// </summary>
    /// <param name="ordered">
    /// ВСЕ строки дисциплины, уже отсортированные по времени — сортирует SQL по
    /// <c>TimeMs</c> (вычисляемая колонка), здесь порядок не переигрывается.
    /// </param>
    /// <param name="world">Мировой рекорд дисциплины или <c>null</c>, если его нет.</param>
    public static IReadOnlyList<RecordRankingRowDto> Build(
        IReadOnlyList<Row> ordered,
        RecordRankingWorldDto? world)
    {
        var rows = new List<RecordRankingRowDto>(ordered.Count);

        var rank = 0;
        int? previousMs = null;

        for (var i = 0; i < ordered.Count; i++)
        {
            var row = ordered[i];

            // Спортивные места: равное время — равное место, следующее перепрыгивает группу
            // (1, 2, 2, 4). Совпадения времён у разных стран — обычное дело, а не край.
            if (previousMs is null || row.TimeMs != previousMs) rank = i + 1;
            previousMs = row.TimeMs;

            rows.Add(new RecordRankingRowDto
            {
                Rank = rank,
                RegionCode = row.RegionCode,
                Time = row.Time,
                TimeMs = row.TimeMs,
                HolderName = row.HolderName,
                RecordDate = row.RecordDate,
                IssueReason = row.IssueReason,
                BehindWorldMs = world is null ? null : row.TimeMs - world.TimeMs,
                BehindWorldPercent = BehindPercent(row.TimeMs, world),
            });
        }

        return rows;
    }

    /// <summary>
    /// Отставание в процентах от мирового, до сотых. Делится на МИРОВОЕ, а не на время
    /// строки: «на 2 % медленнее рекорда» — утверждение про рекорд, и знаменатель обязан
    /// быть общим для всего рейтинга, иначе проценты соседних строк несравнимы между собой.
    /// </summary>
    private static double? BehindPercent(int timeMs, RecordRankingWorldDto? world)
    {
        if (world is null || world.TimeMs <= 0) return null;
        return Math.Round((timeMs - world.TimeMs) * 100.0 / world.TimeMs, 2);
    }
}

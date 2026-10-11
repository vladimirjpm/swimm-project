namespace Swimm.Application.Abstractions;

/// <summary>
/// Сбор отдельных соревнований в многодневное событие (docs/plans/multiday-split-plan.md, М4,
/// второй класс — «дни есть, события нет»). Обратная половина <see cref="ICompetitionDaySplitService"/>:
/// там строки одного соревнования раскладываются по дням, здесь дни уже лежат порознь —
/// импорт разложил старт по датам, но в событие не собрал (Эйлат-2025 #1676–1678).
///
/// Строки не двигаются: меняются только поля дней (<c>EventId</c>, <c>DayNumber</c>,
/// <c>SubName</c>) и заводится событие. Настройки дней (правило очков, «зачёт не ведётся»,
/// категории) команда НЕ выравнивает — только называет расхождения: это решение по данным.
/// </summary>
public interface ICompetitionDayJoinService
{
    /// <param name="competitionIds">Дни одного старта, в любом порядке (порядок дней — по дате).</param>
    /// <param name="apply">false — только план (по умолчанию в CLI).</param>
    Task<CompetitionDayJoinReport> JoinAsync(
        IReadOnlyList<int> competitionIds, bool apply, CancellationToken ct = default);
}

/// <summary>Итог сбора.</summary>
/// <param name="Differences">Настройки, которые у дней расходятся (на сбор не влияют).</param>
/// <param name="Refusal">Почему собирать нельзя; null — можно (или собрано).</param>
public sealed record CompetitionDayJoinReport(
    string Name,
    int? OrgCompId,
    IReadOnlyList<CompetitionDayJoinDay> Days,
    IReadOnlyList<string> Differences,
    string? Refusal,
    bool Applied,
    int? EventId)
{
    public static CompetitionDayJoinReport Refused(string name, string why) =>
        new(name, null, [], [], why, false, null);
}

public sealed record CompetitionDayJoinDay(int CompetitionId, DateOnly Date, int DayNumber, int? OrgCompId, int Rows);

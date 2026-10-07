namespace Swimm.Application.Abstractions;

/// <summary>
/// Обзор многодневок, которые лежат в базе не так, как плыли (docs/plans/multiday-split-plan.md, М4).
/// Только чтение — решение по каждой находке принимает Влад.
///
/// Два класса:
/// <list type="bullet">
/// <item><b>Схлопнуты</b> — в базе один день без события, а у сетки loglig дней больше (как были
/// #1645 и #64). Лечится <c>--split-days</c>; к каждой находке приложен его план (dry-run).</item>
/// <item><b>Дни есть, события нет</b> — импорт разложил старт по датам, но в событие не собрал
/// (Эйлат-2025 #1676–1678). Ищется по базе: одно имя и бассейн, дни в пределах недели.</item>
/// </list>
/// В реестр Health не входит: первому классу нужна сеть, а реестр читает только БД (довод Р13).
/// </summary>
public interface ICollapsedDaysReviewService
{
    /// <param name="pause">Пауза между запросами к loglig.</param>
    /// <param name="progress">Строка на каждый проверенный старт (для CLI); null — молча.</param>
    Task<CollapsedDaysReview> ReviewAsync(
        TimeSpan pause, Action<string>? progress = null, CancellationToken ct = default);
}

/// <param name="Checked">Сколько однодневных стартов сверено с сеткой.</param>
/// <param name="Failed">Старты, сетку которых получить не удалось, — с причиной.</param>
public sealed record CollapsedDaysReview(
    int Checked,
    IReadOnlyList<CollapsedDaysCandidate> Collapsed,
    IReadOnlyList<DaysWithoutEventGroup> DaysWithoutEvent,
    IReadOnlyList<string> Failed);

/// <param name="SourceDays">Дни по сетке loglig.</param>
/// <param name="Split">План <c>--split-days</c> (dry-run): раскладка строк или причина отказа.</param>
public sealed record CollapsedDaysCandidate(
    int DiscoveredId, int LogligId, int OrgCompId,
    IReadOnlyList<DateOnly> SourceDays, CompetitionDaySplitReport Split);

/// <summary>Отдельные соревнования, которые по всем признакам — дни одного старта.</summary>
public sealed record DaysWithoutEventGroup(string Name, string PoolType, IReadOnlyList<DaysWithoutEventDay> Days);

public sealed record DaysWithoutEventDay(int CompetitionId, DateOnly Date, int? OrgCompId, int Rows);

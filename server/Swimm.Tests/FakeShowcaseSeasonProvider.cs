using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Tests;

/// <summary>
/// Витринный сезон с заданным ответом — для тестов, которые проверяют, что репозиторий
/// СПРАШИВАЕТ шов, а не считает сезон сам (docs/season-boundary-rule.md).
///
/// Год берётся заведомо старый (данные теста сеются в тот же сезон): если код вернётся к
/// календарному <c>SeasonMath.CurrentStartYear()</c>, тест упадёт в любой день года — а не
/// только 1 сентября, как это случилось вживую.
/// </summary>
public sealed class FakeShowcaseSeasonProvider : IShowcaseSeasonProvider
{
    private readonly int _startYear;
    private readonly ShowcaseSeasonNoticeDto? _notice;

    /// <param name="notice">
    /// Заметка «новый сезон откроется после зимнего чемпионата». null (по умолчанию) —
    /// сезон открыт, витрина о границе молчит.
    /// </param>
    public FakeShowcaseSeasonProvider(int startYear, ShowcaseSeasonNoticeDto? notice = null)
    {
        _startYear = startYear;
        _notice = notice;
    }

    public Task<int> CurrentStartYearAsync(CancellationToken ct = default) =>
        Task.FromResult(_startYear);

    public Task<int> StartYearAtAsync(DateTime now, CancellationToken ct = default) =>
        Task.FromResult(_startYear);

    public Task<ShowcaseSeasonNoticeDto?> PendingNoticeAsync(CancellationToken ct = default) =>
        Task.FromResult(_notice);

    public Task<ShowcaseSeasonNoticeDto?> PendingNoticeAtAsync(
        DateTime now, CancellationToken ct = default) => Task.FromResult(_notice);
}

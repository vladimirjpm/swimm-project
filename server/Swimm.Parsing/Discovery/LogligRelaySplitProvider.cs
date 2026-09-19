using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Parsing.Parsers.IsrOrg;

namespace Swimm.Parsing.Discovery;

/// <summary>
/// Промежуточные эстафет: сетка дисциплин loglig → эстафетные дисциплины → PDF
/// «זמני ביניים» каждой → <see cref="LogligRelaySplitParser"/> → у мастерс-чемпионата
/// <see cref="RelayMastersBuilder"/> (эстафеты целиком), иначе <see cref="RelaySplitEnricher"/>.
///
/// Всё или ничего по сети: не скачалась или не разобралась хоть одна эстафетная дисциплина —
/// возвращаем исходный JSON. Полдоклейки хуже, чем ничего: по флагу «есть промежуточные» уже
/// не понять, какие эстафеты остались без них.
/// </summary>
public sealed class LogligRelaySplitProvider : IRelaySplitProvider
{
    private readonly ICompetitionDiscoveryProvider _loglig;
    private readonly ILogger<LogligRelaySplitProvider> _logger;

    public LogligRelaySplitProvider(ICompetitionDiscoveryProvider loglig, ILogger<LogligRelaySplitProvider> logger)
    {
        _loglig = loglig;
        _logger = logger;
    }

    public async Task<RelaySplitOutcome> EnrichAsync(int logligId, string resultsJson, CancellationToken ct = default)
    {
        try
        {
            var grid = await _loglig.FetchDisciplineGridAsync(logligId, ct);
            var relays = grid.Where(r => r.IsRelay).ToList();
            if (relays.Count == 0)
                return new RelaySplitOutcome(resultsJson, 0, "эстафет в программе нет");

            var events = new List<RelayBuildEvent>();
            foreach (var d in relays)
            {
                var pdf = await _loglig.FetchDisciplineSplitPdfAsync(logligId, d.DisciplineId, ct);
                using var ms = new MemoryStream(pdf);
                events.Add(new RelayBuildEvent(
                    ParserStyle(d.StyleName), d.Distance, RelayGender(d.Category, d.Gender), d.DisciplineRaw,
                    LogligRelaySplitParser.Parse(ms)));
            }

            // Мастерский формат (полоса у каждой команды) — эстафеты целиком из источника
            // (этап 2); иначе — доклейка к тому, что нашёл основной парсер (этап 1).
            if (RelayMastersBuilder.Build(resultsJson, events) is { } built)
                return new RelaySplitOutcome(built.Json, built.Relays,
                    $"эстафеты мастерс собраны из loglig: {built.Relays} команд в {events.Count} дисциплинах "
                    + $"(вместо {built.Replaced} из основного протокола)");

            var (json, report) = RelaySplitEnricher.Apply(resultsJson,
                events.Select(e => new RelaySplitEvent(e.Style, e.Len, e.Teams)).ToList());
            return new RelaySplitOutcome(json, report.Enriched, report.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Промежуточные эстафет loglig {LogligId} не доклеены", logligId);
            return new RelaySplitOutcome(resultsJson, 0, $"промежуточные эстафет не доклеены: {ex.Message}");
        }
    }

    /// <summary>
    /// Пол эстафеты по категории сетки. У мастерсов категория «מאסטרס נ 21-99» /
    /// «מאסטרס ג 21-99» / «מיקס מיקס 21-99», а разбор сетки (общий со стартовым протоколом)
    /// берёт пол из ПЕРВОГО слова — «מאסטרס» — и отдаёт none: на первом переимпорте
    /// 19.09.2026 все женские и мужские эстафеты легли микстом. Берём первое слово, которое
    /// означает пол; не нашлось — то, что дала сетка.
    /// </summary>
    internal static string RelayGender(string category, string gridGender)
    {
        foreach (var token in category.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var g = Swimm.Parsing.Helpers.HebrewTextHelper.NormalizeGenderHE(token);
            if (g is "female" or "male") return g;
        }
        return gridGender;
    }

    /// <summary>
    /// Стиль сетки loglig в форме основного парсера: комплексные эстафеты основной разбор
    /// пишет как <c>individual_medley</c> (NormalizeStyleName схлопывает medley).
    /// </summary>
    private static string ParserStyle(string gridStyle) =>
        gridStyle.Contains("medley", StringComparison.OrdinalIgnoreCase) ? "individual_medley" : gridStyle;
}

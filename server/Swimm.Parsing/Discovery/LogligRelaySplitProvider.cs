using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
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
            var (withIndividual, individualMessage) = await EnrichIndividualAsync(logligId, grid, resultsJson, ct);
            resultsJson = withIndividual;

            var relays = grid.Where(r => r.IsRelay).ToList();
            if (relays.Count == 0)
                return new RelaySplitOutcome(resultsJson, 0, $"эстафет в программе нет; {individualMessage}");

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
                    + $"(вместо {built.Replaced} из основного протокола); {individualMessage}");

            var (json, report) = RelaySplitEnricher.Apply(resultsJson,
                events.Select(e => new RelaySplitEvent(e.Style, e.Len, e.Teams)).ToList());
            return new RelaySplitOutcome(json, report.Enriched, $"{report}; {individualMessage}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Промежуточные эстафет loglig {LogligId} не доклеены", logligId);
            return new RelaySplitOutcome(resultsJson, 0, $"промежуточные эстафет не доклеены: {ex.Message}");
        }
    }

    /// <summary>
    /// Промежуточные личных заплывов: PDF «זמני ביניים» каждой личной дисциплины длиннее 50 м
    /// (у 50 м отрезков нет — лишний запрос к loglig). Сбой — своя строка в сводке, эстафеты
    /// и остальной импорт это не трогает: личные промежуточные — отдельная, необязательная часть.
    /// </summary>
    private async Task<(string Json, string Message)> EnrichIndividualAsync(
        int logligId, IReadOnlyList<LogligDisciplineGridRowDto> grid, string resultsJson, CancellationToken ct)
    {
        var individual = grid
            .Where(r => !r.IsRelay && int.TryParse(r.Distance, out var d) && d > 50)
            .ToList();
        if (individual.Count == 0) return (resultsJson, "личных дисциплин длиннее 50 м нет");

        try
        {
            var events = new List<IndividualSplitEvent>();
            foreach (var d in individual)
            {
                var pdf = await _loglig.FetchDisciplineSplitPdfAsync(logligId, d.DisciplineId, ct);
                using var ms = new MemoryStream(pdf);
                events.Add(new IndividualSplitEvent(
                    d.StyleName, d.Distance, RelayGender(d.Category, d.Gender), LogligIndividualSplitParser.Parse(ms)));
            }

            var (json, swims, enriched) = IndividualSplitEnricher.Apply(resultsJson, events);
            return (json, $"личные промежуточные: пловцов в источнике {swims}, доклеено {enriched}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Личные промежуточные loglig {LogligId} не доклеены", logligId);
            return (resultsJson, $"личные промежуточные не доклеены: {ex.Message}");
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
    private static string ParserStyle(string gridStyle) => ParserStyleOf(gridStyle);

    /// <inheritdoc cref="ParserStyle"/>
    /// <remarks>Тем же словарём пользуется доклейка без переимпорта (LogligSplitSourceProvider).</remarks>
    internal static string ParserStyleOf(string gridStyle) =>
        gridStyle.Contains("medley", StringComparison.OrdinalIgnoreCase) ? "individual_medley" : gridStyle;
}

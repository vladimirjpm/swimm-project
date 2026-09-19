using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Parsing.Parsers.IsrOrg;

namespace Swimm.Parsing.Discovery;

/// <summary>
/// Промежуточные эстафет: сетка дисциплин loglig → эстафетные дисциплины → PDF
/// «זמני ביניים» каждой → <see cref="LogligRelaySplitParser"/> → <see cref="RelaySplitEnricher"/>.
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

            var events = new List<RelaySplitEvent>();
            foreach (var d in relays)
            {
                var pdf = await _loglig.FetchDisciplineSplitPdfAsync(logligId, d.DisciplineId, ct);
                using var ms = new MemoryStream(pdf);
                events.Add(new RelaySplitEvent(ParserStyle(d.StyleName), d.Distance, LogligRelaySplitParser.Parse(ms)));
            }

            var (json, report) = RelaySplitEnricher.Apply(resultsJson, events);
            return new RelaySplitOutcome(json, report.Enriched, report.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Промежуточные эстафет loglig {LogligId} не доклеены", logligId);
            return new RelaySplitOutcome(resultsJson, 0, $"промежуточные эстафет не доклеены: {ex.Message}");
        }
    }

    /// <summary>
    /// Стиль сетки loglig в форме основного парсера: комплексные эстафеты основной разбор
    /// пишет как <c>individual_medley</c> (NormalizeStyleName схлопывает medley).
    /// </summary>
    private static string ParserStyle(string gridStyle) =>
        gridStyle.Contains("medley", StringComparison.OrdinalIgnoreCase) ? "individual_medley" : gridStyle;
}

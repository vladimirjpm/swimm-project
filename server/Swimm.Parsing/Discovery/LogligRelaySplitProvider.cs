using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers.IsrOrg;

namespace Swimm.Parsing.Discovery;

/// <summary>
/// СБОРКА МАСТЕРС-ЭСТАФЕТ из пособытийных данных loglig: сетка дисциплин → эстафетные
/// дисциплины → PDF «זמני ביניים» каждой → <see cref="LogligRelaySplitParser"/> →
/// <see cref="RelayMastersBuilder"/> (эстафеты целиком, docs/relays.md, этап 2).
///
/// ⚠ **Промежуточных этот путь больше не доклеивает** (решение Влада 20.09.2026, Д7 плана
/// docs/plans/splits-attach-without-repull-plan.md): за них отвечает отдельная операция
/// <c>--attach-splits</c> (<see cref="ISplitAttachService"/>), которая пишет к строкам БАЗЫ
/// и потому не может задвоить соревнование. Здесь остаётся только то, что МЕНЯЕТ САМИ СТРОКИ
/// и обязано ехать вместе с импортом: у мастерс-чемпионата полоса и пол команды берутся из
/// источника, а не из протокола, и после импорта их уже не доклеить.
///
/// Всё или ничего по сети: не скачалась или не разобралась хоть одна эстафетная дисциплина —
/// возвращаем исходный JSON. Полсборки хуже, чем ничего.
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
            // (этап 2).
            if (RelayMastersBuilder.Build(resultsJson, events) is { } built)
                return new RelaySplitOutcome(built.Json, built.Relays,
                    $"эстафеты мастерс собраны из loglig: {built.Relays} команд в {events.Count} дисциплинах "
                    + $"(вместо {built.Replaced} из основного протокола)");

            // Остальным импорт правит СОСТАВ и ВЛАДЕЛЬЦА строки (решение Влада 20.09.2026):
            // владелец обязан быть первой ногой, иначе строка остаётся за пловцом-тенью из
            // порванной переносом фамилии, а после импорта это уже не починить. ВРЕМЕНА
            // ЭТАПОВ здесь не пишем — их пишет --attach-splits по строкам базы (Д7).
            var (json, report) = RelaySplitEnricher.Apply(resultsJson,
                events.Select(e => new RelaySplitEvent(e.Style, e.Len, e.Teams)).ToList(),
                writeSplitTimes: false);
            return new RelaySplitOutcome(json, report.Enriched,
                $"состав эстафет из loglig: команд {report.Teams}, уточнено {report.Enriched}; "
                + "времена этапов — отдельной операцией --attach-splits");
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
    private static string ParserStyle(string gridStyle) => ParserStyleOf(gridStyle);

    /// <inheritdoc cref="ParserStyle"/>
    /// <remarks>Тем же словарём пользуется доклейка без переимпорта (LogligSplitSourceProvider).</remarks>
    internal static string ParserStyleOf(string gridStyle) =>
        gridStyle.Contains("medley", StringComparison.OrdinalIgnoreCase) ? "individual_medley" : gridStyle;
}

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
/// возвращаем исходный JSON. Полсборки хуже, чем ничего. Исключение — пустой PDF промежуточных
/// при живых результатах: такую дисциплину берём со страницы результатов (<see cref="TeamsFromResults"/>),
/// но только для сборки мастерса; доклейке состава без ног она не годится.
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
            var fromResultsPage = new List<int>();
            foreach (var d in relays)
            {
                IReadOnlyList<SplitRelayTeam> teams;
                try
                {
                    var pdf = await _loglig.FetchDisciplineSplitPdfAsync(logligId, d.DisciplineId, ct);
                    using var ms = new MemoryStream(pdf);
                    teams = LogligRelaySplitParser.Parse(ms);
                }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
                {
                    // PDF промежуточных бывает пустым при живых результатах (6595, loglig 11586:
                    // мужская 4X50 комплекс 58140) — и одна такая дисциплина роняла сборку мастерса
                    // целиком. Команды берём со страницы результатов: полоса, место, клуб, время,
                    // имена ног. Состав с годами — из протокола (RelayMastersBuilder.BorrowLegs).
                    _logger.LogWarning("loglig {LogligId}: промежуточных дисциплины {DisciplineId} нет ({Error}), "
                                       + "команды — со страницы результатов", logligId, d.DisciplineId, ex.Message);
                    teams = TeamsFromResults((await _loglig.FetchEventResultsAsync(d.DisciplineId, ct)).Rows);
                    fromResultsPage.Add(d.DisciplineId);
                }

                events.Add(new RelayBuildEvent(
                    ParserStyle(d.StyleName), d.Distance, RelayGender(d.Category, d.Gender), d.DisciplineRaw,
                    teams));
            }

            var fallbackNote = fromResultsPage.Count == 0
                ? ""
                : $"; без PDF промежуточных, со страницы результатов: {string.Join(", ", fromResultsPage)}";

            // Мастерский формат (полоса у каждой команды) — эстафеты целиком из источника
            // (этап 2).
            if (RelayMastersBuilder.Build(resultsJson, events) is { } built)
                return new RelaySplitOutcome(built.Json, built.Relays,
                    $"эстафеты мастерс собраны из loglig: {built.Relays} команд в {events.Count} дисциплинах "
                    + $"(вместо {built.Replaced} из основного протокола){fallbackNote}");

            // Команды со страницы результатов ног не несут — доклейке состава их отдавать нельзя
            // (она переписывает состав и владельца строки). Как и раньше без PDF: ничего не трогаем.
            if (fromResultsPage.Count > 0)
                return new RelaySplitOutcome(resultsJson, 0,
                    $"промежуточные эстафет не доклеены: нет PDF у дисциплин {string.Join(", ", fromResultsPage)}, "
                    + "а сборка мастерса не подошла");

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
    /// Команды эстафеты со страницы результатов дисциплины — замена пустому PDF промежуточных.
    /// Ног нет (на странице имена без годов, а фамилию от имени в «רבינוביץ בץ» не отделить),
    /// поэтому <see cref="SplitRelayTeam.Legs"/> пуст, а имена — в <see cref="SplitRelayTeam.LegNames"/>.
    /// Полоса — хвост подзаголовка секции «מאסטרס שליחים 120-159» КАК У LOGLIG, даже личная
    /// пятилетка «מאסטרס ג 30-34» у трёх команд 6595 (решение Влада 07.10.2026: зачёт и медали
    /// организатор считал по ним; ошибку источника помечаем, а не правим).
    /// </summary>
    internal static IReadOnlyList<SplitRelayTeam> TeamsFromResults(IEnumerable<LogligResultRowDto> rows) =>
        rows.Select(r => new SplitRelayTeam(
                r.Heat, r.Lane, r.Time, r.Club, [],
                Band: BandOf(r.Category),
                Status: r.Time == null ? r.FailNote : null,
                LegNames: r.FullName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .ToList();

    /// <summary>
    /// Полоса только у МАСТЕРСКОЙ секции: полоса — признак мастерса для сборщика
    /// (<see cref="RelayMastersBuilder.CanBuild"/>), а детская «בנים 13-14» сделала бы мастерсом
    /// эстафеты Маккаби. Тот же смысл, что строка «NNN-NNN מאסטרס שליחים» в PDF.
    /// </summary>
    private static string? BandOf(string category)
    {
        if (!category.Contains("מאסטרס", StringComparison.Ordinal)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(category, @"(\d+-\d+)\s*$");
        return m.Success ? m.Groups[1].Value : null;
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
            // «מיקס» — смешанная эстафета, а не «пол неизвестен» (Э4).
            if (Swimm.Parsing.Helpers.HebrewTextHelper.IsMixToken(token)) return "mixed";
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

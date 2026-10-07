using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Parsing.Parsers.IsrOrg;

namespace Swimm.Parsing.Discovery;

/// <summary>
/// Промежуточные loglig для доклейки БЕЗ переимпорта: сетка дисциплин → PDF «זמני ביניים»
/// каждой → разбор → плоские записи источника (<see cref="SplitSource"/>).
///
/// От <see cref="LogligRelaySplitProvider"/> отличается только тем, что НИЧЕГО не дописывает:
/// сопоставлять со строками базы будет <see cref="SplitAttachMatcher"/>. Скачивание, разбор
/// и оба перевода словаря (пол эстафеты из категории, комплекс → individual_medley) общие с
/// ним — переписывать их здесь нельзя, разъедутся.
///
/// «Всё или ничего» по ЭСТАФЕТАМ сохраняем: не скачалась или не разобралась хоть одна
/// эстафетная дисциплина — бросаем, и доклейка не пишет ничего. Полдоклейки хуже, чем
/// ничего: по флагу «есть промежуточные» уже не понять, какие эстафеты остались без них.
/// Личные — часть необязательная: их сбой не роняет эстафеты, а уходит строкой в отчёт.
/// Исключение из «всё или ничего» — дисциплина, у которой loglig PDF промежуточных не отдаёт
/// вовсе (<see cref="LogligSplitPdfMissingException"/>, 6595 / 58140): её пропускаем и называем
/// в отчёте — доклеивать ей нечего, а из-за неё не доклеивалось всё соревнование.
/// </summary>
public sealed class LogligSplitSourceProvider : ISplitSourceProvider
{
    private readonly ICompetitionDiscoveryProvider _loglig;
    private readonly ILogger<LogligSplitSourceProvider> _logger;

    public LogligSplitSourceProvider(
        ICompetitionDiscoveryProvider loglig, ILogger<LogligSplitSourceProvider> logger)
    {
        _loglig = loglig;
        _logger = logger;
    }

    public async Task<SplitSource> FetchAsync(int logligId, CancellationToken ct = default)
    {
        var grid = await _loglig.FetchDisciplineGridAsync(logligId, ct);

        // Дисциплины, у которых loglig промежуточных не отдаёт вовсе (пустой ответ вместо PDF).
        var missing = new List<int>();

        var teams = new List<SplitSourceTeam>();
        var relays = grid.Where(r => r.IsRelay).ToList();
        foreach (var d in relays)
        {
            var parsed = await ParseOrSkipAsync(logligId, d.DisciplineId, missing, ct);
            if (parsed == null) continue;
            var style = LogligRelaySplitProvider.ParserStyleOf(d.StyleName);
            teams.AddRange(LogligRelaySplitParser.Parse(parsed)
                .Select(t => new SplitSourceTeam(style, d.Distance, t.Heat, t.Lane, t.Time,
                    t.Legs.Select(l => new SplitSourceLeg(l.Order, l.BirthYear, l.SplitTime)).ToList())));
        }

        // У 50 м отрезков нет — лишний запрос к loglig (то же правило, что у пути импорта).
        var individual = grid
            .Where(r => !r.IsRelay && int.TryParse(r.Distance, out var dist) && dist > 50)
            .ToList();
        var swims = new List<SplitSourceSwim>();
        string swimsMessage;
        try
        {
            foreach (var d in individual)
            {
                var parsed = await ParseOrSkipAsync(logligId, d.DisciplineId, missing, ct);
                if (parsed == null) continue;
                var gender = LogligRelaySplitProvider.RelayGender(d.Category, d.Gender);
                swims.AddRange(LogligIndividualSplitParser.Parse(parsed)
                    .Select(s => new SplitSourceSwim(d.StyleName, d.Distance, gender, s.Time, s.BirthYear, s.Laps)));
            }

            swimsMessage = individual.Count == 0
                ? "личных дисциплин длиннее 50 м нет"
                : $"личных дисциплин {individual.Count}, пловцов {swims.Count}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Личные промежуточные loglig {LogligId} не прочитаны", logligId);
            swims.Clear();
            swimsMessage = $"личные промежуточные не прочитаны: {ex.Message}";
        }

        var missingMessage = missing.Count == 0
            ? ""
            : $"; без PDF промежуточных у loglig (пропущены): {string.Join(", ", missing)}";
        return new SplitSource(teams, swims,
            $"источник loglig {logligId}: эстафетных дисциплин {relays.Count}, команд {teams.Count}; "
            + $"{swimsMessage}{missingMessage}");
    }

    /// <summary>
    /// PDF промежуточных дисциплины; null — у loglig его нет (<see cref="LogligSplitPdfMissingException"/>),
    /// номер дисциплины уходит в <paramref name="missing"/>. Пропуск — не «полдоклейки»: этой
    /// дисциплине доклеивать нечего и повтор ничего не даст. Сеть и разбор по-прежнему бросают.
    /// </summary>
    private async Task<MemoryStream?> ParseOrSkipAsync(
        int logligId, int disciplineId, List<int> missing, CancellationToken ct)
    {
        try
        {
            var pdf = await _loglig.FetchDisciplineSplitPdfAsync(logligId, disciplineId, ct);
            return new MemoryStream(pdf);
        }
        catch (LogligSplitPdfMissingException ex)
        {
            _logger.LogWarning("loglig {LogligId}: промежуточных дисциплины {DisciplineId} нет — пропущена",
                logligId, ex.DisciplineId);
            missing.Add(disciplineId);
            return null;
        }
    }
}

using System.Text.Json;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers;

namespace Swimm.Parsing;

/// <summary>
/// IResultSourceProvider над PDF-парсерами протоколов (IsrOrg и др.).
/// Только парсит и сериализует в формат импорта — в БД не пишет (контракт провайдеров).
/// </summary>
public class PdfResultSourceProvider : IResultSourceProvider
{
    private readonly IReadOnlyDictionary<string, IFormatParser> _parsers;

    public PdfResultSourceProvider(IEnumerable<IFormatParser> parsers)
    {
        _parsers = parsers.ToDictionary(p => p.FormatName, StringComparer.OrdinalIgnoreCase);
    }

    public string SourceName => "pdf";

    public IReadOnlyList<string> AvailableFormats => _parsers.Keys.ToList();

    public Task<ParsedCompetition> ParseAsync(ResultSourceRequest request, CancellationToken ct = default)
    {
        if (!_parsers.TryGetValue(request.Format, out var parser))
            throw new InvalidOperationException(
                $"Unknown format '{request.Format}'. Available: {string.Join(", ", _parsers.Keys)}");

        var parseRequest = new ParseRequest(
            request.PrimaryStream,
            request.PrimaryFileName,
            request.SecondaryStream,
            request.SecondaryFileName,
            request.IsAward,
            request.PoolType,
            request.ExtraFiles?.Select(f => (f.Stream, f.FileName)).ToList());

        // Парсеры синхронные (CPU-bound); вызывающий код гоняет их через фоновую
        // очередь импорта, поэтому здесь без Task.Run.
        var results = parser.Parse(parseRequest).ToList();
        var debugLog = parser.GetDebugLog();

        // JsonPropertyName на Models.Result даёт snake_case — ровно тот массив,
        // который принимает IImportService.ImportAsync (легаси JSON-контракт).
        var json = JsonSerializer.Serialize(results);

        var summaries = results
            .GroupBy(r => (r.Competition, r.Date))
            .Select(g => new ParsedCompetitionSummary(g.Key.Competition, g.Key.Date, g.Count()))
            .OrderBy(s => s.Date, StringComparer.Ordinal)
            .ToList();

        var warnings = (debugLog ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Contains("warn", StringComparison.OrdinalIgnoreCase)
                        || l.Contains("skip", StringComparison.OrdinalIgnoreCase)
                        || l.Contains("fail", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(new ParsedCompetition
        {
            Format = parser.FormatName,
            ResultsJson = json,
            ResultCount = results.Count,
            Competitions = summaries,
            Warnings = warnings,
            DebugLog = debugLog
        });
    }
}

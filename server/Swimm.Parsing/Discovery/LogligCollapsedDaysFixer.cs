using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Parsing.Discovery;

/// <inheritdoc cref="ICollapsedDaysFixer"/>
public sealed class LogligCollapsedDaysFixer(
    IDisciplineDateSource dates, ILogger<LogligCollapsedDaysFixer> logger) : ICollapsedDaysFixer
{
    public async Task<ParsedCompetition> FixAsync(int logligId, ParsedCompetition parsed, CancellationToken ct = default)
    {
        // Протокол уже разных дат — сетка не нужна, лишний запрос к loglig не делаем.
        if (parsed.Competitions.Select(c => c.Date).Distinct().Count() != 1) return parsed;

        try
        {
            var outcome = CollapsedDaysRewriter.Rewrite(parsed, await dates.FetchAsync(logligId, ct));
            logger.LogInformation("loglig {LogligId}: {Message}", logligId, outcome.Message);
            // Молчим, если делить нечего (обычный однодневный старт) — иначе предупреждение у каждого.
            return outcome.Warn
                ? outcome.Parsed with { Warnings = outcome.Parsed.Warnings.Append(outcome.Message).ToList() }
                : outcome.Parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "loglig {LogligId}: даты дисциплин не прочитаны", logligId);
            return parsed with
            {
                Warnings = parsed.Warnings.Append($"даты дисциплин не прочитаны: {ex.Message}").ToList(),
            };
        }
    }
}

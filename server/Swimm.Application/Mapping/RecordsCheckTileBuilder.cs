using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Плитка «Рекорды · источники» в сводке /Admin/Jobs (16.11.2026): когда последний раз
/// проверяли справочник рекордов, что ждёт Apply и что упало. Рядом с ней — кнопка «Проверить
/// сейчас» (тот же <c>POST /api/admin/records/check-all</c>, что на /Admin/Import).
///
/// Только источники с провайдером (<see cref="RecordSources.Order"/>): прогон по странам в
/// проверку не входит, и его давность не должна желтить плитку, которую кнопка не исправит.
/// Чистая функция — свежесть и время приходят снаружи.
/// </summary>
public static class RecordsCheckTileBuilder
{
    public static RecordsCheckTile Build(IReadOnlyList<RecordSourceFreshnessDto> freshness, DateTime now)
    {
        var sources = freshness
            .Where(f => RecordSources.Order.Contains(f.Source, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // «Проверено» — последняя УСПЕШНАЯ проверка: сбой дату не сдвигает (§2 плана свежести).
        var lastChecked = sources.Where(f => f.CheckedAt != null).Max(f => f.CheckedAt);
        var pending = sources.Where(f => f.PendingAdded + f.PendingChanged > 0).ToList();
        var failed = sources.Where(f => f.LastOutcome == RecordSourceCheckOutcomes.Failed).ToList();
        var stale = sources.Any(f => f.NeedsCheck);

        var parts = new List<string>();
        if (pending.Count > 0)
            parts.Add("ждут Apply: " + string.Join(", ",
                pending.Select(f => $"{f.Source} +{f.PendingAdded} ~{f.PendingChanged}")));
        if (failed.Count > 0)
            parts.Add("сбой: " + string.Join(", ", failed.Select(f => f.Source)));
        if (parts.Count == 0)
            parts.Add(lastChecked is null ? "ни разу не проверяли" : "изменений нет");

        var tone = failed.Count > 0 ? "danger"
            : pending.Count > 0 || stale ? "warning"
            : "ok";

        return new RecordsCheckTile(
            Value: lastChecked is { } at ? JobTimeText.Ago(at, now) : "не было",
            Sub: string.Join(" · ", parts),
            Tone: tone,
            LastCheckedAt: lastChecked);
    }
}

/// <summary>Что рисует плитка: давность проверки, строка изменений/сбоев и тон рамки.</summary>
public sealed record RecordsCheckTile(string Value, string Sub, string Tone, DateTime? LastCheckedAt);

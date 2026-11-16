using System.Text.Json;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Подробности прогона фоновой задачи — JSON в <c>Sys_JobRunLog.Details</c> (рядом с итогом
/// одной строкой из <see cref="JobRunSummaries"/>). Сейчас подробности пишет только discovery:
/// список самих изменений («какие старты новые, какие перенесены…») для попапа на /Admin/Jobs.
/// Прогон без изменений подробностей не пишет — у него в журнале нечего открывать.
/// </summary>
public static class JobRunDetails
{
    /// <summary>
    /// Больше стольких изменений за прогон не храним (первый забор пустой базы видит весь сезон —
    /// это ~70 строк; потолок — от сбоя, чтобы строка журнала не разрослась).
    /// </summary>
    public const int MaxChanges = 300;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>JSON изменений прогона discovery; null — изменений нет.</summary>
    public static string? Discovery(DiscoverySyncResult result)
    {
        if (result.Changes.Count == 0) return null;
        var payload = new DiscoveryRunDetails(
            result.Changes.Take(MaxChanges).ToList(),
            Truncated: result.Changes.Count > MaxChanges ? result.Changes.Count : null);
        return JsonSerializer.Serialize(payload, Json);
    }

    /// <summary>
    /// Разобрать подробности прогона discovery. null — подробностей нет, они пустые или не
    /// разбираются (строку журнала писала старая версия или другая задача) — тогда дата прогона
    /// просто не кликается.
    /// </summary>
    public static DiscoveryRunDetails? ParseDiscovery(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<DiscoveryRunDetails>(details, Json);
            return parsed?.Changes is { Count: > 0 } ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Подробности прогона discovery. <paramref name="Truncated"/> — сколько изменений было на
/// самом деле, если список обрезан до <see cref="JobRunDetails.MaxChanges"/>; иначе null.
/// </summary>
public sealed record DiscoveryRunDetails(IReadOnlyList<DiscoveryChange> Changes, int? Truncated = null);

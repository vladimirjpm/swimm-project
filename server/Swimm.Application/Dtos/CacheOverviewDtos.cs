using System.Globalization;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;

namespace Swimm.Application.Dtos;

/// <summary>
/// Сводка серверного кэша для админки — верх /Admin/Cache и блок «Кэш» дашборда
/// (<c>GET /api/admin/cache/summary</c>): сколько записей и сколько они весят (ответы API и
/// записи данных порознь), сколько записей без меток данных, последний общий сброс и последние
/// сбросы.
///
/// Живая, а не в двухминутной сводке дашборда (<see cref="DashboardStatusSummary"/>): после кнопки
/// сброса число должно поменяться сразу. Память процесса — для контекста: кэш — её часть, и
/// объекты в памяти занимают больше своего JSON.
/// </summary>
public sealed record CacheOverview(
    int Entries,
    long Bytes,
    int ApiEntries,
    long ApiBytes,
    int DataEntries,
    long DataBytes,
    int Untagged,
    DateTimeOffset Since,
    CacheInvalidationEvent? LastFullReset,
    IReadOnlyList<CacheInvalidationEvent> RecentResets,
    long ProcessBytes,
    long ManagedHeapBytes)
{
    /// <summary>Сколько последних сбросов в сводке; весь журнал — на /Admin/Cache.</summary>
    public const int RecentCount = 5;

    public static CacheOverview From(
        IReadOnlyList<CacheEntryInfo> entries, CacheJournal journal, long processBytes, long managedHeapBytes)
    {
        var api = entries.Where(e => IsApiResponse(e.Key)).ToList();
        var data = entries.Where(e => !IsApiResponse(e.Key)).ToList();

        return new CacheOverview(
            entries.Count,
            SumBytes(entries),
            api.Count,
            SumBytes(api),
            data.Count,
            SumBytes(data),
            entries.Count(IsUntagged),
            journal.Since,
            journal.LastFullReset,
            journal.Events.Take(RecentCount).ToList(),
            processBytes,
            managedHeapBytes);
    }

    /// <summary>Готовый ответ API (<c>CachedJson</c>, ключ <c>http:…</c>), а не запись данных репозитория.</summary>
    public static bool IsApiResponse(string key) => key.StartsWith("http:", StringComparison.Ordinal);

    /// <summary>
    /// Запись без меток данных и без объявления «не из базы» (<see cref="CacheTags.NotFromDb"/>): её
    /// сбросит только общий сброс, запись в базу — нет (сторож К5).
    /// </summary>
    public static bool IsUntagged(CacheEntryInfo entry) =>
        !entry.Tags.Any(t => CacheTags.IsData(t) || t == CacheTags.NotFromDb);

    /// <summary>«812 Б», «41 КБ», «1,8 МБ» — как читает человек.</summary>
    public static string Human(long bytes) =>
        bytes < 1024 ? $"{bytes} Б"
        : bytes < 1024 * 1024 ? $"{Math.Round(bytes / 1024.0):0} КБ"
        // Запятая руками, а не культурой ru-RU: на сервере без ICU культуры может не быть.
        : (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " МБ";

    // Не измеренные (null) — в сумме нулём: размер известен у всех записей, кроме редкой, чей JSON не сложился.
    private static long SumBytes(IEnumerable<CacheEntryInfo> entries) => entries.Sum(e => e.SizeBytes ?? 0);
}

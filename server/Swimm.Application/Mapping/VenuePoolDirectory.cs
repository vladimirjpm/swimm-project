using Swimm.Application.Constants;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Справочник бассейнов в памяти: нормализованное написание → запись. Чистый — ни БД, ни сети;
/// его собирает <c>IVenuePoolService.LoadAsync</c> одним запросом, а results-auto (П5) потом
/// резолвит им сколько угодно строк «Входящих» без похода в базу на каждую.
///
/// Правила ответа (Р-3): нет места → NoVenue; нет написания → NotInDirectory; двусмысленное →
/// Ambiguous. По сезону, по соседним стартам и по похожести НЕ угадываем.
/// </summary>
public sealed class VenuePoolDirectory
{
    private readonly IReadOnlyDictionary<string, VenuePoolEntry> _byAlias;

    public static readonly VenuePoolDirectory Empty = new([]);

    /// <param name="aliases">Пары «нормализованное написание → запись». Повтор ключа — первая пара
    /// (в базе ключ уникален, повтор возможен только в тестах).</param>
    public VenuePoolDirectory(IEnumerable<KeyValuePair<string, VenuePoolEntry>> aliases)
    {
        var map = new Dictionary<string, VenuePoolEntry>(StringComparer.Ordinal);
        foreach (var (key, entry) in aliases) map.TryAdd(key, entry);
        _byAlias = map;
    }

    public int Count => _byAlias.Count;

    public VenuePoolLookup Resolve(string? venue)
    {
        var trimmed = string.IsNullOrWhiteSpace(venue) ? null : venue.Trim();
        var key = VenueNameNormalizer.Normalize(trimmed);
        if (key is null)
            return new VenuePoolLookup(VenuePoolLookupStatus.NoVenue, trimmed, null, null, null);

        if (!_byAlias.TryGetValue(key, out var entry))
            return new VenuePoolLookup(VenuePoolLookupStatus.NotInDirectory, trimmed, key, null, null);

        // Кривая длина у недвусмысленной записи (CHECK в базе такого не пустит) — тоже «не решаем».
        if (entry.IsAmbiguous || !PoolTypes.IsValid(entry.PoolType))
            return new VenuePoolLookup(VenuePoolLookupStatus.Ambiguous, trimmed, key, null, entry);

        return new VenuePoolLookup(VenuePoolLookupStatus.Found, trimmed, key, entry.PoolType, entry);
    }
}

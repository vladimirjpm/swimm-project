using System.Globalization;

namespace Swimm.Application.Dtos;

/// <summary>Итог поиска длины бассейна по месту проведения (П4).</summary>
public enum VenuePoolLookupStatus
{
    /// <summary>Место в справочнике, длина однозначна — можно подставлять.</summary>
    Found,
    /// <summary>У старта место не указано (isr.org.il дописывает его ближе к старту).</summary>
    NoVenue,
    /// <summary>Место указано, но такого написания в справочнике нет — ждёт Влада.</summary>
    NotInDirectory,
    /// <summary>Место в справочнике, но двусмысленное (бывает и 25, и 50) — выбирает человек.</summary>
    Ambiguous,
}

/// <summary>Запись справочника, как её видит поиск (без синонимов).</summary>
public sealed record VenuePoolEntry(
    int Id, string Name, string? PoolType, bool IsAmbiguous, string? Note,
    string ConfirmedBy, DateTime ConfirmedAt);

/// <summary>
/// Ответ справочника бассейнов. Контракт для превью «Затянуть» и для results-auto (П5):
/// <see cref="PoolType"/> не пуст ТОЛЬКО при <see cref="VenuePoolLookupStatus.Found"/>, а
/// <see cref="Describe"/> — готовая фраза причины (источник при удаче, отказ — иначе), которую
/// пишут в журнал и показывают рядом с длиной.
/// </summary>
/// <param name="Status">Нашлось или почему нет.</param>
/// <param name="Venue">Место как пришло (обрезанное); null — места нет.</param>
/// <param name="NormalizedVenue">Ключ поиска (<c>VenueNameNormalizer</c>).</param>
/// <param name="PoolType">«25m»/«50m» — только у Found.</param>
/// <param name="Entry">Запись справочника (Found и Ambiguous).</param>
public sealed record VenuePoolLookup(
    VenuePoolLookupStatus Status,
    string? Venue,
    string? NormalizedVenue,
    string? PoolType,
    VenuePoolEntry? Entry)
{
    public bool IsFound => Status == VenuePoolLookupStatus.Found;

    /// <summary>Фраза для админа и журнала — по-русски, как остальные причины превью.</summary>
    public string Describe() => Status switch
    {
        VenuePoolLookupStatus.Found =>
            $"справочник бассейнов: «{Entry!.Name}» → {PoolType} (подтвердил {Entry.ConfirmedBy}, " +
            $"{Entry.ConfirmedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)})",
        VenuePoolLookupStatus.NoVenue => "место проведения не указано — длину по месту не узнать",
        VenuePoolLookupStatus.NotInDirectory => $"места «{Venue}» нет в справочнике бассейнов",
        VenuePoolLookupStatus.Ambiguous =>
            $"«{Entry!.Name}» в справочнике двусмысленное (бывает и 25, и 50) — выбери вручную",
        _ => Status.ToString(),
    };
}

/// <summary>Строка списка /Admin/VenuePools.</summary>
/// <param name="InboxCount">Сколько строк «Входящих» (плавание) с этим местом — сколько справочник закрывает.</param>
public sealed record VenuePoolRowDto(
    int Id, string Name, string? PoolType, bool IsAmbiguous, string? Note,
    string ConfirmedBy, DateTime ConfirmedAt, IReadOnlyList<string> Aliases, int InboxCount);

/// <summary>Место из «Входящих», которого справочник не знает (кандидат на добавление).</summary>
/// <param name="Venue">Написание как на isr.org.il.</param>
/// <param name="Count">Сколько стартов с этим написанием.</param>
/// <param name="LastDate">Самый поздний старт — свежие места важнее.</param>
public sealed record UnknownVenueDto(string Venue, int Count, DateTime LastDate);

/// <summary>Форма создания/правки места.</summary>
/// <param name="Aliases">Написания, по одному на строку; само <c>Name</c> добавляется автоматически.</param>
public sealed record VenuePoolInputDto(
    string Name, string? PoolType, bool IsAmbiguous, string? Note, string? Aliases);

public sealed record VenuePoolSaveResult(bool Success, int Id, string? Error)
{
    public static VenuePoolSaveResult Ok(int id) => new(true, id, null);
    public static VenuePoolSaveResult Fail(string error) => new(false, 0, error);
}

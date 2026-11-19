using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Откуда взялась предложенная длина бассейна (метка рядом с длиной в превью).</summary>
public static class PoolTypeSources
{
    /// <summary>Из регламента (תקנון) — сильнее справочника: это документ именно этого старта.</summary>
    public const string Regulation = "regulation";
    /// <summary>Из справочника бассейнов по месту проведения (П4).</summary>
    public const string Venue = "venue";
    /// <summary>Честное умолчание пачки «лиги — 25 м» (И-30): ни регламент, ни справочник не ответили.</summary>
    public const string Default = "default";
}

/// <summary>
/// Какую длину бассейна ПРЕДЛОЖИТЬ старту в превью «Затянуть» (П4). Порядок: регламент →
/// справочник бассейнов → как было (одиночное превью — «выбери вручную», пачка — умолчание
/// 25 м у не-чемпионата). Ручной выбор человека сильнее всего — он правит предложение в форме.
///
/// Двусмысленное место (Вингейт) умолчанием НЕ закрывается: там 25 и 50 встречаются почти
/// поровну, и «25 по умолчанию» молча кладёт 50-метровый старт в короткую воду (Р-3).
/// </summary>
public static class PoolTypeChooser
{
    /// <param name="PoolType">Предложенная длина или null — решает человек.</param>
    /// <param name="Source"><see cref="PoolTypeSources"/> или null.</param>
    /// <param name="Reason">Фраза для админа: откуда длина или почему её нет.</param>
    public sealed record Choice(string? PoolType, string? Source, string Reason);

    /// <param name="regulationPool">Длина из регламента («25m»/«50m»), null — не нашлось.</param>
    /// <param name="regulationReason">Цитата регламента — основание, если длина оттуда.</param>
    /// <param name="venue">Ответ справочника; null — не спрашивали.</param>
    public static Choice Choose(string? regulationPool, string? regulationReason, VenuePoolLookup? venue)
    {
        if (regulationPool is not null)
            return new Choice(regulationPool, PoolTypeSources.Regulation, regulationReason ?? "из регламента");

        if (venue is { IsFound: true })
            return new Choice(venue.PoolType, PoolTypeSources.Venue, venue.Describe());

        var why = venue is null ? "" : "; " + venue.Describe();
        return new Choice(null, null,
            "протокол бассейна не указывает, в регламенте не нашлось" + why + " — выбери вручную");
    }

    /// <summary>
    /// Пакетный забор: то же, но без ответа у НЕ-чемпионата с однозначным (или неизвестным)
    /// местом остаётся прежнее умолчание 25 м. Чемпионат и двусмысленное место — null:
    /// импорт пачкой такую строку пропустит, её импортируют по одной, выбрав 25/50.
    /// </summary>
    public static Choice ChooseForBulk(
        string? regulationPool, string? regulationReason, VenuePoolLookup? venue, bool isChampionship)
    {
        var choice = Choose(regulationPool, regulationReason, venue);
        if (choice.PoolType is not null || isChampionship || venue?.Status == VenuePoolLookupStatus.Ambiguous)
            return choice;
        return new Choice("25m", PoolTypeSources.Default,
            "регламент и справочник длину не дали — умолчание лиг 25 м" + (venue is null ? "" : $" ({venue.Describe()})"));
    }
}

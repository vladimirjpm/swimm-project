namespace Swimm.API.Pages.Admin.Shared;

/// <summary>
/// Каталог стран для DDL админки (Import, HubGroups/Edit, …): alpha-3 коды
/// World Aquatics — как в БД и импорте рекордов (см. docs/ARCHITECTURE.md,
/// решение 2026-07-13). Не путать с ISO alpha-2 (IL/US/…) — те нужны только
/// флагам на клиенте. «Избранные» — первая optgroup, Израиль сверху.
/// </summary>
public static class CountryCatalog
{
    /// <summary>Опция DDL: код + подпись.</summary>
    public record CountryOption(string Code, string Label);

    /// <summary>Избранные — первая группа в DDL.</summary>
    public static readonly IReadOnlyList<CountryOption> Favorites =
    [
        new("ISR", "Израиль")
    ];

    /// <summary>Остальные страны по алфавиту.</summary>
    public static readonly IReadOnlyList<CountryOption> Others =
    [
        new("AUS", "Австралия"), new("AUT", "Австрия"), new("AZE", "Азербайджан"),
        new("ARG", "Аргентина"), new("BLR", "Беларусь"), new("BEL", "Бельгия"),
        new("BUL", "Болгария"), new("BRA", "Бразилия"), new("GBR", "Великобритания"),
        new("HUN", "Венгрия"), new("GER", "Германия"), new("GRE", "Греция"),
        new("GEO", "Грузия"), new("DEN", "Дания"), new("IND", "Индия"),
        new("IRL", "Ирландия"), new("ESP", "Испания"), new("ITA", "Италия"),
        new("KAZ", "Казахстан"), new("CAN", "Канада"), new("CYP", "Кипр"),
        new("CHN", "Китай"), new("KOR", "Корея"), new("LAT", "Латвия"),
        new("LTU", "Литва"), new("MEX", "Мексика"), new("MDA", "Молдова"),
        new("NED", "Нидерланды"), new("NZL", "Новая Зеландия"), new("NOR", "Норвегия"),
        new("POL", "Польша"), new("POR", "Португалия"), new("RUS", "Россия"),
        new("ROU", "Румыния"), new("SRB", "Сербия"), new("SVK", "Словакия"),
        new("SLO", "Словения"), new("USA", "США"), new("TUR", "Турция"),
        new("UKR", "Украина"), new("UZB", "Узбекистан"), new("FIN", "Финляндия"),
        new("FRA", "Франция"), new("CRO", "Хорватия"), new("CZE", "Чехия"),
        new("SUI", "Швейцария"), new("SWE", "Швеция"), new("EST", "Эстония"),
        new("RSA", "ЮАР"), new("JPN", "Япония")
    ];
}

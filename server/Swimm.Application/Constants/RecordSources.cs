namespace Swimm.Application.Constants;

/// <summary>
/// Источники справочника рекордов и ПОРЯДОК их применения — одно место на CLI
/// (<c>--records-refresh</c>, <c>--records-check</c>) и сервис проверки свежести.
///
/// Порядок — САМО ПРАВИЛО (решение Влада 24.08, И-13): у <c>country/ISR/open</c> два хозяина,
/// World Aquatics и федерация, и последним обязан писать федеральный — иначе израильские
/// рекорды откатятся к устаревшим значениям WA. Мировые источники (<c>wa-masters</c>,
/// <c>wa-junior</c>) владеют своими осями единолично и стоят рядом с WA по здравому смыслу
/// «сначала мир, потом федерация».
/// </summary>
public static class RecordSources
{
    public const string WorldRecords = "worldrecords";
    public const string WaMasters = "wa-masters";
    public const string WaJunior = "wa-junior";
    public const string IsrOrgAge = "isrorg-age";
    public const string IsrOrgMasters = "isrorg-masters";

    public static readonly IReadOnlyList<string> Order =
        [WorldRecords, WaMasters, WaJunior, IsrOrgAge, IsrOrgMasters];

    /// <summary>
    /// Через сколько проверку считать устаревшей — «пора проверить» в админке (решение Влада
    /// 21.09.2026, records-freshness-plan §7-3). Федерация обновляет PDF редко, World Aquatics —
    /// после крупных стартов; неделя покрывает оба.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);
}

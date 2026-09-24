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

    /// <summary>
    /// Прогон «национальные рекорды по странам» (кнопка «Прогнать все страны», 11.1.2).
    ///
    /// ⚠ Это ТИП ЗАПИСИ В ЖУРНАЛЕ, а не провайдер: своего <c>IRecordSourceProvider</c> у него
    /// нет, в <see cref="Order"/> он не входит и в <c>--records-refresh</c>/<c>--records-check</c>
    /// не участвует. Отдельный тип нужен потому, что страновые NR приезжают СВОИМ прогоном
    /// (час-два, руками, раз в недели), а отчёт мировых рекордов сверяется обычным
    /// <c>worldrecords</c> — и подпись на витрине, показывая дату второго под цифрами первого,
    /// врала бы о свежести (замечание Влада 23.09.2026).
    /// </summary>
    public const string WorldRecordsCountries = "worldrecords-countries";

    public static readonly IReadOnlyList<string> Order =
        [WorldRecords, WaMasters, WaJunior, IsrOrgAge, IsrOrgMasters];

    /// <summary>
    /// Что показывать в свежести: провайдеры плюс прогон по странам. Отдельно от
    /// <see cref="Order"/>, потому что Order — это ещё и порядок ПРИМЕНЕНИЯ источников (И-13),
    /// и лишний ключ в нём означал бы попытку «проверить» то, у чего нет провайдера.
    /// </summary>
    public static readonly IReadOnlyList<string> FreshnessKeys =
        [.. Order, WorldRecordsCountries];

    /// <summary>
    /// Через сколько проверку считать устаревшей — «пора проверить» в админке (решение Влада
    /// 21.09.2026, records-freshness-plan §7-3). Федерация обновляет PDF редко, World Aquatics —
    /// после крупных стартов; неделя покрывает оба.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);
}

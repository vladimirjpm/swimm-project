namespace Swimm.Domain.Entities;

/// <summary>
/// Отладочная подробность, которую можно включить админу: «показывать на витрине то, чего
/// в обычном виде нет» — годы рождения держателей рекордов, промежуточные расчёты и т.п.
///
/// ⚠ Двухуровневый выключатель. Эта таблица хранит ЧАСТНЫЕ опции, а над ними стоит общий
/// тумблер — настройка <c>DebugDetails</c> в /Admin/Settings. Пока общий выключен, ни одна
/// опция не действует, сколько бы галочек тут ни стояло: одним движением гасится всё, и
/// нет риска забыть включённую подробность на боевом сайте.
///
/// Почему таблица, а не настройки: настройки живут в памяти процесса и сбрасываются при
/// рестарте (<c>AdminSettingsService</c>), а состояние опций должно переживать деплой.
/// </summary>
public class DebugOption
{
    /// <summary>Ключ опции — он же первичный ключ: одна строка на опцию.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Галочка самой опции. Действует только при включённом <c>DebugDetails</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Короткое имя для админки (English — правило UI проекта).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Что именно опция показывает и где — человеку, который её впервые видит.</summary>
    public string Description { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }

    /// <summary>Кто последний менял — как в остальных админ-сущностях.</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Ключи отладочных опций. Строки, а не enum: значения уходят в JSON клиенту и лежат в БД,
/// и осмысленный ключ в колонке дороже экономии.
/// </summary>
public static class DebugOptionKeys
{
    /// <summary>
    /// Витрина «ISR Age Records»: под датой рекорда показать год рождения держателя и его
    /// возраст на день рекорда. Ради этого всё и затевалось — видеть, почему запись стоит
    /// в ступени 10, когда пловчихе в сезоне 11 (docs/data-integrity.md §13).
    /// </summary>
    public const string ShowAgeRecordsDetails = "ShowAgeRecordsDetails";

    /// <summary>Все известные ключи — ими сидируется таблица при старте.</summary>
    public static readonly IReadOnlyList<(string Key, string Title, string Description)> All =
    [
        (ShowAgeRecordsDetails,
         "Show age records details",
         "Under the record date on the ISR Age Records card: holder's birth year and the age "
         + "they were on the record date. Birth year is resolved from our protocols (verified "
         + "swim) or by unique name match; a dash means we cannot tell.")
    ];
}

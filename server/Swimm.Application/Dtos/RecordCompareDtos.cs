using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Запрос сравнения двух стран по рекордам (этап 11.3.1).
///
/// Разрез (<paramref name="PoolType"/>, <paramref name="Gender"/>) необязателен — в отличие
/// от рейтинга, где дисциплина обязана быть одна. Здесь наоборот: смысл экрана в том, чтобы
/// пройтись ПО ВСЕМ дисциплинам сразу, а фильтры только сужают охват.
/// </summary>
public sealed record RecordCompareQuery(
    string A,
    string B,
    string? PoolType,
    string? Gender)
{
    /// <summary>Единственный способ собрать запрос — приводит коды и оси к форме справочника.</summary>
    public static RecordCompareQuery Create(string? a, string? b, string? poolType, string? gender)
        => new(
            A: (a ?? "").Trim().ToUpperInvariant(),
            B: (b ?? "").Trim().ToUpperInvariant(),
            PoolType: string.IsNullOrWhiteSpace(poolType) ? null : poolType.Trim().ToLowerInvariant(),
            Gender: string.IsNullOrWhiteSpace(gender) ? null : gender.Trim().ToLowerInvariant());
}

/// <summary>Рекорд одной стороны в дисциплине. Отсутствие стороны выражается <c>null</c>-ом целиком.</summary>
public sealed class RecordCompareSideDto
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("time_ms")]
    public int TimeMs { get; set; }

    [JsonPropertyName("holder_name")]
    public string? HolderName { get; set; }

    /// <summary>
    /// Дата рекорда. Для сравнения это не украшение: рядом стоят отметки разных эпох, и без
    /// даты «медленнее» читается как «слабее», хотя может значить «на двадцать лет старше»
    /// (требование 11.3.3).
    /// </summary>
    [JsonPropertyName("record_date")]
    public string? RecordDate { get; set; }

    /// <summary>Претензия к записи справочника (инвариант И11 — время едет с качеством).</summary>
    [JsonPropertyName("issue_reason")]
    public string? IssueReason { get; set; }
}

/// <summary>Кто быстрее в дисциплине.</summary>
public enum RecordCompareOutcome
{
    /// <summary>Рекорда нет у одной из сторон — сравнивать нечего. **Не «медленнее».**</summary>
    NoData,
    A,
    B,
    Tie,
}

/// <summary>Одна дисциплина: что есть у A, что у B и чем это кончилось.</summary>
public sealed class RecordCompareRowDto
{
    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("pool_type")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("a")]
    public RecordCompareSideDto? A { get; set; }

    [JsonPropertyName("b")]
    public RecordCompareSideDto? B { get; set; }

    /// <summary>
    /// <c>no_data</c> | <c>a</c> | <c>b</c> | <c>tie</c>. Строкой, а не числом:
    /// числовой enum в JSON — та же тихая поломка, что ловил тест прогона по странам.
    /// </summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "no_data";

    /// <summary>
    /// Разница по модулю в миллисекундах; <c>null</c>, если сравнивать нечего. Знак не нужен —
    /// кто быстрее, сказано в <see cref="Outcome"/>, а знак пришлось бы читать вместе с тем,
    /// какая сторона слева.
    /// </summary>
    [JsonPropertyName("delta_ms")]
    public int? DeltaMs { get; set; }
}

/// <summary>
/// Сводный счёт. Побед считается ТОЛЬКО по дисциплинам, где рекорд есть у обеих сторон
/// (<see cref="Compared"/>): иначе страна с половинным покрытием «выигрывала» бы пустотами —
/// прямое требование 11.3.3.
/// </summary>
public sealed class RecordCompareScoreDto
{
    /// <summary>Дисциплин, где есть обе стороны. Знаменатель счёта.</summary>
    [JsonPropertyName("compared")]
    public int Compared { get; set; }

    [JsonPropertyName("a")]
    public int A { get; set; }

    [JsonPropertyName("b")]
    public int B { get; set; }

    [JsonPropertyName("tie")]
    public int Tie { get; set; }

    /// <summary>Дисциплин, где рекорд есть только у A. В победы НЕ идёт.</summary>
    [JsonPropertyName("a_only")]
    public int AOnly { get; set; }

    /// <summary>Дисциплин, где рекорд есть только у B. В победы НЕ идёт.</summary>
    [JsonPropertyName("b_only")]
    public int BOnly { get; set; }
}

/// <summary>
/// Страна, у которой в справочнике есть рекорды (<c>GET /api/records/countries</c>).
///
/// Отдельный публичный список, а не админский <c>/api/admin/records/countries</c>: тот ходит
/// в живой источник за 215 странами, включая те, чьи файлы пусты, и требует прав. Витрине
/// нужно ровно обратное — что есть В БАЗЕ, без сети и без логина.
/// </summary>
public sealed class RecordCountryOptionDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Сколько строк `open` у страны — по нему видно, с кем сравнение осмысленно.</summary>
    [JsonPropertyName("records")]
    public int Records { get; set; }
}

/// <summary>
/// Сколько мировых рекордов в справочнике по категориям — числа на табах <c>/records</c>
/// (<c>GET /api/records/world-counts</c>): всего, по обоим бассейнам и всем полам. Отдельным
/// крошечным ответом, чтобы подпись таба не тянула его данные до открытия таба.
/// </summary>
public sealed class RecordWorldCountsDto
{
    /// <summary>Мировые рекорды open (таб WR).</summary>
    [JsonPropertyName("open")]
    public int Open { get; set; }

    /// <summary>World Junior Records (таб World Junior).</summary>
    [JsonPropertyName("junior")]
    public int Junior { get; set; }

    /// <summary>Мировые рекорды мастерс (таб Masters WR).</summary>
    [JsonPropertyName("masters")]
    public int Masters { get; set; }
}

/// <summary>Ответ <c>GET /api/records/compare</c>.</summary>
public sealed class RecordCompareDto
{
    [JsonPropertyName("a")]
    public string A { get; set; } = string.Empty;

    [JsonPropertyName("b")]
    public string B { get; set; } = string.Empty;

    /// <summary>Разрез запроса; <c>null</c> — без сужения.</summary>
    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("score")]
    public RecordCompareScoreDto Score { get; set; } = new();

    [JsonPropertyName("rows")]
    public IReadOnlyList<RecordCompareRowDto> Rows { get; set; } = [];
}

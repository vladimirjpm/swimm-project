using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Запрос рейтинга рекордов (этап 11.2.1). Дисциплина, бассейн и пол — ЧАСТЬ ЗАПРОСА, а не
/// фильтр «можно не задавать»: рейтинг, в котором смешаны 50 и 100 метров или мужчины с
/// женщинами, не рейтинг, а список строк. Категория зафиксирована <c>open</c> — у других стран
/// её не бывает (решение Влада 29.07.2026), и в запросе её нет вовсе.
/// </summary>
/// <param name="Regions">
/// Сузить до этих стран (alpha-3). Пусто — все страны. Для 11.3 (head-to-head) это же поле
/// с двумя кодами, отдельного запроса не потребуется.
/// </param>
public sealed record RecordRankingQuery(
    string Style,
    string Distance,
    string Gender,
    string PoolType,
    IReadOnlyList<string>? Regions,
    int Limit,
    int Offset)
{
    /// <summary>
    /// Единственный способ собрать запрос: приводит значения к тому виду, в каком оси лежат
    /// в <c>Records</c>. Нормализация живёт ЗДЕСЬ, а не в контроллере, потому что мимо
    /// контроллера в репозиторий ходят тесты и будущий head-to-head (11.3) — разъехавшись,
    /// они искали бы «4x50m» там, где в базе «4X50m», и молча получали бы пустой рейтинг.
    /// </summary>
    public static RecordRankingQuery Create(
        string style, string distance, string gender, string poolType,
        IReadOnlyList<string>? regions, int limit, int offset)
        => new(
            Style: (style ?? "").Trim().ToLowerInvariant(),
            Distance: NormalizeDistance(distance),
            Gender: (gender ?? "").Trim().ToLowerInvariant(),
            PoolType: (poolType ?? "").Trim().ToLowerInvariant(),
            Regions: regions,
            Limit: limit,
            Offset: offset);

    /// <summary>
    /// «4x50M» → «4X50m». У эстафет в справочнике заглавная <c>X</c> («4X100m»), а метры
    /// строчные — форма неочевидная, но это форма БАЗЫ, и подгоняться должен запрос.
    /// </summary>
    public static string NormalizeDistance(string? distance)
        => (distance ?? "").Trim().ToLowerInvariant().Replace("x", "X");
}

/// <summary>Строка рейтинга: страна, её рекорд и отставание от мирового.</summary>
public sealed class RecordRankingRowDto
{
    /// <summary>
    /// Место в рейтинге, спортивное: одинаковое время — одинаковое место, следующее
    /// перепрыгивается (1, 2, 2, 4). Совпадения времён у разных стран — обычное дело,
    /// а не край.
    /// </summary>
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("region_code")]
    public string RegionCode { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    /// <summary>Время в миллисекундах — вычисляемая колонка <c>Records.TimeMs</c>.</summary>
    [JsonPropertyName("time_ms")]
    public int TimeMs { get; set; }

    /// <summary>
    /// Держатель. ⚠ Бывает ПУСТОЙ строкой (132 строки из 13 788 на 16.09.2026): источник
    /// отдаёт время без имени. Это не повод скрыть рекорд — витрина рисует прочерк.
    /// </summary>
    [JsonPropertyName("holder_name")]
    public string? HolderName { get; set; }

    [JsonPropertyName("record_date")]
    public string? RecordDate { get; set; }

    /// <summary>
    /// Открытая претензия к записи (<c>Sys_RecordIssues</c>), как и у <c>/api/records</c>.
    /// ⚠ Кандидаты сторожа сюда НЕ попадают, пока человек не переведёт их в <c>open</c> —
    /// автомат только предлагает (records-quality-plan.md §3).
    /// </summary>
    [JsonPropertyName("issue_reason")]
    public string? IssueReason { get; set; }

    /// <summary>
    /// Отставание от мирового рекорда в миллисекундах. <c>null</c> — мирового эталона для
    /// дисциплины нет (4×50 в длинной воде: у World Aquatics такой рекорд не ведётся).
    /// **Может быть отрицательным** — тогда рекорд страны быстрее мирового, и это находка
    /// сторожа, а не украшение: живой случай — И-22 и И-23 в docs/data-integrity.md.
    /// </summary>
    [JsonPropertyName("behind_world_ms")]
    public int? BehindWorldMs { get; set; }

    /// <summary>Отставание в процентах от мирового, округлённое до сотых. Знак — как у мс.</summary>
    [JsonPropertyName("behind_world_percent")]
    public double? BehindWorldPercent { get; set; }
}

/// <summary>Мировой рекорд дисциплины — эталон, от которого считается отставание.</summary>
public sealed class RecordRankingWorldDto
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("time_ms")]
    public int TimeMs { get; set; }

    [JsonPropertyName("holder_name")]
    public string? HolderName { get; set; }

    [JsonPropertyName("record_date")]
    public string? RecordDate { get; set; }

    /// <summary>
    /// Претензия к САМОМУ мировому рекорду (инвариант И11: показал время — покажи и его
    /// качество). Не теоретическая: живой случай И-20 — World Aquatics отдал «мировой»
    /// 100 вольным ж 50 м за 40.11. Эталон, от которого считается весь рейтинг, обязан
    /// уметь сказать о себе, что он спорный.
    /// </summary>
    [JsonPropertyName("issue_reason")]
    public string? IssueReason { get; set; }
}

/// <summary>
/// Ответ <c>GET /api/records/ranking</c>: страны одной дисциплины по времени.
/// </summary>
public sealed class RecordRankingDto
{
    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("pool_type")]
    public string PoolType { get; set; } = string.Empty;

    /// <summary>Сколько стран в рейтинге всего (до пагинации).</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// Сколько строк дисциплины отброшено из-за неразобранного времени (<c>TimeMs IS NULL</c>).
    /// Отдаём числом, а не молчим: «страны нет в рейтинге» и «её время не разобралось» —
    /// разные вещи, и вторую надо чинить, а не искать глазами.
    /// </summary>
    [JsonPropertyName("unparsed_skipped")]
    public int UnparsedSkipped { get; set; }

    /// <summary>Мировой рекорд дисциплины; <c>null</c> — его нет (см. <c>BehindWorldMs</c>).</summary>
    [JsonPropertyName("world")]
    public RecordRankingWorldDto? World { get; set; }

    [JsonPropertyName("rows")]
    public IReadOnlyList<RecordRankingRowDto> Rows { get; set; } = [];
}

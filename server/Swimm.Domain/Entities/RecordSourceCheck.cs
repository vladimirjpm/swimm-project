namespace Swimm.Domain.Entities;

/// <summary>
/// Одна проверка источника рекордов: когда сходили в источник, что он сказал и применили ли
/// дифф (docs/plans/records-freshness-plan.md, U1).
///
/// Зачем отдельный журнал: <see cref="Record.UpdatedAt"/> меняется, только если запись
/// изменилась, и по нему не отличить «проверили вчера, всё то же» от «не проверяли полгода».
/// Отсюда витрина берёт «checked», а админка — «пора проверить».
///
/// ⚠ Проверка и применение — разные шаги: проверка пишет строку всегда, применение ставит
/// <see cref="AppliedAt"/> в ЕЁ строку (по <see cref="DiffId"/>). Сбой источника пишется
/// <c>failed</c> и дату «проверено» не сдвигает — иначе 504 выглядел бы свежестью.
/// </summary>
public class RecordSourceCheck
{
    public long Id { get; set; }

    /// <summary>Ключ провайдера: <c>worldrecords</c>, <c>wa-masters</c>, <c>wa-junior</c>, <c>isrorg-*</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Когда сходили в источник (UTC).</summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>Исход — <see cref="RecordSourceCheckOutcomes"/>.</summary>
    public string Outcome { get; set; } = RecordSourceCheckOutcomes.Unchanged;

    /// <summary>
    /// SHA-256 РАЗОБРАННЫХ строк в каноническом порядке, а не сырого файла: сырой ответ
    /// пересобирается на каждый запрос (у JSON World Aquatics — <c>generatedOn</c>, у XLSX —
    /// своя дата), и хэш файла менялся бы всегда. null — проверка упала до разбора.
    /// </summary>
    public string? ContentHash { get; set; }

    public int? AddedCount { get; set; }
    public int? ChangedCount { get; set; }
    public int? MissingCount { get; set; }

    /// <summary>Id превью-диффа в памяти (<c>RecordDiffService</c>): по нему Apply находит свою проверку.</summary>
    public string? DiffId { get; set; }

    /// <summary>Текст сбоя у <c>failed</c>: 504, таймаут, формат поменялся.</summary>
    public string? Error { get; set; }

    /// <summary>Когда дифф ЭТОЙ проверки применён; null — не применялся.</summary>
    public DateTime? AppliedAt { get; set; }
}

/// <summary>
/// Исходы проверки. Строки, а не enum: читаются прямо из SQL при разборе «что источник говорил
/// на прошлой неделе».
/// </summary>
public static class RecordSourceCheckOutcomes
{
    /// <summary>Данные источника совпали с базой (или с прошлой проверкой по хэшу).</summary>
    public const string Unchanged = "unchanged";

    /// <summary>Дифф не пустой — ждёт Apply человеком.</summary>
    public const string ChangesFound = "changes-found";

    /// <summary>Источник не ответил или не разобрался. «Проверено» не сдвигает.</summary>
    public const string Failed = "failed";
}

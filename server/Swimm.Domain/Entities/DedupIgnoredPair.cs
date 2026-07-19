using System.ComponentModel.DataAnnotations;

namespace Swimm.Domain.Entities;

/// <summary>Тип сущности игнорируемой пары дедупа.</summary>
public static class DedupEntityType
{
    public const string Swimmer = "swimmer";
    public const string Club = "club";
}

/// <summary>
/// «Развязанная» пара дедупа: админ подтвердил, что это НЕ дубли (например, два
/// пловца-однофамильца с Левенштейн-дистанцией 1) — пара больше не всплывает в
/// кандидатах на склейку. Id хранятся нормализованно (IdA &lt; IdB), без FK — при
/// удалении/merge сущности строка просто перестаёт на что-то влиять.
/// Служебная Sys_-таблица, БЕЗ grant swimm_ro.
/// </summary>
public class DedupIgnoredPair
{
    [Key]
    public int Id { get; set; }

    /// <summary>swimmer | club (<see cref="DedupEntityType"/>).</summary>
    [Required, MaxLength(20)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Меньший Id пары (нормализация: IdA &lt; IdB).</summary>
    public int IdA { get; set; }

    /// <summary>Больший Id пары.</summary>
    public int IdB { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

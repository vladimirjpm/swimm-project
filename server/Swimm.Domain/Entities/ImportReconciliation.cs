using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Сверка импорта: сколько строк обещал файл-протокол и сколько реально оказалось в БД
/// (docs/data-integrity.md, фаза Д1). Пишется в конце каждого импорта — и при совпадении
/// тоже: это журнал «что реально приехало», по нему разбираются инциденты задним числом.
///
/// Ловит класс багов, который иначе виден только глазами: строки уехали в чужой заплыв
/// (И-1), переимпорт создал дубликат соревнования (И-3), дубликаты эстафет из-за смены
/// поля ключа upsert (И-4).
/// </summary>
public class ImportReconciliation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition? Competition { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(500)]
    public string ImportFileName { get; set; } = string.Empty;

    /// <summary>
    /// Заплыв протокола: <c>стиль|дистанция|эстафета?|категория</c>. Пустая строка —
    /// итоговая строка по соревнованию целиком (её и смотрит человек в первую очередь).
    /// </summary>
    [Required, MaxLength(200)]
    public string EventKey { get; set; } = string.Empty;

    /// <summary>Строк в файле.</summary>
    public int ExpectedRows { get; set; }

    /// <summary>Строк в БД после импорта.</summary>
    public int ActualRows { get; set; }

    /// <summary>ok | mismatch. Порог — строгое равенство (решение Р14).</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "ok";
}

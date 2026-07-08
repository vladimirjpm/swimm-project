using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Категория соревнований (напр. «Main Results», «Masters», «Youth Team»).
/// Членство соревнований — через таблицу CategoryCompetitions (M:N).
/// </summary>
public class Category
{
    /// <summary>Ключ категории Masters: членство в ней определяет Competition.IsMasters.</summary>
    public const string MastersKey = "results-masters";

    /// <summary>Ключи категорий, зашитые в код сервера (<see cref="MastersKey"/>, приоритет раздела
    /// в <c>ResultRepository.CategoryFor</c>) и клиента (legacy-редиректы <c>?cat=…</c> в
    /// <c>results-categories.ts</c>). Переименование Key или удаление такой категории сломает эту
    /// логику — админка должна их защищать.</summary>
    public static readonly IReadOnlySet<string> ReservedKeys = new HashSet<string>
    {
        "results-main", "results-masters", "results-youth-team", "results-junior-results"
    };

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Slug-ключ категории, уникальный. Используется в URL и на клиенте.</summary>
    [MaxLength(50)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Человекочитаемое название категории.</summary>
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Короткая метка для бейджей (буква/эмодзи, напр. «J» для Junior). Рендерится как
    /// обычный текст (не HTML) — без риска XSS.</summary>
    [MaxLength(8)]
    public string? Badge { get; set; }

    /// <summary>Порядок отображения (меньше — выше).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Связанные соревнования (через CategoryCompetitions).</summary>
    public ICollection<CategoryCompetition> Competitions { get; set; } = [];
}

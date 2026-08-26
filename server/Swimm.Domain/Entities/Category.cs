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
        "results-main", "results-masters", "results-kids-team", "results-youth-team",
        "results-junior-results"
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

    /// <summary>Название на иврите (ילדים / צעירים / נוער / בוגרים). Заполнено в БД, но публичным
    /// клиентом пока не используется — UI сайта английский (см. корневой CLAUDE.md).
    /// null — перевод не задан.</summary>
    [MaxLength(100)]
    public string? NameHe { get; set; }

    /// <summary>Короткая метка для бейджей (буква/эмодзи, напр. «J» для Junior). Рендерится как
    /// обычный текст (не HTML) — без риска XSS.</summary>
    [MaxLength(8)]
    public string? Badge { get; set; }

    /// <summary>Порядок отображения (меньше — выше).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Возрастная полоса категории: Kids 8–11, Young 11–14, Juniors 14–17, Adults 17+
    /// (решение Влада 2026-08-23). null — категория не про возраст (Maccabiah, Masters:
    /// мастерс определяется признаком заплыва, а не годами).
    ///
    /// Зачем в БД, а не в коде: по этим числам подбираются категории соревнования, когда
    /// возраст назван в его названии цифрами («לגילאי 8-11»), и правило должно меняться в
    /// админке вместе с самой лестницей, а не правкой константы.
    ///
    /// Границы соседних полос СМЫКАЮТСЯ (11, 14, 17 принадлежат обеим). Поэтому диапазон из
    /// названия относят к полосе только при перекрытии в два года и больше — иначе «8-11»
    /// цеплял бы ещё и Young за один общий год (см. CategoryNameMatcher).
    /// </summary>
    public int? MinAge { get; set; }

    /// <summary>Верх возрастной полосы; null у открытой сверху (Adults 17+) и у невозрастных.</summary>
    public int? MaxAge { get; set; }

    /// <summary>Связанные соревнования (через CategoryCompetitions).</summary>
    public ICollection<CategoryCompetition> Competitions { get; set; } = [];
}

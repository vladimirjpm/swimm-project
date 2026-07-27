using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Правило начисления очков пловца (High Point Swimmer): версия, область применения,
/// источник очков, шкала мест. Э0 — только схема, расчёт не реализован (см. Э1–Э2.5).
/// </summary>
public class PointRuleSwimmers
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Идентификатор версии правила, напр. "2026.01".</summary>
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>Дата вступления правила в силу.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Человекочитаемое описание правила.</summary>
    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>Область применения: "all", "masters", "non-masters".</summary>
    [MaxLength(20)]
    public string Scope { get; set; } = string.Empty;

    /// <summary>placement — по шкале мест (Entries); fina — сумма international points.</summary>
    [MaxLength(20)]
    public string PointsSource { get; set; } = "placement";

    /// <summary>Очки по умолчанию для мест за пределами шкалы.</summary>
    public int DefaultPoints { get; set; }

    /// <summary>Максимальное место, за которое начисляются очки (null = без ограничения).</summary>
    public int? MaxScoringPlace { get; set; }

    /// <summary>Считать только N лучших заплывов; null — все.</summary>
    public int? CountBestSwims { get; set; }

    /// <summary>age | age-group | none — как формировать номинации.</summary>
    [MaxLength(20)]
    public string GroupBy { get; set; } = "age";

    public bool SplitByGender { get; set; } = true;

    public bool IncludeRelays { get; set; }

    /// <summary>Минимум заплывов для попадания в зачёт; null — без ограничения.</summary>
    public int? MinSwims { get; set; }

    /// <summary>Очки за УСТАНОВЛЕННЫЙ возрастной рекорд. Замещают очки за место, а не
    /// складываются с ними («כולל הניקוד עבור מדליית הזהב»). null — рекорды не влияют.</summary>
    public int? RecordPoints { get; set; }

    /// <summary>Очки за ПОВТОРЕНИЕ возрастного рекорда (время в точности равно). Тоже
    /// замещают очки за место. null — повторение не выделяется.</summary>
    public int? RecordTiePoints { get; set; }

    /// <summary>Считать только финальные заплывы (регламент бугрим, п.16). Признака типа
    /// заплыва в данных пока НЕТ — до его появления флаг ни на что не влияет.</summary>
    public bool FinalsOnly { get; set; }

    /// <summary>Правило участвует только в явной привязке к соревнованию и никогда —
    /// в подборе по дате/scope. Без этого свежее правило со scope "all" перехватывало бы
    /// все непривязанные соревнования, включая masters.</summary>
    public bool ManualOnly { get; set; }

    /// <summary>Строки шкалы: место → очки.</summary>
    public ICollection<PointRuleSwimmersEntry> Entries { get; set; } = [];
}

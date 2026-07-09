using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Норматив уровня/разряда (замена клиентских normative.js / normative-masters.js).
/// Kind=regular: уровни III_youth…MSMK без возраста; Kind=masters: MSMK…III по возрастным группам.
/// </summary>
public class NormativeStandard
{
    /// <summary>Допустимые значения Kind.</summary>
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string> { "regular", "masters" };

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>regular | masters.</summary>
    [Required, MaxLength(10)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Страна/система нормативов. Текущие сеты — российская система разрядов ("RUS");
    /// появятся израильские и другие — лягут рядом, клиент выбирает какую показывать.
    /// NOT NULL (пустая строка = универсальная) ради уникального индекса.</summary>
    [Required, MaxLength(10)]
    public string Country { get; set; } = string.Empty;

    /// <summary>male | female.</summary>
    [Required, MaxLength(10)]
    public string Gender { get; set; } = string.Empty;

    /// <summary>25m | 50m.</summary>
    [Required, MaxLength(5)]
    public string PoolType { get; set; } = string.Empty;

    /// <summary>Ключ стиля как на клиенте (freestyle/…), не FK — см. Record.Style.</summary>
    [Required, MaxLength(30)]
    public string Style { get; set; } = string.Empty;

    /// <summary>50m, 100m, … (у нормативов эстафет нет).</summary>
    [Required, MaxLength(10)]
    public string Distance { get; set; } = string.Empty;

    /// <summary>Возрастная группа для Kind=masters ("25-29"…); для regular — пустая строка.</summary>
    [Required, MaxLength(10)]
    public string AgeKey { get; set; } = string.Empty;

    /// <summary>Уровень: regular — III_youth/II_youth/I_youth/III_adult/II_adult/I_adult/KMS/MS/MSMK;
    /// masters — MSMK/MS/KMS/I/II/III. Ключи — как в клиентских данных.</summary>
    [Required, MaxLength(15)]
    public string Level { get; set; } = string.Empty;

    /// <summary>Время-порог в исходном строковом формате ("0:59.05", "00:21.82").</summary>
    [Required, MaxLength(15)]
    public string Time { get; set; } = string.Empty;
}

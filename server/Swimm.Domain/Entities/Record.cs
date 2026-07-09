using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Рекорд по плаванию (замена клиентских normative-records/age-records/masters-records.js).
/// Три независимые оси вместо плоского scope — расширяется на любые страны/континенты/категории
/// без изменения схемы:
///   1) территория: RegionType + RegionCode (чей рекорд — мира/Европы/страны);
///   2) категория спортсменов: Category + AgeKey (open/age/junior/masters);
///   3) дисциплина: Gender + PoolType + Style + Distance.
/// API и кэш режутся по региону: records:{region}:{category}.
/// </summary>
public class Record
{
    /// <summary>RegionType: world — RegionCode пуст; continent — код континента (EU/AS/…);
    /// country — ISO-код страны (ISR/USA/…).</summary>
    public static readonly IReadOnlySet<string> RegionTypes = new HashSet<string>
    {
        "world", "continent", "country"
    };

    /// <summary>Категории: open — абсолютный рекорд; age — по одному возрасту ("10"…"18");
    /// junior — юниорские (формат AgeKey определится с данными, напр. "U17");
    /// masters — по возрастным группам ("25-29"…"80-84").</summary>
    public static readonly IReadOnlySet<string> Categories = new HashSet<string>
    {
        "open", "age", "junior", "masters"
    };

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    // ── Ось 1: территория ────────────────────────────────────────────────────

    /// <summary>world | continent | country.</summary>
    [Required, MaxLength(10)]
    public string RegionType { get; set; } = string.Empty;

    /// <summary>Пусто для world; EU/AS/… для continent; ISO-код (ISR, USA, …) для country.
    /// NOT NULL (пустая строка) — ради честного составного уникального индекса.</summary>
    [Required, MaxLength(10)]
    public string RegionCode { get; set; } = string.Empty;

    // ── Ось 2: категория спортсменов ─────────────────────────────────────────

    /// <summary>open | age | junior | masters.</summary>
    [Required, MaxLength(10)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Возрастной ключ: "" для open; "10"…"18" для age; "25-29"…"80-84" для masters;
    /// формат юниоров — по данным (напр. "U17"). NOT NULL ради уникального индекса.</summary>
    [Required, MaxLength(10)]
    public string AgeKey { get; set; } = string.Empty;

    // ── Ось 3: дисциплина ────────────────────────────────────────────────────

    /// <summary>male | female — как в клиентских данных.</summary>
    [Required, MaxLength(10)]
    public string Gender { get; set; } = string.Empty;

    /// <summary>25m | 50m (клиентские ключи 25m_pool/50m_pool нормализуются при сидинге).</summary>
    [Required, MaxLength(5)]
    public string PoolType { get; set; } = string.Empty;

    /// <summary>Ключ стиля как на клиенте: freestyle/backstroke/breaststroke/butterfly/medley/….
    /// Намеренно НЕ FK на Styles — клиент матчится по этим строковым ключам.</summary>
    [Required, MaxLength(30)]
    public string Style { get; set; } = string.Empty;

    /// <summary>50m, 100m, …, 4X50m (эстафеты).</summary>
    [Required, MaxLength(10)]
    public string Distance { get; set; } = string.Empty;

    // ── Собственно рекорд ────────────────────────────────────────────────────

    /// <summary>Время в исходном строковом формате ("21.08", "01:43.45") — клиент
    /// сравнивает/парсит строки, как делал с JS-данными.</summary>
    [Required, MaxLength(15)]
    public string Time { get; set; } = string.Empty;

    /// <summary>Держатель(и): у эстафет — несколько имён через запятую; у age/masters — иврит.</summary>
    [MaxLength(400)]
    public string? HolderName { get; set; }

    /// <summary>Клуб держателя (age/masters-рекорды).</summary>
    [MaxLength(200)]
    public string? Club { get; set; }

    /// <summary>Страна ДЕРЖАТЕЛЯ — не путать с территорией рекорда: мировой рекорд (world)
    /// держит спортсмен конкретной страны (CAY, AUS, …).</summary>
    [MaxLength(10)]
    public string? HolderCountry { get; set; }

    /// <summary>Дата рекорда в исходном строковом виде (обычно dd/MM/yyyy; в легаси-данных
    /// встречаются и M/d/yyyy — нормализация руками через админ-CRUD, не сидером).</summary>
    [MaxLength(20)]
    public string? RecordDate { get; set; }
}

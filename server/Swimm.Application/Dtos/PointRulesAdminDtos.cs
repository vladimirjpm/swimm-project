namespace Swimm.Application.Dtos;

/// <summary>Вид правила начисления очков: клубный зачёт или High Point пловца.
/// Оба живут на одной странице /Admin/PointsRules (два таба).</summary>
public enum PointRuleKind
{
    Clubs,
    Swimmers
}

/// <summary>Строка списка правил: версия + сколько соревнований на неё ссылается.</summary>
public sealed class PointRuleRowDto
{
    public int Id { get; set; }
    public string Version { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public string? Description { get; set; }
    public bool ManualOnly { get; set; }

    /// <summary>Строк в шкале «место → очки».</summary>
    public int EntryCount { get; set; }

    /// <summary>Соревнований с явной привязкой к этому правилу (FK). Пока > 0 — удалять нельзя.</summary>
    public int CompetitionCount { get; set; }
}

/// <summary>Строка шкалы «место → очки».</summary>
public sealed class PointRuleEntryDto
{
    public int Place { get; set; }
    public int Points { get; set; }
}

/// <summary>
/// Поля формы правила — объединение полей клубного правила и правила пловца.
/// Форма одна на оба вида (см. <see cref="PointRuleKind"/>): чужие поля просто не
/// показываются и репозиторием игнорируются.
/// </summary>
public class PointRuleInputDto
{
    // ── общее ──────────────────────────────────────────────────────────────────
    public string Version { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public string? Description { get; set; }
    public string Scope { get; set; } = "all";
    public int DefaultPoints { get; set; }
    public int? MaxScoringPlace { get; set; }
    public bool ManualOnly { get; set; }

    // ── только клубное правило ────────────────────────────────────────────────
    public int RelayMultiplier { get; set; } = 2;

    // ── только правило пловца ─────────────────────────────────────────────────
    public string PointsSource { get; set; } = "placement";
    public int? CountBestSwims { get; set; }
    public string GroupBy { get; set; } = "age";
    public bool SplitByGender { get; set; } = true;
    public bool IncludeRelays { get; set; }
    public int? MinSwims { get; set; }
    public int? RecordPoints { get; set; }
    public int? RecordTiePoints { get; set; }
    public bool FinalsOnly { get; set; }

    /// <summary>Шкала «место → очки». Пустая — правило считает всем DefaultPoints.</summary>
    public IReadOnlyList<PointRuleEntryDto> Entries { get; set; } = [];
}

/// <summary>Полные данные правила для формы Edit.</summary>
public sealed class PointRuleEditDto : PointRuleInputDto
{
    public int Id { get; set; }

    /// <summary>Соревнований с явной привязкой (FK) — блокирует удаление.</summary>
    public int CompetitionCount { get; set; }
}

/// <summary>Результат мутации правила: успех + Id + сообщение об ошибке для формы.</summary>
public sealed record PointRuleSaveResult(bool Success, int Id, string? Error)
{
    public static PointRuleSaveResult Ok(int id) => new(true, id, null);
    public static PointRuleSaveResult Fail(string error) => new(false, 0, error);
}

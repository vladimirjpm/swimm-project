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

    /// <summary>Из них сверено с официальным протоколом.</summary>
    public int VerifiedCount { get; set; }

    /// <summary>Из них принято как верное (официальных очков нет).</summary>
    public int AcceptedCount { get; set; }

    /// <summary>Из них расходятся с официальными (ошибка у организатора, верны наши).</summary>
    public int MismatchCount { get; set; }
}

/// <summary>
/// Строка панели «Соревнования правила» на /Admin/PointsRules: одно логическое соревнование
/// (многодневное событие — одной строкой) + текущая привязка к правилу своего вида.
/// </summary>
public sealed class PointRuleCompetitionRowDto
{
    /// <summary>Id «головы» — первого дня события либо самого соревнования. Смена правила
    /// применяется ко всем дням события (регламент у события один).</summary>
    public int Id { get; set; }

    public int? EventId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Дата первого дня (dd/MM/yyyy).</summary>
    public string Date { get; set; } = "";

    /// <summary>Дней в событии (1 — однодневное).</summary>
    public int DayCount { get; set; }

    public int ResultCount { get; set; }
    public bool IsMasters { get; set; }

    /// <summary>Id соревнования на isr.org.il (у «головы»); null — не сопоставлено с сайтом.</summary>
    public int? OrgCompId { get; set; }

    /// <summary>Когда очки ЭТОГО вида были вручную проверены; null — не проверялось.</summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>Кто проверил (логин админа).</summary>
    public string? VerifiedBy { get; set; }

    /// <summary>Итог проверки: <c>official</c> (сверено с протоколом) | <c>accepted</c>
    /// (официальных нет, принято как верное) | <c>mismatch</c> (официальные есть, но в них
    /// ошибка — верны наши) | null (не проверялось).</summary>
    public string? VerifiedKind { get; set; }

    /// <summary>Текущее правило нужного вида; null — привязки нет (автоподбор по дате).</summary>
    public int? RuleId { get; set; }

    /// <summary>
    /// Объяснение расхождения с официальными очками (только клубный зачёт): тексты по языкам
    /// и табличка расхождения. null — объяснение не написано.
    /// </summary>
    public CompetitionNoteDto? MismatchNote { get; set; }
}

/// <summary>Одна перепривязка из панели: соревнование → правило (null — снять, вернуть автоподбор).</summary>
public sealed record PointRuleReassignItem(int CompetitionId, int? RuleId);

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

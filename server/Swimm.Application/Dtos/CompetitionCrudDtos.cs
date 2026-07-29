namespace Swimm.Application.Dtos;

/// <summary>Страница элементов + метаданные для пагинации.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;
}

/// <summary>Флаги соревнования для ячейки бейджей — общий контракт строки списка и шапки события.</summary>
public interface ICompetitionFlags
{
    bool IsMasters { get; }
    bool IsAward { get; }
    /// <summary>Чемпионат Израиля (ручной флаг соревнования) — бейдж 🏆.</summary>
    bool IsChampionship { get; }
    bool ShowCombineAllResults { get; }
}

/// <summary>Строка списка соревнований (Competitions/Index).</summary>
public sealed class CompetitionListItemDto : ICompetitionFlags
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? SubName { get; set; }
    public string Date { get; set; } = "";
    public string PoolType { get; set; } = "";
    public string Country { get; set; } = "";
    public int? OrgCompId { get; set; }
    public bool IsMasters { get; set; }
    public bool IsAward { get; set; }
    /// <summary>Чемпионат Израиля (ручной флаг).</summary>
    public bool IsChampionship { get; set; }
    public bool ShowCombineAllResults { get; set; }
    public int? EventId { get; set; }
    public string? EventName { get; set; }
    public int? DayNumber { get; set; }
    public int ResultCount { get; set; }
    public int ResultUrlCount { get; set; }
    /// <summary>Привязанное правило клубных очков; null — «Авто» (для панели быстрой правки).</summary>
    public int? PointRuleClubsId { get; set; }
    /// <summary>Привязанное правило High Point; null — «Авто».</summary>
    public int? PointRuleSwimmersId { get; set; }
    /// <summary>Категории соревнования (для бейджей в списке). Прикрепляются после основной выборки.</summary>
    public List<CategoryTagDto> Categories { get; set; } = [];
}

/// <summary>Лёгкий тег категории (ключ + название) для бейджей и чекбоксов формы.</summary>
public sealed class CategoryTagDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Badge { get; set; }
}

/// <summary>
/// Строка списка соревнований: либо одиночное соревнование (<see cref="Single"/>),
/// либо свёрнутое многодневное событие (<see cref="IsEvent"/> — заголовок + <see cref="Days"/>).
/// </summary>
public sealed class CompetitionRowDto : ICompetitionFlags
{
    /// <summary>Одиночное соревнование. null, если строка — заголовок события.</summary>
    public CompetitionListItemDto? Single { get; set; }

    // ── Заголовок многодневного события (иначе всё null/0/пусто) ───────────────
    public int? EventId { get; set; }
    public string? EventName { get; set; }
    /// <summary>Диапазон дат «дд/ММ/гггг – дд/ММ/гггг» (или один день).</summary>
    public string? DateRange { get; set; }
    public string? PoolType { get; set; }
    public string? Country { get; set; }
    public int DayCount { get; set; }
    public int TotalResultCount { get; set; }
    /// <summary>Дни события по возрастанию DayNumber (для раскрытия строки).</summary>
    public List<CompetitionListItemDto> Days { get; set; } = [];

    // Флаги события = флаги, общие для всех дней (обычно у всех дней одинаковые).
    public bool IsMasters { get; set; }
    public bool IsAward { get; set; }
    /// <summary>Чемпионат Израиля (ручной флаг).</summary>
    public bool IsChampionship { get; set; }
    public bool ShowCombineAllResults { get; set; }

    /// <summary>Категории события = общие для всех дней.</summary>
    public List<CategoryTagDto> Categories { get; set; } = [];

    public bool IsEvent => EventId != null;
}

/// <summary>Полные данные соревнования для формы Competitions/Edit.</summary>
public sealed class CompetitionEditDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? SubName { get; set; }
    public string Date { get; set; } = "";
    public string PoolType { get; set; } = "";
    public string Country { get; set; } = "";
    public int? OrgCompId { get; set; }
    public bool IsMasters { get; set; }
    public bool IsAward { get; set; }
    /// <summary>Чемпионат Израиля (ручной флаг).</summary>
    public bool IsChampionship { get; set; }
    public bool ShowCombineAllResults { get; set; }
    // Многодневность управляется импортом — тут только для чтения/отображения.
    public int? EventId { get; set; }
    public string? EventName { get; set; }
    public int? DayNumber { get; set; }
    public int ResultCount { get; set; }
    public List<CompetitionResultUrlDto> ResultUrls { get; set; } = [];
    /// <summary>Ключи категорий, в которых состоит соревнование (для предвыбора чекбоксов).</summary>
    public List<string> CategoryKeys { get; set; } = [];

    /// <summary>Явная привязка к правилу клубных очков; null — правило подбирается по дате и типу (Э4).</summary>
    public int? PointRuleClubsId { get; set; }

    /// <summary>Явная привязка к правилу High Point; null — подбор по дате и типу.</summary>
    public int? PointRuleSwimmersId { get; set; }

    /// <summary>Сколько дней у события (1 — одиночное соревнование). Для операции «проставить всем дням».</summary>
    public int EventDayCount { get; set; } = 1;
}

/// <summary>URL PDF-результатов соревнования (связь по OrgCompId).</summary>
public sealed class CompetitionResultUrlDto
{
    public int Id { get; set; }
    public int OrgCompId { get; set; }
    public string Culture { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>Входные данные создания/обновления соревнования (Id — отдельно, в пути/скрытом поле).</summary>
public sealed class CompetitionInputDto
{
    public string Name { get; set; } = "";
    public string? SubName { get; set; }
    public string Date { get; set; } = "";
    public string PoolType { get; set; } = "";
    public string Country { get; set; } = "";
    public int? OrgCompId { get; set; }
    public bool IsAward { get; set; }
    /// <summary>Чемпионат Израиля (ручной флаг).</summary>
    public bool IsChampionship { get; set; }
    public bool ShowCombineAllResults { get; set; }
    /// <summary>Выбранные категории. IsMasters у соревнования выводится из членства в категории Masters.</summary>
    public List<string> CategoryKeys { get; set; } = [];

    /// <summary>Привязка к правилу клубных очков; null — «Авто» (подбор по дате и типу).</summary>
    public int? PointRuleClubsId { get; set; }

    /// <summary>Привязка к правилу High Point; null — «Авто».</summary>
    public int? PointRuleSwimmersId { get; set; }
}

/// <summary>
/// Быстрая правка соревнования прямо из списка (раскрывающаяся панель строки): общие для
/// всех дней поля. Применяется КО ВСЕМ дням события — регламент у события один, а поля
/// хранятся у каждого дня. Имя/дата/подзаголовок/страна/OrgCompId тут не трогаются: они
/// у дней разные, для них — страница Edit.
/// </summary>
public sealed class CompetitionQuickEditDto
{
    /// <summary>Любой день события или одиночное соревнование — раскрываем до всех дней.</summary>
    public int CompetitionId { get; set; }
    public string PoolType { get; set; } = "";
    public bool IsAward { get; set; }
    public bool IsChampionship { get; set; }
    public bool ShowCombineAllResults { get; set; }
    public List<string> CategoryKeys { get; set; } = [];
    /// <summary>Правило клубных очков; null — «Авто».</summary>
    public int? PointRuleClubsId { get; set; }
    /// <summary>Правило High Point; null — «Авто».</summary>
    public int? PointRuleSwimmersId { get; set; }
}

/// <summary>Результат мутации: успех + Id + сообщение об ошибке валидации (для показа в форме).</summary>
public sealed record CompetitionSaveResult(bool Success, int Id, string? Error)
{
    public static CompetitionSaveResult Ok(int id) => new(true, id, null);
    public static CompetitionSaveResult Fail(string error) => new(false, 0, error);
}

// ── Массовая привязка правил очков (Э4) ─────────────────────────────────────────

/// <summary>Строка превью массовой привязки: соревнование и его текущие правила.</summary>
public sealed class CompetitionRuleRowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? SubName { get; set; }
    public string Date { get; set; } = "";
    public bool IsMasters { get; set; }

    /// <summary>Версия привязанного клубного правила; null — «Авто».</summary>
    public string? ClubsRuleVersion { get; set; }

    /// <summary>Версия привязанного правила High Point; null — «Авто».</summary>
    public string? SwimmersRuleVersion { get; set; }
}

/// <summary>
/// Что проставить выбранным соревнованиям. Два независимых поля: у каждого «менять или нет»
/// отделено от значения, потому что <c>null</c> — легитимное значение («Авто», снять привязку),
/// а не «не трогать».
/// </summary>
public sealed class CompetitionRuleAssignmentDto
{
    public IReadOnlyList<int> CompetitionIds { get; set; } = [];

    public bool SetClubs { get; set; }
    public int? ClubsRuleId { get; set; }

    public bool SetSwimmers { get; set; }
    public int? SwimmersRuleId { get; set; }
}

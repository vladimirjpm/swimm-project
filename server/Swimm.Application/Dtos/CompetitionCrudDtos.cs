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
    public bool ShowCombineAllResults { get; set; }
    public int? EventId { get; set; }
    public string? EventName { get; set; }
    public int? DayNumber { get; set; }
    public int ResultCount { get; set; }
    public int ResultUrlCount { get; set; }
    /// <summary>Категории соревнования (для бейджей в списке). Прикрепляются после основной выборки.</summary>
    public List<CategoryTagDto> Categories { get; set; } = [];
}

/// <summary>Лёгкий тег категории (ключ + название) для бейджей и чекбоксов формы.</summary>
public sealed class CategoryTagDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
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
    public bool ShowCombineAllResults { get; set; }
    // Многодневность управляется импортом — тут только для чтения/отображения.
    public int? EventId { get; set; }
    public string? EventName { get; set; }
    public int? DayNumber { get; set; }
    public int ResultCount { get; set; }
    public List<CompetitionResultUrlDto> ResultUrls { get; set; } = [];
    /// <summary>Ключи категорий, в которых состоит соревнование (для предвыбора чекбоксов).</summary>
    public List<string> CategoryKeys { get; set; } = [];
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
    public bool ShowCombineAllResults { get; set; }
    /// <summary>Выбранные категории. IsMasters у соревнования выводится из членства в категории Masters.</summary>
    public List<string> CategoryKeys { get; set; } = [];
}

/// <summary>Результат мутации: успех + Id + сообщение об ошибке валидации (для показа в форме).</summary>
public sealed record CompetitionSaveResult(bool Success, int Id, string? Error)
{
    public static CompetitionSaveResult Ok(int id) => new(true, id, null);
    public static CompetitionSaveResult Fail(string error) => new(false, 0, error);
}

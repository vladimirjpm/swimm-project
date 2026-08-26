namespace Swimm.Application.Dtos;

/// <summary>Строка списка Admin/Categories: категория + сколько соревнований в ней состоит.</summary>
public sealed class CategoryAdminRowDto
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Название на иврите; в публичном UI пока не используется.</summary>
    public string? NameHe { get; set; }
    public string? Badge { get; set; }
    public int DisplayOrder { get; set; }
    public int CompetitionCount { get; set; }

    /// <summary>Ключ зашит в код сервера/клиента (см. <see cref="Swimm.Domain.Entities.Category.ReservedKeys"/>) —
    /// Key нельзя менять, категорию нельзя удалить.</summary>
    public bool IsReserved { get; set; }

    /// <summary>Возрастная полоса категории (Kids 8–11, Young 11–14, Juniors 14–17, Adults 17+).
    /// null — категория не про возраст (Maccabiah); пустой MaxAge — полоса открыта сверху.
    /// По этим числам категория подбирается сама, когда возраст назван в названии
    /// соревнования цифрами («לגילאי 8-11») — см. CategoryNameMatcher.</summary>
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
}

/// <summary>Полные данные категории для формы Admin/Categories/Edit.</summary>
public sealed class CategoryEditDto
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameHe { get; set; }
    public string? Badge { get; set; }
    public int DisplayOrder { get; set; }
    public int CompetitionCount { get; set; }
    public bool IsReserved { get; set; }

    /// <summary>Возрастная полоса категории (Kids 8–11, Young 11–14, Juniors 14–17, Adults 17+).
    /// null — категория не про возраст (Maccabiah); пустой MaxAge — полоса открыта сверху.
    /// По этим числам категория подбирается сама, когда возраст назван в названии
    /// соревнования цифрами («לגילאי 8-11») — см. CategoryNameMatcher.</summary>
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
}

/// <summary>Входные данные создания/обновления категории.</summary>
public sealed class CategoryInputDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameHe { get; set; }
    public string? Badge { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Возрастная полоса категории (Kids 8–11, Young 11–14, Juniors 14–17, Adults 17+).
    /// Пусто — категория не про возраст (Maccabiah); пустой MaxAge — полоса открыта сверху.</summary>
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
}

/// <summary>Результат мутации категории: успех + Id + сообщение об ошибке для формы.</summary>
public sealed record CategorySaveResult(bool Success, int Id, string? Error)
{
    public static CategorySaveResult Ok(int id) => new(true, id, null);
    public static CategorySaveResult Fail(string error) => new(false, 0, error);
}

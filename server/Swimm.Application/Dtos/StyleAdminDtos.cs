namespace Swimm.Application.Dtos;

/// <summary>Строка списка Admin/Styles: стиль + сколько результатов на него ссылается.</summary>
public sealed class StyleAdminRowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ResultCount { get; set; }

    /// <summary>Посевной стиль (имя зашито в код) — переименовать/удалить нельзя.
    /// См. <see cref="Swimm.Domain.Entities.Style.ReservedNames"/>.</summary>
    public bool IsReserved { get; set; }
}

/// <summary>Полные данные стиля для формы Admin/Styles/Edit.</summary>
public sealed class StyleEditDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ResultCount { get; set; }
    public bool IsReserved { get; set; }
}

/// <summary>Входные данные создания/обновления стиля.</summary>
public sealed class StyleInputDto
{
    public string Name { get; set; } = "";
}

/// <summary>Результат мутации стиля: успех + Id + сообщение об ошибке для формы.</summary>
public sealed record StyleSaveResult(bool Success, int Id, string? Error)
{
    public static StyleSaveResult Ok(int id) => new(true, id, null);
    public static StyleSaveResult Fail(string error) => new(false, 0, error);
}

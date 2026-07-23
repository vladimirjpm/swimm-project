namespace Swimm.Application.Dtos;

/// <summary>Данные клуба для формы Admin/Clubs/Edit (фаза 7.3 op#2 — переименование).</summary>
public sealed class ClubEditDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsPseudo { get; set; }
    public int ResultCount { get; set; }
}

/// <summary>Входные данные правки клуба.</summary>
public sealed class ClubInputDto
{
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsPseudo { get; set; }
}

/// <summary>Результат мутации клуба: успех + сообщение об ошибке для формы.</summary>
public sealed record ClubSaveResult(bool Success, string? Error)
{
    public static ClubSaveResult Ok() => new(true, null);
    public static ClubSaveResult Fail(string error) => new(false, error);
}

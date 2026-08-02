namespace Swimm.Application.Dtos;

/// <summary>Данные клуба для формы Admin/Clubs/Edit (фаза 7.3 op#2 — переименование).</summary>
public sealed class ClubEditDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsPseudo { get; set; }
    public int ResultCount { get; set; }

    /// <summary>Клуб склеен в этот (мягкий merge); null — клуб живой.</summary>
    public int? MergedIntoId { get; set; }

    /// <summary>Имя клуба-приёмника — чтобы админ видел, куда уехал этот клуб.</summary>
    public string? MergedIntoName { get; set; }
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

/// <summary>
/// Результат удаления пустого клуба. <see cref="Name"/> — снимок имени ДО удаления,
/// нужен для записи в аудит (строки в БД уже не будет).
/// </summary>
public sealed record ClubDeleteResult(bool Success, string? Error, string? Name)
{
    public static ClubDeleteResult Ok(string name) => new(true, null, name);
    public static ClubDeleteResult Fail(string error) => new(false, error, null);
}

/// <summary>Удалённый клуб — снимок для аудита и отчёта на странице.</summary>
public sealed record ClubDeletedRow(int Id, string Name);

/// <summary>
/// Результат пакетной чистки пустых клубов. <see cref="Skipped"/> — причины отказа по тем,
/// кто попал в список фильтра, но не прошёл полную проверку (избранное, заявка на клуб…).
/// </summary>
public sealed record ClubBulkDeleteResult(
    IReadOnlyList<ClubDeletedRow> Deleted,
    IReadOnlyList<string> Skipped);

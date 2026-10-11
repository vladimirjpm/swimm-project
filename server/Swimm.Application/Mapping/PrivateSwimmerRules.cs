namespace Swimm.Application.Mapping;

/// <summary>
/// Пловец группы (Р71, решение Влада 29.09.2026): человек без аккаунта и без loglig, которого
/// тренер заводит сам, чтобы ставить на дорожки и писать времена тренировок.
///
/// Главное правило — ВИДИМОСТЬ, а не проверка имени: такой пловец существует только внутри своей
/// группы (<c>Swimmer.PrivateHubGroupId</c>). Публично его нет нигде — ни страницы, ни поиска, ни
/// состава клуба, ни общего ответа страницы группы, — поэтому «Горбенко» или имя с матом, заведённые
/// тренером, никто снаружи не увидит. Фильтр по имени здесь только санитарный (длина, пустота).
/// </summary>
public static class PrivateSwimmerRules
{
    public const string Origin = "local";
    public const int MaxNameLength = 50;
    public const int MinBirthYear = 1920;

    /// <summary>Нормализованный ввод или текст ошибки для тренера.</summary>
    public static (string First, string Last, string? Gender, int BirthYear, string? Error) Normalize(
        string? firstName, string? lastName, string? gender, int? birthYear, int currentYear)
    {
        var first = Clean(firstName);
        var last = Clean(lastName);
        if (first.Length == 0 || last.Length == 0)
            return (first, last, null, 0, "First and last name are required");
        if (first.Length > MaxNameLength || last.Length > MaxNameLength)
            return (first, last, null, 0, $"Name is too long (max {MaxNameLength} characters)");

        var g = string.IsNullOrWhiteSpace(gender) ? null : gender.Trim().ToLowerInvariant();
        if (g is not (null or "male" or "female"))
            return (first, last, null, 0, "Gender must be male or female");

        var year = birthYear ?? 0;
        if (year != 0 && (year < MinBirthYear || year > currentYear))
            return (first, last, g, 0, $"Birth year must be between {MinBirthYear} and {currentYear}");

        return (first, last, g, year, null);
    }

    /// <summary>Пробелы схлопнуты, управляющие символы выброшены.</summary>
    private static string Clean(string? s) =>
        string.Join(' ', new string((s ?? "").Where(c => !char.IsControl(c)).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

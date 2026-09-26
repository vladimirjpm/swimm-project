using System.Text.RegularExpressions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Правила уровней пловцов группы (docs/plans/lane-plans-plan.md) — одно место, без БД:
/// стандартный набор и проверка списка, который тренер сохраняет целиком.
/// </summary>
public static partial class HubGroupLevelRules
{
    public const int MinLevels = 1;
    public const int MaxLevels = 12;
    public const int NameMaxLength = 50;
    public const int DescriptionMaxLength = 300;

    /// <summary>
    /// Стандартный набор при первом открытии (решение Влада 26.09.2026): 4 уровня, первый —
    /// сильнейший. Названия — видимый UI, поэтому по-английски; тренер переименует.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Description)> Defaults =
    [
        ("Advanced", "Strongest swimmers"),
        ("Intermediate", ""),
        ("Developing", ""),
        ("Beginner", "New to structured training"),
    ];

    /// <summary>Уровень после проверки: ранг = место в списке (1..N).</summary>
    public sealed record NormalizedLevel(int? Id, int Rank, string Name, string? Description, string? Color);

    /// <summary>
    /// Проверить и нормализовать список уровней. Ошибка — текст для тренера (видимый UI,
    /// по-английски); иначе уровни с рангами 1..N по порядку.
    /// </summary>
    public static (IReadOnlyList<NormalizedLevel>? Levels, string? Error) Normalize(
        IReadOnlyList<HubGroupLevelInputDto>? input)
    {
        var items = input ?? [];
        if (items.Count < MinLevels) return (null, "A group needs at least one level.");
        if (items.Count > MaxLevels) return (null, $"At most {MaxLevels} levels.");

        var ids = items.Where(i => i.Id != null).Select(i => i.Id!.Value).ToList();
        if (ids.Count != ids.Distinct().Count()) return (null, "The same level is listed twice.");

        var result = new List<NormalizedLevel>(items.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            var name = CollapseSpaces(items[i].Name);
            if (name.Length == 0) return (null, $"Level {i + 1} needs a name.");
            if (name.Length > NameMaxLength) return (null, $"Level name is longer than {NameMaxLength} characters.");
            if (!names.Add(name)) return (null, $"Two levels are called “{name}”.");

            var description = items[i].Description?.Trim();
            if (string.IsNullOrEmpty(description)) description = null;
            else if (description.Length > DescriptionMaxLength)
                return (null, $"Description of “{name}” is longer than {DescriptionMaxLength} characters.");

            var color = items[i].Color?.Trim();
            if (string.IsNullOrEmpty(color)) color = null;
            else if (!ColorRx().IsMatch(color)) return (null, $"Color of “{name}” must look like #1e88e5.");
            else color = color.ToLowerInvariant();

            result.Add(new NormalizedLevel(items[i].Id, i + 1, name, description, color));
        }
        return (result, null);
    }

    private static string CollapseSpaces(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : SpacesRx().Replace(s.Trim(), " ");

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRx();
}

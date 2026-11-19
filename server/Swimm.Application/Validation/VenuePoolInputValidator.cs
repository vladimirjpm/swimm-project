using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Validation;

/// <summary>
/// Проверка формы места (/Admin/VenuePools) до похода в базу. Чистая: ответ — либо ошибка,
/// либо готовый список написаний (уникальных по нормализованной форме, имя — первым).
/// Конфликт написания с ДРУГИМ местом проверяет репозиторий (нужна база), но UNIQUE на
/// <c>NormalizedAlias</c> всё равно стоит последним рубежом.
/// </summary>
public static class VenuePoolInputValidator
{
    public const int MaxLength = 300;

    public sealed record Normalized(
        string Name, string? PoolType, bool IsAmbiguous, string? Note,
        IReadOnlyList<(string Alias, string Key)> Aliases);

    public static (Normalized? Value, string? Error) Validate(VenuePoolInputDto input)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) return (null, "Название места обязательно");
        if (name.Length > MaxLength) return (null, $"Название длиннее {MaxLength} символов");
        if (VenueNameNormalizer.Normalize(name) is null)
            return (null, "В названии нет слов, по которым место можно узнать (одно «בריכה» не годится)");

        string? pool = string.IsNullOrWhiteSpace(input.PoolType) ? null : input.PoolType.Trim();
        if (input.IsAmbiguous)
            pool = null; // двусмысленное место длины не имеет — её выбирает человек на каждый старт
        else if (!PoolTypes.IsValid(pool))
            return (null, "Выберите длину 25m или 50m — или отметьте место двусмысленным");

        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        if (note is { Length: > 500 }) return (null, "Пояснение длиннее 500 символов");

        var aliases = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = new[] { name }.Concat((input.Aliases ?? "").Split('\n'));
        foreach (var raw in lines)
        {
            var alias = raw.Trim();
            if (alias.Length == 0) continue;
            if (alias.Length > MaxLength) return (null, $"Написание «{alias[..40]}…» длиннее {MaxLength} символов");
            var key = VenueNameNormalizer.Normalize(alias);
            if (key is null) return (null, $"Написание «{alias}» пустое после нормализации");
            if (seen.Add(key)) aliases.Add((alias, key));
        }

        return (new Normalized(name, pool, input.IsAmbiguous, note, aliases), null);
    }
}

namespace Swimm.Application.Constants;

/// <summary>
/// Коды стран: правило проекта — **alpha-3 в данных, alpha-2 только флагам**
/// (docs/ARCHITECTURE.md, решение 2026-07-13).
///
/// Справочник <c>Countries</c> наполняют три find-or-create: импорт результатов
/// (<c>JsonImportService</c>), страна соревнования (<c>CompetitionAdminRepository</c>) и
/// страна группы (<c>HubGroupCrudCore</c>). Каждый берёт код из внешних данных как есть,
/// поэтому alpha-2 «IL» из старого JSON заводил ВТОРУЮ запись Израиля рядом с «ISR» — и
/// поиск «рекорды моей страны» для 791 пловца возвращал пусто (инцидент 2026-09-02,
/// docs/data-integrity.md §14; до него та же склейка уже делалась миграцией
/// <c>MergeCountryIlIntoIsr</c> 2026-07-13 — то есть однократной чистки не хватает,
/// закрывать надо ВХОД).
///
/// Поэтому все три зовут <see cref="Normalize"/> перед поиском и созданием.
/// </summary>
public static class CountryCodes
{
    /// <summary>Израиль — «своя» страна витрины (рекорды, рейтинги).</summary>
    public const string Israel = "ISR";

    /// <summary>
    /// Известные синонимы «чужой код → наш alpha-3». Список НАМЕРЕННО короткий: сюда
    /// попадает то, что реально пришло в данных, а не вся таблица ISO. Коды World Aquatics
    /// местами расходятся с ISO alpha-3 (GER≠DEU, SUI≠CHE, NED≠NLD), и таблица «на всякий
    /// случай» тихо подменяла бы страну там, где никто не проверял.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IL"] = Israel,
    };

    /// <summary>
    /// Код к виду справочника: обрезка, верхний регистр, известный синоним → alpha-3.
    /// Пустой вход остаётся пустым — «страна не указана» это не Израиль.
    /// </summary>
    public static string Normalize(string? code)
    {
        var trimmed = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Aliases.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    /// <summary>
    /// Похож ли код на alpha-3 (три латинские буквы). Не проверяет, что такая страна
    /// существует, — только форму: по ней импорт решает, кричать ли о новом коде.
    /// </summary>
    public static bool LooksAlpha3(string? code)
    {
        var value = (code ?? string.Empty).Trim();
        return value.Length == 3 && value.All(char.IsAsciiLetter);
    }
}

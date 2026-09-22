using System;
using System.Collections.Generic;
using System.Linq;

namespace Swimm.Application.Mapping;

/// <summary>
/// Пол эстафеты в <c>Results</c> (Э4 плана docs/plans/records-relays-plan.md, 22.09.2026).
///
/// Два РАЗНЫХ значения, которые раньше сливались в одно <c>none</c>:
/// <list type="bullet">
/// <item><c>mixed</c> — смешанная эстафета (в четвёрке и мужчины, и женщины);</item>
/// <item><c>none</c> — пол НЕ ИЗВЕСТЕН (в PDF непонятно; уточняется по loglig).</item>
/// </list>
/// Переводить <c>none</c> в <c>mixed</c> вслепую нельзя (решение Влада). Единственное
/// доказательство, которое мы принимаем без первоисточника, — пол самих участников: если среди
/// ног есть и мужчина, и женщина, эстафета смешанная. Однополый состав при <c>none</c> — НЕ
/// повод ставить male/female: это, скорее всего, ошибка шапки протокола, и её разбирают по loglig.
/// </summary>
public static class RelayGender
{
    /// <summary>
    /// Итоговый пол эстафеты: male/female/mixed из шапки не трогаем; при неизвестном — mixed,
    /// если состав смешанный, иначе прежнее значение.
    /// </summary>
    public static string Resolve(string? eventGender, IEnumerable<string?> memberGenders)
    {
        if (eventGender is "male" or "female" or "mixed") return eventGender;

        var genders = memberGenders.Select(Normalize).Where(g => g != null).ToHashSet();
        return genders.Contains("male") && genders.Contains("female")
            ? "mixed"
            : eventGender ?? string.Empty;
    }

    /// <summary>Пол из карточки пловца: male/female (и старые M/F) → male/female, прочее → null.</summary>
    public static string? Normalize(string? gender) =>
        gender?.Trim().ToLowerInvariant() switch
        {
            "male" or "m" => "male",
            "female" or "f" => "female",
            _ => null,
        };
}

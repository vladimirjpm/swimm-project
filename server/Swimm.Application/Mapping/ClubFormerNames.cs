using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>Прежнее название клуба как лежит в базе (строка <c>ClubNames</c>).</summary>
public sealed record ClubFormerNameRow(
    int Id, string Name, string NameEn, DateTime? UsedFrom, DateTime? UsedTo, string Kind);

/// <summary>
/// Прежние названия клуба на витрине (docs/plans/swimmer-club-change-plan.md §5, Н6–Н8):
/// «Formerly: הפועל עירוני הוד השרון (2024–2026)» в шапке клуба и в SEO-описании, «then: …»
/// в окне истории клубов пловца. Показываются только переименования (<c>former</c>):
/// вариант написания дубля (<c>spelling</c>) — не история клуба, а опечатка.
/// </summary>
public static class ClubFormerNames
{
    /// <summary>Для витрины: только переименования, последние сверху.</summary>
    public static List<ClubFormerNameDto> ToDtos(IEnumerable<ClubFormerNameRow> rows) =>
        rows
            .Where(r => r.Kind == ClubNameKinds.Former)
            .OrderByDescending(r => r.UsedTo ?? DateTime.MinValue)
            .ThenBy(r => r.Id)
            .Select(r => new ClubFormerNameDto
            {
                Name = r.Name,
                NameEn = string.IsNullOrWhiteSpace(r.NameEn) || r.NameEn == r.Name ? null : r.NameEn,
                FromYear = r.UsedFrom?.Year,
                ToYear = r.UsedTo?.Year,
            })
            .ToList();

    /// <summary>Годы под названием: «2024–2026», «2025» (один год), null — стартов не было.</summary>
    public static string? YearsLabel(int? fromYear, int? toYear) => (fromYear, toYear) switch
    {
        (int f, int t) when f == t => f.ToString(),
        (int f, int t) => $"{f}–{t}",
        (int f, null) => f.ToString(),
        (null, int t) => t.ToString(),
        _ => null,
    };

    /// <summary>Название для витрины: иврит по умолчанию, английское — фоллбек.</summary>
    public static string DisplayName(ClubFormerNameDto n) =>
        string.IsNullOrWhiteSpace(n.Name) ? n.NameEn ?? string.Empty : n.Name;

    /// <summary>
    /// «Formerly: A (2024–2026), B.» — хвост SEO-описания клуба, чтобы поиск находил клуб и по
    /// старому названию. Пусто — переименований не было.
    /// </summary>
    public static string FormerlySentence(IReadOnlyList<ClubFormerNameDto> names)
    {
        if (names.Count == 0) return string.Empty;
        var parts = names.Select(n =>
        {
            var years = YearsLabel(n.FromYear, n.ToYear);
            var name = DisplayName(n);
            return years is null ? name : $"{name} ({years})";
        });
        return $"Formerly: {string.Join(", ", parts)}.";
    }
}

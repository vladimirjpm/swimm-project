using System.Globalization;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Порядок дней многодневки — один на всех: по дате (dd/MM/yyyy), затем DayNumber, затем Id.
/// Им пользуются <c>day_ids</c> списка соревнований (ResultRepository) и представитель события
/// в sitemap (SeoRepository) — «первый день» у них обязан совпадать.
/// </summary>
public static class CompetitionDays
{
    public readonly record struct Day(int Id, int? DayNumber, string? Date);

    public static IReadOnlyList<int> OrderedIds(IEnumerable<Day> days) => days
        .OrderBy(d => DateOnly.TryParseExact(d.Date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : DateOnly.MaxValue)
        .ThenBy(d => d.DayNumber ?? int.MaxValue)
        .ThenBy(d => d.Id)
        .Select(d => d.Id)
        .ToList();

    public static int FirstId(IEnumerable<Day> days) => OrderedIds(days)[0];
}

using System.Globalization;
using Swimm.Application.Dtos;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>Строка попапа «что изменилось за прогон discovery» (/Admin/Jobs).</summary>
/// <param name="Dates">Даты старта, какими стали (у пропавшего — какими были).</param>
/// <param name="OldDates">Даты до прогона — только если сменились.</param>
/// <param name="OldName">Имя до прогона — только если сменилось.</param>
/// <param name="Note">Пометка строки: «перенос отменён» и т.п.</param>
/// <param name="AdminUrl">Запись в /Admin/Competitions: правка соревнования, если оно есть в базе, иначе строка «Входящих» поиском.</param>
public sealed record DiscoveryRunChangeLine(
    int OrgCompId,
    string Name,
    string Dates,
    string? OldDates,
    string? OldName,
    string? Note,
    string SiteUrl,
    string AdminUrl,
    string AdminText);

/// <summary>Группа попапа: новые / перенесены / обновлены / пропали.</summary>
public sealed record DiscoveryRunChangeGroup(string Kind, string Title, string Hint, IReadOnlyList<DiscoveryRunChangeLine> Lines);

/// <summary>Попап прогона discovery: группы изменений; <see cref="Truncated"/> — список обрезан, было столько.</summary>
public sealed record DiscoveryRunChangesView(
    long RunId, DateTime StartedAt, IReadOnlyList<DiscoveryRunChangeGroup> Groups, int Total, int? Truncated);

/// <summary>
/// Собирает попап прогона discovery из подробностей журнала (<see cref="JobRunDetails"/>).
/// Чистая функция: какое соревнование уже есть в базе, передаёт вызывающий
/// (<paramref name="competitionIdByOrgCompId"/> — compID → Competitions.Id, как связывает
/// /Admin/Competitions: штамп OrgCompId, иначе матч имя+дата).
/// </summary>
public static class DiscoveryRunChangesBuilder
{
    private static readonly (string Kind, string Title, string Hint)[] GroupOrder =
    [
        (DiscoveryChangeKinds.New, "Новые", "впервые появились в списке isr.org.il"),
        (DiscoveryChangeKinds.Postponed, "Перенесены", "будущий старт сдвинули; входят и в «обновлено»"),
        (DiscoveryChangeKinds.Updated, "Обновлены", "сменились имя или даты без переноса"),
        (DiscoveryChangeKinds.Missing, "Пропали", "будущий старт исчез из списка сайта — отменён?"),
    ];

    public static DiscoveryRunChangesView Build(
        long runId, DateTime startedAt, DiscoveryRunDetails details, IReadOnlyDictionary<int, int> competitionIdByOrgCompId)
    {
        var groups = new List<DiscoveryRunChangeGroup>();
        foreach (var (kind, title, hint) in GroupOrder)
        {
            // Отменённый перенос — тоже «обновлено», с пометкой.
            var lines = details.Changes
                .Where(c => c.Kind == kind || (kind == DiscoveryChangeKinds.Updated && c.Kind == DiscoveryChangeKinds.Restored))
                .OrderBy(c => c.DateStart).ThenBy(c => c.OrgCompId)
                .Select(c => Line(c, competitionIdByOrgCompId))
                .ToList();
            if (lines.Count > 0) groups.Add(new DiscoveryRunChangeGroup(kind, title, hint, lines));
        }
        return new DiscoveryRunChangesView(runId, startedAt, groups, details.Changes.Count, details.Truncated);
    }

    /// <summary>«05.12.2026» или «05–07.12.2026» / «30.11–02.12.2026».</summary>
    public static string FormatDates(DateTime start, DateTime end)
    {
        var inv = CultureInfo.InvariantCulture;
        if (start.Date == end.Date) return start.ToString("dd.MM.yyyy", inv);
        if (start.Year == end.Year && start.Month == end.Month)
            return $"{start.ToString("dd", inv)}–{end.ToString("dd.MM.yyyy", inv)}";
        if (start.Year == end.Year)
            return $"{start.ToString("dd.MM", inv)}–{end.ToString("dd.MM.yyyy", inv)}";
        return $"{start.ToString("dd.MM.yyyy", inv)}–{end.ToString("dd.MM.yyyy", inv)}";
    }

    public static string SiteUrl(int orgCompId) => $"https://isr.org.il/comp.asp?compID={orgCompId}";

    private static DiscoveryRunChangeLine Line(DiscoveryChange c, IReadOnlyDictionary<int, int> competitionIdByOrgCompId)
    {
        var (adminUrl, adminText) = competitionIdByOrgCompId.TryGetValue(c.OrgCompId, out var competitionId)
            ? ($"/Admin/Competitions/Edit?id={competitionId}", $"в базе #{competitionId}")
            // В базе соревнования нет — ведём на его строку «Входящих»: поиск по имени в сезоне
            // старта, все дисциплины (иначе артистическое спрячет дефолтный фильтр).
            : ($"/Admin/Competitions?q={Uri.EscapeDataString(c.Name)}&season={SeasonMath.FederationYearOf(c.DateStart)}&discipline=all",
                "во Входящих");

        return new DiscoveryRunChangeLine(
            c.OrgCompId,
            c.Name,
            FormatDates(c.DateStart, c.DateEnd),
            c.OldDateStart is DateTime os ? FormatDates(os, c.OldDateEnd ?? os) : null,
            c.OldName,
            c.Kind == DiscoveryChangeKinds.Restored ? "перенос отменён — вернули прежнюю дату" : null,
            SiteUrl(c.OrgCompId),
            adminUrl,
            adminText);
    }
}

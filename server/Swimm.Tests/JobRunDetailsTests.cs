using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Подробности прогона discovery в <c>Sys_JobRunLog.Details</c> и попап «что изменилось» на
/// /Admin/Jobs: JSON туда-обратно, пустой прогон не пишет ничего, вид — группы и ссылки.
/// </summary>
public class JobRunDetailsTests
{
    private static readonly DateTime D1 = new(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc);

    private static DiscoverySyncResult Result(params DiscoveryChange[] changes)
    {
        var r = new DiscoverySyncResult();
        r.Changes.AddRange(changes);
        return r;
    }

    [Fact]
    public void Discovery_NoChanges_WritesNothing()
    {
        Assert.Null(JobRunDetails.Discovery(new DiscoverySyncResult { TotalOnSite = 70 }));
    }

    [Fact]
    public void Discovery_RoundTrip_KeepsAllFields()
    {
        var change = new DiscoveryChange(DiscoveryChangeKinds.Postponed, 123, "אליפות \"ישראל\"", D1.AddDays(7), D1.AddDays(8),
            OldName: "старое", OldDateStart: D1, OldDateEnd: D1.AddDays(1));
        var json = JobRunDetails.Discovery(Result(change));

        var parsed = JobRunDetails.ParseDiscovery(json);
        Assert.NotNull(parsed);
        Assert.Equal(change, Assert.Single(parsed.Changes));
        Assert.Null(parsed.Truncated);
    }

    [Fact]
    public void Discovery_TooManyChanges_TruncatesAndRemembersTotal()
    {
        var many = Enumerable.Range(1, JobRunDetails.MaxChanges + 5)
            .Select(i => new DiscoveryChange(DiscoveryChangeKinds.New, i, "c" + i, D1, D1)).ToArray();
        var parsed = JobRunDetails.ParseDiscovery(JobRunDetails.Discovery(Result(many)));

        Assert.NotNull(parsed);
        Assert.Equal(JobRunDetails.MaxChanges, parsed.Changes.Count);
        Assert.Equal(JobRunDetails.MaxChanges + 5, parsed.Truncated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"changes\":[]}")]
    [InlineData("[1,2]")]
    public void ParseDiscovery_NothingToShow_ReturnsNull(string? details)
    {
        Assert.Null(JobRunDetails.ParseDiscovery(details));
    }

    [Fact]
    public void Build_GroupsInOrder_RestoredGoesToUpdatedWithNote()
    {
        var details = new DiscoveryRunDetails(
        [
            new(DiscoveryChangeKinds.Missing, 4, "gone", D1, D1),
            new(DiscoveryChangeKinds.Restored, 3, "back", D1, D1, OldDateStart: D1.AddDays(3), OldDateEnd: D1.AddDays(3)),
            new(DiscoveryChangeKinds.Postponed, 2, "moved", D1.AddDays(7), D1.AddDays(7), OldDateStart: D1, OldDateEnd: D1),
            new(DiscoveryChangeKinds.New, 1, "fresh", D1, D1),
        ]);

        var view = DiscoveryRunChangesBuilder.Build(42, D1, details, new Dictionary<int, int>());

        Assert.Equal(42, view.RunId);
        Assert.Equal(4, view.Total);
        Assert.Equal(
            [DiscoveryChangeKinds.New, DiscoveryChangeKinds.Postponed, DiscoveryChangeKinds.Updated, DiscoveryChangeKinds.Missing],
            view.Groups.Select(g => g.Kind));
        var back = Assert.Single(view.Groups[2].Lines);
        Assert.NotNull(back.Note);
        Assert.Equal("08.12.2026", back.OldDates);
        var moved = Assert.Single(view.Groups[1].Lines);
        Assert.Equal("05.12.2026", moved.OldDates);
        Assert.Equal("12.12.2026", moved.Dates);
    }

    [Fact]
    public void Build_Links_EditWhenInDb_ElseIncomingSearch()
    {
        var details = new DiscoveryRunDetails(
        [
            new(DiscoveryChangeKinds.New, 10, "in db", D1, D1),
            new(DiscoveryChangeKinds.New, 11, "אליפות חורף", D1, D1),
        ]);

        var lines = DiscoveryRunChangesBuilder.Build(1, D1, details, new Dictionary<int, int> { [10] = 555 }).Groups[0].Lines;

        var inDb = lines.Single(l => l.OrgCompId == 10);
        Assert.Equal("https://isr.org.il/comp.asp?compID=10", inDb.SiteUrl);
        Assert.Equal("/Admin/Competitions/Edit?id=555", inDb.AdminUrl);

        var incoming = lines.Single(l => l.OrgCompId == 11);
        // Декабрь 2026 — сезон федерации 2027 (cYear, год окончания).
        Assert.Equal(
            "/Admin/Competitions?q=" + Uri.EscapeDataString("אליפות חורף") + "&season=2027&discipline=all",
            incoming.AdminUrl);
    }

    [Theory]
    [InlineData("2026-12-05", "2026-12-05", "05.12.2026")]
    [InlineData("2026-12-05", "2026-12-07", "05–07.12.2026")]
    [InlineData("2026-11-30", "2026-12-02", "30.11–02.12.2026")]
    [InlineData("2026-12-31", "2027-01-01", "31.12.2026–01.01.2027")]
    public void FormatDates_CompactsSameMonthAndYear(string start, string end, string expected)
    {
        Assert.Equal(expected, DiscoveryRunChangesBuilder.FormatDates(DateTime.Parse(start), DateTime.Parse(end)));
    }
}

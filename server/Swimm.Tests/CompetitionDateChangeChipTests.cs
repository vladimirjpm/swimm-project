using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Чипы «⇄ перенесён с …» и «✕ пропал с сайта» в /Admin/Competitions (решение Влада 22.11.2026):
/// пометки ставит суточный забор «Входящих», список обязан донести их до строки — раньше они
/// были видны только в календаре сайта.
/// </summary>
public class CompetitionDateChangeChipTests
{
    [Fact]
    public async Task SiteRow_CarriesPostponeAndMissingMarks()
    {
        await using var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(nameof(SiteRow_CarriesPostponeAndMissingMarks)).Options);
        var moved = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        var gone = new DateTime(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc);
        db.DiscoveredCompetitions.AddRange(
            Site(1, 501, "Перенесённый", new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc), previous: moved),
            Site(2, 502, "Пропавший", new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc), missing: gone),
            Site(3, 503, "Обычный", new DateTime(2026, 11, 29, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var list = await new CompetitionAdminRepository(db)
            .GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20);
        var bySite = list.Page.Items.ToDictionary(u => u.Site!.OrgCompId, u => u.Site!);

        Assert.Equal(moved, bySite[501].PreviousDateStart);
        Assert.Null(bySite[501].MissingSince);
        Assert.Equal(gone, bySite[502].MissingSince);
        Assert.Null(bySite[503].PreviousDateStart);
        Assert.Null(bySite[503].MissingSince);
    }

    private static DiscoveredCompetition Site(int id, int orgCompId, string name, DateTime start,
        DateTime? previous = null, DateTime? missing = null) => new()
    {
        Id = id, OrgCompId = orgCompId, Name = name, DateStart = start, DateEnd = start,
        Status = "new", PreviousDateStart = previous, PreviousDateEnd = previous, MissingSince = missing,
    };
}

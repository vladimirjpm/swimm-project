using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пометка «у соревнования нет протокола» (PDF на isr.org.il пуст).
///
/// Живой случай: «ליגת הפועל נוער ובוגרים 3» (OrgCompId 4561) — PDF на одну страницу, из
/// которой парсер извлёк 0 строк. Без пометки строка выглядит обычной «новой», и «Затянуть»
/// нажимают снова и снова. Это НЕ ошибка (её стоило бы повторить), а факт «тянуть нечего» —
/// поэтому отдельное поле, а не LastError.
/// </summary>
public class DiscoveryEmptySourceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static DiscoveredCompetition Row() => new()
    {
        OrgCompId = 4561,
        Name = "ליגת הפועל נוער ובוגרים 3",
        DateStart = new DateTime(2026, 3, 1),
        DateEnd = new DateTime(2026, 3, 1),
        Status = DiscoveredCompetitionStatus.New
    };

    [Fact]
    public async Task Mark_And_Unmark()
    {
        await using var db = CreateDb(nameof(Mark_And_Unmark));
        var row = Row();
        db.DiscoveredCompetitions.Add(row);
        await db.SaveChangesAsync();

        var service = new CompetitionDiscoveryService(db, null!, null!);

        Assert.True(await service.SetEmptySourceAsync(row.Id, true, "auto"));
        var marked = await db.DiscoveredCompetitions.SingleAsync();
        Assert.NotNull(marked.EmptySourceAt);
        Assert.Equal("auto", marked.EmptySourceBy);

        // Файл могут выложить позже — пометка обязана сниматься, иначе строка зачёркнута навсегда.
        Assert.True(await service.SetEmptySourceAsync(row.Id, false, "vlad"));
        var cleared = await db.DiscoveredCompetitions.SingleAsync();
        Assert.Null(cleared.EmptySourceAt);
        Assert.Null(cleared.EmptySourceBy);
    }

    [Fact]
    public async Task UnknownRow_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(UnknownRow_ReturnsFalse));
        var service = new CompetitionDiscoveryService(db, null!, null!);

        Assert.False(await service.SetEmptySourceAsync(999, true, "auto"));
    }
}

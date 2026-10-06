using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// И-41: держатель рекорда без единого заплыва — не сирота. Карточки #62871, #62873,
/// #62874, #62876 (Гал Нево, Гал Коэн Груми, Рон Полонски, Алон Мандель) заведены 17–19.09
/// ради латиницы держателя на экранах рекордов; ссылки на них нет, связь со справочником —
/// по имени. Health показывал их сиротами, кнопка «удалить сирот» стёрла бы их молча.
/// </summary>
public class SwimmerOrphanRecordHolderTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<SwimmDbContext> SeedAsync(string name)
    {
        var db = CreateDb(name);
        db.Swimmers.AddRange(
            new Swimmer { Id = 1, FirstName = "גל", LastName = "נבו", BirthYear = 1987, Origin = "isr" },
            // Держатель только в составе эстафеты — и в обратном порядке слов.
            new Swimmer { Id = 2, FirstName = "רון", LastName = "פולונסקי", BirthYear = 2001, Origin = "isr" },
            new Swimmer { Id = 3, FirstName = "סתם", LastName = "יתום", BirthYear = 2010, Origin = "isr" });
        db.Records.AddRange(
            new Swimm.Domain.Entities.Record { HolderName = "גל נבו", Time = "04:03.50" },
            new Swimm.Domain.Entities.Record { HolderName = "דוד גרצ׳יק, פולונסקי  רון, גרג ליצ׳ינסקי", Time = "01:34.50" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task RecordHolders_AreNotOrphans()
    {
        await using var db = await SeedAsync(nameof(RecordHolders_AreNotOrphans));

        var report = await new SwimmerDedupService(db).FindCandidatesAsync();

        Assert.Equal([3], report.Orphans.Select(o => o.Id));
    }

    [Fact]
    public async Task DeleteOrphans_LeavesRecordHolders()
    {
        await using var db = await SeedAsync(nameof(DeleteOrphans_LeavesRecordHolders));

        // Даже если держателя попросили удалить по id — он не сирота, его пропускают.
        var result = await new SwimmerDedupService(db).DeleteOrphansAsync([1, 2, 3]);

        Assert.Equal([3], result.DeletedIds);
        Assert.Equal([1, 2], result.SkippedIds.OrderBy(x => x));
        Assert.Equal(2, await db.Swimmers.CountAsync());
    }
}

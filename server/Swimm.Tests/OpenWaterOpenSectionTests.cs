using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Общая категория («כללי») открытой воды — места внутри пола (Р78, docs/data-integrity.md).
/// PDF соревнования печатает мужскую и женскую дистанции одним блоком с ОДНОЙ общей секцией на
/// оба пола; на Эйлате-2026 עומר שורץ (единственная женщина в общей 1600 м) получала 2-е место
/// за мужчиной, а по loglig и по регламенту она первая.
/// </summary>
public class OpenWaterOpenSectionTests
{
    private static OpenSectionRow Row(long id, string? gender, int? pos, string block = "1600") =>
        new(id, block, gender, pos);

    [Fact]
    public void MixedBlock_PlacesCountedWithinGender()
    {
        // Эйлат-2026, 1600 м: תורגמן (м) 1, שורץ (ж) 2.
        var changes = OpenWaterOpenSectionRanker.Rerank([Row(1, "male", 1), Row(2, "female", 2)]);

        Assert.Equal(new Dictionary<long, int> { [2] = 1 }, changes);
    }

    [Fact]
    public void MixedBlock_FiveK_WomanThirdBecomesFirst_MenKeepOrder()
    {
        // 5 км: אחדות 1, ברוך 2 (м), נגירה 3 (ж).
        var changes = OpenWaterOpenSectionRanker.Rerank(
            [Row(1, "male", 1), Row(2, "male", 2), Row(3, "female", 3)]);

        Assert.Equal(new Dictionary<long, int> { [3] = 1 }, changes);
    }

    [Fact]
    public void Ties_StayShared_AndOrderIsKept()
    {
        var changes = OpenWaterOpenSectionRanker.Rerank(
            [Row(1, "male", 1), Row(2, "female", 2), Row(3, "female", 2), Row(4, "female", 4), Row(5, "male", 5)]);

        Assert.Equal(new Dictionary<long, int> { [2] = 1, [3] = 1, [4] = 3, [5] = 2 }, changes);
    }

    [Fact]
    public void SingleGenderBlock_Untouched()
    {
        // 10 км: в общей секции один мужчина — напечатанное место и так верное.
        Assert.Empty(OpenWaterOpenSectionRanker.Rerank([Row(1, "male", 1), Row(2, "male", 3)]));
    }

    [Fact]
    public void UnplacedAndUnknownGender_Untouched_AndRerunIsNoOp()
    {
        var rows = new List<OpenSectionRow>
        {
            Row(1, "male", 1), Row(2, "female", 2), Row(3, "female", null), Row(4, null, 3),
        };
        var changes = OpenWaterOpenSectionRanker.Rerank(rows);
        Assert.Equal(new Dictionary<long, int> { [2] = 1 }, changes);

        var applied = rows.Select(r => changes.TryGetValue(r.Id, out var p) ? r with { Position = p } : r);
        Assert.Empty(OpenWaterOpenSectionRanker.Rerank(applied));
    }

    [Fact]
    public void BlocksDoNotMix()
    {
        // Одна женщина в общей 1600 и мужчина в общей 5000 — это разные блоки, пересчитывать нечего.
        Assert.Empty(OpenWaterOpenSectionRanker.Rerank(
            [Row(1, "male", 1, "5000"), Row(2, "female", 2, "1600")]));
    }

    // ── Импорт ────────────────────────────────────────────────────────────────

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private const string Competition = "Eilat open water";
    private const string Date = "27/04/2026";

    /// <summary>Строка общей секции — как её отдаёт парсер: пол «none», программа «mix-0».</summary>
    private static object OpenSectionItem(string lastName, int birthYear, int position, string time) => new
    {
        country = "ISR",
        competition = Competition,
        date = Date,
        event_style_name = "Freestyle",
        event_style_len = "1600",
        event_style_gender = "none",
        event_category = "mix-0",
        pool_type = "25m",
        position,
        heat = 3,
        lane = position,
        last_name = lastName,
        first_name = "A",
        birth_year = birthYear,
        club = "Club",
        time
    };

    private static Stream Payload() => new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
    {
        OpenSectionItem("Turgeman", 2003, 1, "0:25:19.14"),
        OpenSectionItem("Schwartz", 2006, 2, "0:27:40.93"),
    })));

    private static async Task<SwimmDbContext> SeedAsync(string name, string? standingKind)
    {
        var db = CreateDb(name);
        db.Competitions.Add(new Competition { Name = Competition, Date = Date, PoolType = "25m", StandingKindOverride = standingKind });
        db.Swimmers.Add(new Swimmer { LastName = "Turgeman", FirstName = "A", BirthYear = 2003, Gender = "male" });
        db.Swimmers.Add(new Swimmer { LastName = "Schwartz", FirstName = "A", BirthYear = 2006, Gender = "female" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Import_OpenWater_OpenSectionRankedWithinGender()
    {
        await using var db = await SeedAsync(nameof(Import_OpenWater_OpenSectionRankedWithinGender), StandingKinds.OpenWater);

        var result = await new JsonImportService(db, new NullCache()).ImportAsync(Payload());

        Assert.Empty(result.ErrorMessages);
        var places = await db.Results.Include(r => r.Swimmer)
            .ToDictionaryAsync(r => r.Swimmer.LastName, r => (r.Gender, r.Position));
        Assert.Equal(("male", (int?)1), places["Turgeman"]);
        Assert.Equal(("female", (int?)1), places["Schwartz"]);
        Assert.Single(await db.Competitions.ToListAsync());
    }

    [Fact]
    public async Task Import_Pool_MixedSectionKeepsPrintedPlaces()
    {
        // Смешанный блок бассейна может быть настоящим общим зачётом — напечатанное остаётся (Р76).
        await using var db = await SeedAsync(nameof(Import_Pool_MixedSectionKeepsPrintedPlaces), null);

        await new JsonImportService(db, new NullCache()).ImportAsync(Payload());

        var schwartz = await db.Results.Include(r => r.Swimmer).SingleAsync(r => r.Swimmer.LastName == "Schwartz");
        Assert.Equal(("female", (int?)2), (schwartz.Gender, schwartz.Position));
    }
}

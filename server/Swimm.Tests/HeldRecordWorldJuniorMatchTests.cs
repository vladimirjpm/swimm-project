using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Второй путь правой стороны карточки «рекорд против мирового» (WJR-план J2): к возрастному
/// рекорду страны (<c>country/age</c>) подбирается World Junior Record, если возраст входит в
/// полосу WJR — женщины 14–17, мужчины 15–18. В отличие от мастерс
/// (<see cref="HeldRecordWorldMatchTests"/>) матч по ПОЛОСЕ, а не точный.
///
/// Держим развилки из плана: в полосе / вне полосы / обе границы у обоих полов / другой пол /
/// другой бассейн / мастерский рекорд WJR не получает / абсолют страны (<c>adults</c>) — тоже.
/// </summary>
public class HeldRecordWorldJuniorMatchTests
{
    private const string HolderHe = "נועה כהן";

    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static Record Rec(
        string regionType, string category, string ageKey, string gender, string poolType,
        string time, string? holder, string style = "freestyle", string distance = "100m") => new()
        {
            RegionType = regionType,
            RegionCode = regionType == "country" ? "ISR" : string.Empty,
            Category = category,
            AgeKey = ageKey,
            Gender = gender,
            PoolType = poolType,
            Style = style,
            Distance = distance,
            Time = time,
            HolderName = holder,
            HolderCountry = regionType == "world" ? "USA" : null,
            RecordDate = "01/01/2025",
        };

    /// <summary>Мировые юниорские обоих полов в 50-метровке, 100 в/с.</summary>
    private static IEnumerable<Record> Wjr() =>
    [
        Rec("world", "junior", "14-17", "female", "50m", "52.70", "Claire Curzan"),
        Rec("world", "junior", "15-18", "male", "50m", "46.86", "David Popovici"),
    ];

    /// <summary>Держатель израильского рекорда и его рекорды; возвращает то, что увидит карточка.</summary>
    private static async Task<IReadOnlyList<HeldRecordRow>> Held(string db, params Record[] own)
    {
        await using var ctx = CreateDb(db);
        ctx.Swimmers.Add(new Swimmer { Id = 1, FirstName = "נועה", LastName = "כהן" });
        ctx.Records.AddRange(Wjr());
        ctx.Records.AddRange(own);
        await ctx.SaveChangesAsync();
        return await new SwimmerPageRepository(ctx, new NullCacheService()).GetRecordsHeldAsync(1);
    }

    [Theory]
    [InlineData("female", "14", "52.70")] // нижняя граница, девушки
    [InlineData("female", "16", "52.70")]
    [InlineData("female", "17", "52.70")] // верхняя граница, девушки
    [InlineData("male", "15", "46.86")]   // нижняя граница, юноши
    [InlineData("male", "18", "46.86")]   // верхняя граница, юноши
    public async Task Age_in_band_gets_world_junior(string gender, string age, string wjr)
    {
        var rows = await Held($"in-{gender}-{age}",
            Rec("country", "age", age, gender, "50m", "58.00", HolderHe));

        var world = Assert.Single(rows).WorldRecord;
        Assert.NotNull(world);
        Assert.Equal(wjr, world!.Time);
        Assert.Equal(WorldRecordKinds.Junior, world.Kind);
        // Полоса едет на витрину: без неё 14-летняя читает WJR как «рекорд 14 лет».
        Assert.Equal(gender == "female" ? "14-17" : "15-18", world.Band);
    }

    [Theory]
    [InlineData("female", "13")] // ниже полосы
    [InlineData("female", "18")] // у девушек полоса кончается на 17
    [InlineData("male", "14")]   // у юношей начинается с 15
    [InlineData("male", "10")]
    [InlineData("male", "adults")] // абсолют страны — не возраст, эталон — не WJR
    public async Task Age_outside_band_stays_empty(string gender, string age)
    {
        var rows = await Held($"out-{gender}-{age}",
            Rec("country", "age", age, gender, "50m", "58.00", HolderHe));

        Assert.Null(Assert.Single(rows).WorldRecord);
    }

    /// <summary>Одно время WJR встаёт против НЕСКОЛЬКИХ ступеней — это и есть матч по полосе.</summary>
    [Fact]
    public async Task One_wjr_serves_several_ages()
    {
        var rows = await Held(nameof(One_wjr_serves_several_ages),
            Rec("country", "age", "15", "female", "50m", "58.00", HolderHe),
            Rec("country", "age", "17", "female", "50m", "56.00", HolderHe));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("52.70", r.WorldRecord?.Time));
    }

    /// <summary>
    /// Бассейн, стиль и дистанция — точно: 25-метровый рекорд против 50-метрового WJR не
    /// ставится, как и WJR соседней дисциплины.
    /// </summary>
    [Theory]
    [InlineData("25m", "freestyle", "100m")] // другой бассейн
    [InlineData("50m", "backstroke", "100m")] // другой стиль
    [InlineData("50m", "freestyle", "200m")]  // другая дистанция
    public async Task Other_discipline_stays_empty(string pool, string style, string distance)
    {
        var rows = await Held($"disc-{pool}-{style}-{distance}",
            Rec("country", "age", "16", "female", pool, "58.00", HolderHe, style, distance));

        Assert.Null(Assert.Single(rows).WorldRecord);
    }

    /// <summary>
    /// Пол — точно: девушке 16 лет мужской WJR не подставляется, хотя его полоса 15–18 её
    /// возраст покрывает.
    /// </summary>
    [Fact]
    public async Task Other_gender_wjr_is_not_used()
    {
        await using var ctx = CreateDb(nameof(Other_gender_wjr_is_not_used));
        ctx.Swimmers.Add(new Swimmer { Id = 1, FirstName = "נועה", LastName = "כהן" });
        ctx.Records.AddRange(
            Rec("world", "junior", "15-18", "male", "50m", "46.86", "David Popovici"),
            Rec("country", "age", "16", "female", "50m", "58.00", HolderHe));
        await ctx.SaveChangesAsync();

        var rows = await new SwimmerPageRepository(ctx, new NullCacheService()).GetRecordsHeldAsync(1);

        Assert.Null(Assert.Single(rows).WorldRecord);
    }

    /// <summary>
    /// Мастерский рекорд WJR не получает: матч по полосе — только для <c>country/age</c>.
    /// И мастерс держит свой вид эталона, без полосы.
    /// </summary>
    [Fact]
    public async Task Masters_record_keeps_masters_world_not_junior()
    {
        var rows = await Held(nameof(Masters_record_keeps_masters_world_not_junior),
            Rec("country", "masters", "25-29", "female", "50m", "58.00", HolderHe),
            Rec("world", "masters", "25-29", "female", "50m", "53.10", "Masters Holder"));

        var world = Assert.Single(rows).WorldRecord;
        Assert.Equal("53.10", world?.Time);
        Assert.Equal(WorldRecordKinds.Masters, world!.Kind);
        Assert.Null(world.Band);
    }

    /// <summary>И11: показанное время WJR несёт своё качество — претензия ищется тем же ключом.</summary>
    [Fact]
    public async Task Wjr_carries_its_own_quality()
    {
        await using var ctx = CreateDb(nameof(Wjr_carries_its_own_quality));
        ctx.Swimmers.Add(new Swimmer { Id = 1, FirstName = "נועה", LastName = "כהן" });
        ctx.Records.AddRange(Wjr());
        ctx.Records.Add(Rec("country", "age", "16", "female", "50m", "58.00", HolderHe));
        ctx.RecordIssues.Add(new RecordIssue
        {
            RegionType = "world", RegionCode = "", Category = "junior", AgeKey = "14-17",
            Gender = "female", PoolType = "50m", Style = "freestyle", Distance = "100m",
            FlaggedTime = "52.70", Reason = RecordIssueReasons.Manual, Status = RecordIssueStatuses.Open,
        });
        await ctx.SaveChangesAsync();

        var rows = await new SwimmerPageRepository(ctx, new NullCacheService()).GetRecordsHeldAsync(1);

        Assert.Equal(RecordIssueReasons.Manual, Assert.Single(rows).WorldRecord?.IssueReason);
    }

    [Theory]
    [InlineData("14-17", "14", true)]
    [InlineData("14-17", "17", true)]
    [InlineData("14-17", "13", false)]
    [InlineData("14-17", "18", false)]
    [InlineData("14-17", "adults", false)]
    [InlineData("14-17", "", false)]
    [InlineData("", "15", false)]
    [InlineData("25-29", "27", true)] // функция общая: полосу читает из ключа, не из константы
    [InlineData("17-14", "15", false)] // перевёрнутая — не полоса
    [InlineData("U17", "15", false)]
    public void Band_covers(string band, string age, bool expected) =>
        Assert.Equal(expected, WorldJuniorBand.Covers(band, age));
}

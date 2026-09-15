using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Сторож правдоподобия при импорте рекордов (docs/data-integrity.md И-20). Источник сам
/// отдаёт мусор: в отчёте World Aquatics за сентябрь 2026 «мировой рекорд» 100 в/с ж 50 м —
/// 40.11 при нашем 51.68, со статусом Approved.
///
/// Тесты стерегут три обещания:
/// <list type="number">
/// <item>ничего не блокируется и не чинится — строка записывается как в источнике;</item>
/// <item>находка уходит в реестр спорных КАНДИДАТОМ, а не открытой претензией: автомат
/// только предлагает, статус ставит человек (records-quality-plan.md §3);</item>
/// <item>решение человека не перетирается повторным импортом того же файла.</item>
/// </list>
/// </summary>
public class RecordImportPlausibilityTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RecordDiffService Diff(SwimmDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()));

    private static Record Rec(string regionType, string regionCode, string category, string pool,
        string style, string distance, string time, string gender = "female") => new()
    {
        RegionType = regionType, RegionCode = regionCode, Category = category, AgeKey = "",
        Gender = gender, PoolType = pool, Style = style, Distance = distance, Time = time,
        HolderName = "Holder", RecordDate = "01/01/2025", UpdatedAt = DateTime.UtcNow
    };

    private static ParsedRecordDto Parsed(string regionType, string regionCode, string pool,
        string style, string distance, string time, string category = "open", string gender = "female") =>
        new(regionType, regionCode, category, "", gender, pool, style, distance, time,
            "Someone", null, null, "15/09/2026");

    // Реальные значения отчёта WR LCM за 2026-09-15 и базы до него.
    private static async Task SeedWorldAsync(SwimmDbContext db)
    {
        db.Records.AddRange(
            Rec("world", "", "open", "50m", "freestyle", "100m", "51.68"),
            Rec("world", "", "open", "50m", "freestyle", "50m", "23.55"),
            Rec("world", "", "open", "50m", "backstroke", "50m", "26.86"),
            Rec("world", "", "open", "25m", "freestyle", "50m", "22.83"));
        await db.SaveChangesAsync();
    }

    private static readonly ParsedRecordDto[] WaSeptember =
    [
        Parsed("world", "", "50m", "freestyle", "100m", "40.11"),   // −22 %: ошибка ввода у источника
        Parsed("world", "", "50m", "freestyle", "50m", "23.19"),    // −1.5 %: живой рекорд 2026
        Parsed("world", "", "50m", "backstroke", "50m", "26.56"),   // −1.1 %: живой рекорд 2026
        Parsed("world", "", "25m", "freestyle", "50m", "22.83"),
    ];

    [Fact]
    public async Task WorldImprovedBeyondThreshold_IsSuspicious_RealImprovementsAreNot()
    {
        using var db = CreateDb(nameof(WorldImprovedBeyondThreshold_IsSuspicious_RealImprovementsAreNot));
        await SeedWorldAsync(db);

        var diff = await Diff(db).BuildDiffAsync("worldrecords", WaSeptember);

        var s = Assert.Single(diff.Suspicious!);
        Assert.Equal(("freestyle", "100m", "40.11"), (s.Style, s.Distance, s.Time));
        Assert.Equal(RecordIssueReasons.ImplausibleImprovement, s.Reason);
        Assert.Contains("51.68", s.Note);
    }

    [Fact]
    public async Task NationalFasterThanWorld_IsSuspicious()
    {
        using var db = CreateDb(nameof(NationalFasterThanWorld_IsSuspicious));
        await SeedWorldAsync(db);

        var diff = await Diff(db).BuildDiffAsync("isrorg-age",
        [
            Parsed("country", "ISR", "25m", "freestyle", "50m", "21.90"),                 // быстрее WR 22.83
            Parsed("country", "ISR", "25m", "freestyle", "50m", "24.46", category: "age"), // честный
        ]);

        var s = Assert.Single(diff.Suspicious!);
        Assert.Equal(("ISR", "open", "21.90"), (s.RegionCode, s.Category, s.Time));
        Assert.Equal(RecordIssueReasons.FasterThanWorldRecord, s.Reason);
        Assert.Contains("22.83", s.Note);
    }

    [Fact]
    public void WorldReference_IsTheBestOfBaseAndDiff_SoNeitherGarbageDirectionFloods()
    {
        // Честный новый WR 22.50 — национальный 22.70 медленнее него, находки нет, хотя он
        // быстрее старого 22.83.
        var fresh = RecordPlausibility.WorldReference(
            [("female", "25m", "freestyle", "50m", "22.83"), ("female", "25m", "freestyle", "50m", "22.50")]);
        Assert.Empty(RecordPlausibility.Check([Entry("country", "22.70")], fresh));

        // Мусорно-МЕДЛЕННЫЙ новый WR 25.00 не делает честный национальный 23.00 «быстрее мирового».
        var slowGarbage = RecordPlausibility.WorldReference(
            [("female", "25m", "freestyle", "50m", "22.83"), ("female", "25m", "freestyle", "50m", "25.00")]);
        Assert.Empty(RecordPlausibility.Check([Entry("country", "23.00")], slowGarbage));
    }

    [Fact]
    public void FirstWorldRecord_AndUnparsableTime_AreNotJudged()
    {
        var reference = RecordPlausibility.WorldReference([]);
        // У нового мирового нет прежнего значения — сравнивать не с чем.
        Assert.Empty(RecordPlausibility.Check([Entry("world", "40.11", oldTime: null)], reference));
        // Неразборное время — другой класс дефекта, не этот сторож.
        Assert.Empty(RecordPlausibility.Check([Entry("world", "DQ", oldTime: "51.68")], reference));
    }

    [Fact]
    public async Task Apply_WritesValueAsInSource_AndFilesCandidateNotOpenIssue()
    {
        using var db = CreateDb(nameof(Apply_WritesValueAsInSource_AndFilesCandidateNotOpenIssue));
        await SeedWorldAsync(db);
        var service = Diff(db);

        var diff = await service.BuildDiffAsync("worldrecords", WaSeptember);
        var result = await service.ApplyAsync(new RecordDiffApplyRequest(diff.DiffId, ApplyAdded: true, ApplyChanged: true));

        Assert.True(result.Success);
        Assert.Equal(1, result.CandidatesCreated);

        // Ошибку источника не чиним: наша копия совпадает с файлом.
        Assert.Equal("40.11", (await db.Records.SingleAsync(r =>
            r.RegionType == "world" && r.PoolType == "50m" && r.Style == "freestyle" && r.Distance == "100m")).Time);

        var issue = await db.RecordIssues.SingleAsync();
        Assert.Equal(RecordIssueStatuses.Candidate, issue.Status);
        Assert.Equal(RecordIssueReasons.ImplausibleImprovement, issue.Reason);
        Assert.Equal("40.11", issue.FlaggedTime);
        Assert.Equal("auto", issue.CreatedBy);
    }

    [Fact]
    public async Task Apply_DoesNotResurrectIssueRejectedByHuman()
    {
        using var db = CreateDb(nameof(Apply_DoesNotResurrectIssueRejectedByHuman));
        await SeedWorldAsync(db);
        // Человек уже разобрал это значение и снял претензию (дистанцию завёл руками без «m» —
        // ключ реестра обязан это пережить).
        db.RecordIssues.Add(new RecordIssue
        {
            RegionType = "world", RegionCode = "", Category = "open", AgeKey = "", Gender = "female",
            PoolType = "50m", Style = "freestyle", Distance = "100", FlaggedTime = "40.11",
            Reason = RecordIssueReasons.Manual, Status = RecordIssueStatuses.Rejected, Note = "разобрались",
            CreatedBy = "admin", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = Diff(db);

        var diff = await service.BuildDiffAsync("worldrecords", WaSeptember);
        var result = await service.ApplyAsync(new RecordDiffApplyRequest(diff.DiffId, true, true));

        Assert.Equal(0, result.CandidatesCreated);
        var issue = await db.RecordIssues.SingleAsync();
        Assert.Equal(RecordIssueStatuses.Rejected, issue.Status);
    }

    [Fact]
    public async Task Apply_WithoutChangedGroup_FilesNothing()
    {
        using var db = CreateDb(nameof(Apply_WithoutChangedGroup_FilesNothing));
        await SeedWorldAsync(db);
        var service = Diff(db);

        var diff = await service.BuildDiffAsync("worldrecords", WaSeptember);
        // Галка «изменившиеся» снята — 40.11 не записан, значит и претензии на него нет.
        var result = await service.ApplyAsync(new RecordDiffApplyRequest(diff.DiffId, ApplyAdded: true, ApplyChanged: false));

        Assert.Equal(0, result.CandidatesCreated);
        Assert.Empty(db.RecordIssues);
    }

    [Theory]
    [InlineData(RecordIssueStatuses.Candidate, null)]
    [InlineData(RecordIssueStatuses.Open, RecordIssueReasons.ImplausibleImprovement)]
    public async Task PublicRecords_ShowMarkOnlyAfterHumanOpensIt(string status, string? expectedReason)
    {
        using var db = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(nameof(PublicRecords_ShowMarkOnlyAfterHumanOpensIt) + status)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        db.Add(Rec("world", "", "open", "50m", "freestyle", "100m", "40.11"));
        db.Add(new RecordIssue
        {
            RegionType = "world", RegionCode = "", Category = "open", AgeKey = "", Gender = "female",
            PoolType = "50m", Style = "freestyle", Distance = "100m", FlaggedTime = "40.11",
            Reason = RecordIssueReasons.ImplausibleImprovement, Status = status, Note = "",
            CreatedBy = "auto", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var row = Assert.Single(await new RecordRepository(db, new NoopCacheService()).GetRecordsAsync("world"));

        Assert.Equal(expectedReason, row.IssueReason);
    }

    private static RecordDiffEntry Entry(string regionType, string newTime, string? oldTime = null) =>
        new(regionType, regionType == "world" ? "" : "ISR", "open", "", "female", "25m", "freestyle", "50m",
            oldTime, null, null, newTime, null, null);

    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }
}

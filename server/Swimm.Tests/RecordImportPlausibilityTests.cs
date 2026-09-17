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
        string style, string distance, string time, string gender = "female", string ageKey = "") => new()
    {
        RegionType = regionType, RegionCode = regionCode, Category = category, AgeKey = ageKey,
        Gender = gender, PoolType = pool, Style = style, Distance = distance, Time = time,
        HolderName = "Holder", RecordDate = "01/01/2025", UpdatedAt = DateTime.UtcNow
    };

    private static ParsedRecordDto Parsed(string regionType, string regionCode, string pool,
        string style, string distance, string time, string category = "open", string gender = "female",
        string ageKey = "") =>
        new(regionType, regionCode, category, ageKey, gender, pool, style, distance, time,
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

    // ── Правило 3: строка стала МЕДЛЕННЕЕ (И-21) ──

    [Fact]
    public async Task SlowerThanStored_IsSuspicious_NewRowIsNot()
    {
        // Живой случай И-21: федерация заменила рекорд 200 брасс ж 70-74 более медленным.
        using var db = CreateDb(nameof(SlowerThanStored_IsSuspicious_NewRowIsNot));
        db.Records.Add(Rec("country", "ISR", "masters", "50m", "breaststroke", "200m", "03:50.05",
            ageKey: "70-74"));
        await db.SaveChangesAsync();

        var diff = await Diff(db).BuildDiffAsync("isrorg-masters",
        [
            Parsed("country", "ISR", "50m", "breaststroke", "200m", "04:34.46", "masters", ageKey: "70-74"),
            // Новой строке сравнивать не с чем — правило её не касается.
            Parsed("country", "ISR", "50m", "breaststroke", "200m", "03:19.66", "masters", ageKey: "50-54"),
        ]);

        var s = Assert.Single(diff.Suspicious!);
        Assert.Equal(("70-74", "04:34.46"), (s.AgeKey, s.Time));
        Assert.Equal(RecordIssueReasons.SlowerThanStored, s.Reason);
        Assert.Contains("03:50.05", s.Note);
        Assert.Contains("+44.41 с", s.Note);
    }

    [Fact]
    public async Task ContestedOpenSlot_RollbackIsIgnored_ButOwnedScopeIsFlagged()
    {
        // country/*/open пишут ДВА источника (И-13): откат World Aquatics — штатная середина
        // цепочки, следующий шаг вернёт федеральное значение. Такие строки правило пропускает,
        // иначе каждый прогон заводил бы десятки кандидатов. Однохозяйные слоты — ловит.
        using var db = CreateDb(nameof(ContestedOpenSlot_RollbackIsIgnored_ButOwnedScopeIsFlagged));
        await SeedWorldAsync(db);
        db.Records.AddRange(
            Rec("country", "ISR", "open", "25m", "freestyle", "50m", "24.46"),
            Rec("country", "ISR", "age", "25m", "freestyle", "50m", "26.10", ageKey: "15"));
        await db.SaveChangesAsync();

        var diff = await Diff(db).BuildDiffAsync("worldrecords",
        [
            Parsed("country", "ISR", "25m", "freestyle", "50m", "24.53"),                       // откат, ждали
            Parsed("country", "ISR", "25m", "freestyle", "50m", "26.40", "age", ageKey: "15"),  // дефект
        ]);

        var s = Assert.Single(diff.Suspicious!);
        Assert.Equal(("age", "26.40"), (s.Category, s.Time));
        Assert.Equal(RecordIssueReasons.SlowerThanStored, s.Reason);
    }

    [Fact]
    public async Task WorldSlower_IsSuspicious_EvenThoughRuleTwoNeverSeesWorldRows()
    {
        using var db = CreateDb(nameof(WorldSlower_IsSuspicious_EvenThoughRuleTwoNeverSeesWorldRows));
        await SeedWorldAsync(db);

        var diff = await Diff(db).BuildDiffAsync("worldrecords",
            [Parsed("world", "", "50m", "freestyle", "100m", "52.10")]);

        var s = Assert.Single(diff.Suspicious!);
        Assert.Equal(RecordIssueReasons.SlowerThanStored, s.Reason);
        Assert.Contains("51.68", s.Note);
    }

    [Fact]
    public async Task SameTimeNewHolder_IsNotSuspicious()
    {
        // Живой случай прогона 2026-09-16: 57.40 → 57.40, сменился только держатель.
        using var db = CreateDb(nameof(SameTimeNewHolder_IsNotSuspicious));
        db.Records.Add(Rec("country", "ISR", "masters", "50m", "freestyle", "100m", "57.40",
            gender: "male", ageKey: "35-39"));
        await db.SaveChangesAsync();

        var diff = await Diff(db).BuildDiffAsync("isrorg-masters",
            [Parsed("country", "ISR", "50m", "freestyle", "100m", "57.40", "masters",
                gender: "male", ageKey: "35-39")]);

        Assert.Equal(1, diff.ChangedCount);
        Assert.Empty(diff.Suspicious!);
    }

    [Fact]
    public void WorldReference_IsTheBestOfBaseAndDiff_SoNeitherGarbageDirectionFloods()
    {
        // Честный новый WR 22.50 — национальный 22.70 медленнее него, находки нет, хотя он
        // быстрее старого 22.83.
        var fresh = RecordPlausibility.WorldReference([World("22.83"), World("22.50")]);
        Assert.Empty(RecordPlausibility.Check([Entry("country", "22.70")], fresh));

        // Мусорно-МЕДЛЕННЫЙ новый WR 25.00 не делает честный национальный 23.00 «быстрее мирового».
        var slowGarbage = RecordPlausibility.WorldReference([World("22.83"), World("25.00")]);
        Assert.Empty(RecordPlausibility.Check([Entry("country", "23.00")], slowGarbage));
    }

    // ── правило 2 для мастерсов: планка не абсолютная, а по полосе (17.09.2026) ──────

    /// <summary>
    /// Суть правки. Мастерский рекорд страны 58.00 в полосе 70-74 медленнее АБСОЛЮТНОГО
    /// мирового (51.68), и старое правило его пропускало. Но мировой рекорд самой полосы —
    /// 01:06.68, и 58.00 быстрее него: так не бывает.
    /// </summary>
    [Fact]
    public void Masters_FasterThanItsOwnBand_IsFound_EvenWhenSlowerThanAbsolute()
    {
        var reference = RecordPlausibility.WorldReference([
            World("51.68"),
            World("01:06.68", category: "masters", ageKey: "70-74"),
        ]);

        var found = Assert.Single(RecordPlausibility.Check([MastersEntry("70-74", "58.00")], reference));

        Assert.Equal(RecordIssueReasons.FasterThanWorldRecord, found.Reason);
        // В обосновании должна стоять ПОЛОСА, иначе человек в реестре не поймёт, с чем сравнивали.
        Assert.Contains("70-74", found.Note);
        Assert.Contains("01:06.68", found.Note);
    }

    /// <summary>Честный мастерский рекорд своей полосы находкой не становится.</summary>
    [Fact]
    public void Masters_SlowerThanItsBand_IsFine()
    {
        var reference = RecordPlausibility.WorldReference([
            World("51.68"),
            World("01:06.68", category: "masters", ageKey: "70-74"),
        ]);

        Assert.Empty(RecordPlausibility.Check([MastersEntry("70-74", "01:10.00")], reference));
    }

    /// <summary>
    /// Полоса берётся СВОЯ — проверяем обе стороны ошибки на ОДНОМ времени 01:00.00.
    /// Схватить чужую полосу можно в любую сторону, и обе дороги: взяли бы полосу помоложе —
    /// потеряли бы находку, взяли бы постарше — выдумали бы её на честной строке.
    /// </summary>
    [Fact]
    public void Masters_IsMeasuredByItsOwnBand_NotAnother()
    {
        var reference = RecordPlausibility.WorldReference([
            World("51.68"),
            World("56.96", category: "masters", ageKey: "25-29"),
            World("01:06.68", category: "masters", ageKey: "70-74"),
        ]);

        // В полосе 70-74 это быстрее её рекорда 01:06.68 — находка. Мерили бы полосой 25-29
        // (56.96) или абсолютом (51.68) — пропустили бы.
        Assert.Single(RecordPlausibility.Check([MastersEntry("70-74", "01:00.00")], reference));

        // В полосе 25-29 то же время медленнее её рекорда 56.96 — честная строка. Схвати мы
        // тут полосу постарше, получили бы находку на ровном месте.
        Assert.Empty(RecordPlausibility.Check([MastersEntry("25-29", "01:00.00")], reference));
    }

    /// <summary>
    /// Полосы в world/masters нет (её просто не принёс источник) — планка откатывается на
    /// абсолютный мировой, то есть на старое поведение, а не пропадает совсем.
    /// </summary>
    [Fact]
    public void Masters_WithoutBandInWorld_FallsBackToAbsolute()
    {
        var reference = RecordPlausibility.WorldReference([World("51.68")]);

        Assert.Empty(RecordPlausibility.Check([MastersEntry("70-74", "58.00")], reference));

        var found = Assert.Single(RecordPlausibility.Check([MastersEntry("70-74", "40.11")], reference));
        Assert.Contains("51.68", found.Note);
        Assert.DoesNotContain("полосе", found.Note);
    }

    /// <summary>
    /// Мировая ось мастерсов сама себя правилом 2 не судит: у world рекорд полосы и есть
    /// потолок, сравнивать его с собой бессмысленно (ветка world уходит в правила 1 и 3).
    /// </summary>
    [Fact]
    public void WorldMastersRow_IsNotJudgedAgainstItself()
    {
        var reference = RecordPlausibility.WorldReference([
            World("51.68"),
            World("01:06.68", category: "masters", ageKey: "70-74"),
        ]);

        var worldMasters = new RecordDiffEntry("world", "", "masters", "70-74", "female", "25m",
            "freestyle", "50m", "01:06.68", null, null, "01:05.00", null, null);

        Assert.Empty(RecordPlausibility.Check([worldMasters], reference));
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

    /// <summary>Мировая строка эталона на той же дисциплине, что и <see cref="Entry"/>.</summary>
    private static RecordPlausibility.WorldRow World(string time, string category = "open", string ageKey = "") =>
        new(category, ageKey, "female", "25m", "freestyle", "50m", time);

    /// <summary>Строка диффа мастерского рекорда страны в полосе.</summary>
    private static RecordDiffEntry MastersEntry(string ageKey, string newTime, string? oldTime = null) =>
        new("country", "ISR", "masters", ageKey, "female", "25m", "freestyle", "50m",
            oldTime, null, null, newTime, null, null);

    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Защита оспоренного времени (И-25, решение Влада 17.09.2026): значение, которое человек
/// уже признал ошибкой источника, импорт НЕ БЕРЁТ — в базе остаётся то, что лежит.
///
/// Живой повод: справочник федерации от 17.09.2026 заменил рекорд 17 ж 50 в/с 50 м 25.03
/// (11.06.2021, подтверждён World Aquatics) на 25.23 — заплыв декабря 2020, который
/// медленнее. Метка в реестре тут не помогает: она метит значение, а Apply всё равно
/// перезаписал бы верное число неверным.
///
/// ⚠ Это единственное сознательное отступление от «копия обязана совпадать с источником»,
/// поэтому тесты держат обе границы: статус, поставленный ЧЕЛОВЕКОМ, защищает, а
/// <c>candidate</c> от автомата — нет (правило §3: автомат только предлагает).
/// </summary>
public class RecordSourceOverruleTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RecordDiffService CreateService(SwimmDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()));

    /// <summary>Тот самый рекорд: 17 ж 50 в/с 50 м, верное значение.</summary>
    private static Swimm.Domain.Entities.Record StoredGorbenko() => new()
    {
        RegionType = "country", RegionCode = "ISR", Category = "age", AgeKey = "17",
        Gender = "female", PoolType = "50m", Style = "freestyle", Distance = "50m",
        Time = "25.03", HolderName = "אנסטסיה גורבנקו", RecordDate = "18/12/2020",
    };

    /// <summary>То, что приехало из нового выпуска справочника, — медленнее на 0.20.</summary>
    private static ParsedRecordDto IncomingSlower() => new(
        "country", "ISR", "age", "17", "female", "50m", "freestyle", "50m",
        "25.23", "אנסטסיה גורבנקו", null, "ISR", "18/12/2020");

    private static RecordIssue IssueOn(string time, string status) => new()
    {
        RegionType = "country", RegionCode = "ISR", Category = "age", AgeKey = "17",
        Gender = "female", PoolType = "50m", Style = "freestyle", Distance = "50m",
        FlaggedTime = time, Reason = RecordIssueReasons.SlowerThanStored, Status = status,
        Note = "Источник потерял рекорд: 25.03 от 11.06.2021 подтверждён World Aquatics.",
        CreatedBy = "vlad",
    };

    private static async Task<(RecordDiffResult Diff, RecordDiffApplyResult Applied, string StoredTime)>
        RunAsync(string dbName, RecordIssue? issue)
    {
        await using var db = CreateDb(dbName);
        db.Records.Add(StoredGorbenko());
        if (issue != null) db.RecordIssues.Add(issue);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var diff = await service.BuildDiffAsync("isrorg-age", [IncomingSlower()]);
        var applied = await service.ApplyAsync(new RecordDiffApplyRequest(diff.DiffId, true, true));

        var stored = await db.Records.AsNoTracking().SingleAsync();
        return (diff, applied, stored.Time);
    }

    [Fact]
    public async Task OpenIssueOnIncomingTime_KeepsStoredRecord()
    {
        var (diff, applied, storedTime) = await RunAsync(
            nameof(OpenIssueOnIncomingTime_KeepsStoredRecord),
            IssueOn("25.23", RecordIssueStatuses.Open));

        Assert.Equal("25.03", storedTime);
        Assert.Equal(0, applied.AppliedCount);
        Assert.Equal(1, applied.ProtectedCount);

        // Строка обязана остаться ВИДНОЙ в диффе: «изменившихся 1, применено 0» без пометки
        // выглядело бы как сбой импорта.
        Assert.Equal(1, diff.ChangedCount);
        Assert.Equal(1, diff.ProtectedCount);
        Assert.True(Assert.Single(diff.Changed).ProtectedByIssue);
    }

    [Theory]
    [InlineData(RecordIssueStatuses.Reported)]
    [InlineData(RecordIssueStatuses.Accepted)]
    public async Task ReportedAndAcceptedAlsoProtect(string status)
    {
        var (_, applied, storedTime) = await RunAsync(
            nameof(ReportedAndAcceptedAlsoProtect) + status, IssueOn("25.23", status));

        Assert.Equal("25.03", storedTime);
        Assert.Equal(1, applied.ProtectedCount);
    }

    /// <summary>
    /// Кандидат от автомата НЕ защищает: иначе сторож импорта сам себе разрешал бы отвергать
    /// источник, а правило §3 плана — «автомат только предлагает, решает человек».
    /// </summary>
    [Fact]
    public async Task CandidateIssue_DoesNotProtect()
    {
        var (_, applied, storedTime) = await RunAsync(
            nameof(CandidateIssue_DoesNotProtect),
            IssueOn("25.23", RecordIssueStatuses.Candidate));

        Assert.Equal("25.23", storedTime);
        Assert.Equal(1, applied.AppliedCount);
        Assert.Equal(0, applied.ProtectedCount);
    }

    /// <summary>
    /// <c>rejected</c> — «разобрались, запись верна»: защищать тут нечего, значение источника
    /// как раз правильное. Ровно этот статус получила эстафета 16 м 4×50 к/п (источник починил
    /// свой же дубль), и её импорт обязан применять.
    /// </summary>
    [Fact]
    public async Task RejectedIssue_DoesNotProtect()
    {
        var (_, applied, storedTime) = await RunAsync(
            nameof(RejectedIssue_DoesNotProtect),
            IssueOn("25.23", RecordIssueStatuses.Rejected));

        Assert.Equal("25.23", storedTime);
        Assert.Equal(1, applied.AppliedCount);
    }

    /// <summary>
    /// Претензия висит на ЗНАЧЕНИИ: когда федерация исправит файл, приедет другое время,
    /// ключ не совпадёт — и защита снимется сама, без правки реестра. Здесь источник прислал
    /// верное 25.03… то есть новый, ещё более быстрый рекорд.
    /// </summary>
    [Fact]
    public async Task IssueOnAnotherTime_DoesNotBlockRealNewRecord()
    {
        await using var db = CreateDb(nameof(IssueOnAnotherTime_DoesNotBlockRealNewRecord));
        db.Records.Add(StoredGorbenko());
        db.RecordIssues.Add(IssueOn("25.23", RecordIssueStatuses.Open));
        await db.SaveChangesAsync();

        var faster = IncomingSlower() with { Time = "24.91", RecordDate = "01/07/2026" };
        var service = CreateService(db);
        var diff = await service.BuildDiffAsync("isrorg-age", [faster]);
        var applied = await service.ApplyAsync(new RecordDiffApplyRequest(diff.DiffId, true, true));

        Assert.Equal(0, applied.ProtectedCount);
        Assert.Equal(1, applied.AppliedCount);
        Assert.Equal("24.91", (await db.Records.AsNoTracking().SingleAsync()).Time);
    }

    /// <summary>
    /// Ключ реестра нормализует регистр и суффикс дистанции (<c>RecordIssueKey</c>): претензию,
    /// заведённую руками как «50» и «Female», импорт обязан узнать в «50m» / «female».
    /// </summary>
    [Fact]
    public async Task IssueKeyNormalization_MatchesHandWrittenAxes()
    {
        var issue = IssueOn("25.23", RecordIssueStatuses.Open);
        issue.Distance = "50";
        issue.Gender = "Female";
        issue.Style = "Freestyle";

        var (_, applied, storedTime) = await RunAsync(
            nameof(IssueKeyNormalization_MatchesHandWrittenAxes), issue);

        Assert.Equal("25.03", storedTime);
        Assert.Equal(1, applied.ProtectedCount);
    }

    /// <summary>
    /// Защита точечная: она отвергает ОДНО значение, а не всю выборку. Соседняя дисциплина в
    /// том же диффе применяется как обычно.
    /// </summary>
    [Fact]
    public async Task ProtectionIsPerRow_NeighbourStillApplies()
    {
        await using var db = CreateDb(nameof(ProtectionIsPerRow_NeighbourStillApplies));
        db.Records.Add(StoredGorbenko());
        db.RecordIssues.Add(IssueOn("25.23", RecordIssueStatuses.Open));
        await db.SaveChangesAsync();

        var neighbour = IncomingSlower() with { Style = "backstroke", Time = "28.21" };
        var service = CreateService(db);
        var diff = await service.BuildDiffAsync("isrorg-age", [IncomingSlower(), neighbour]);
        var applied = await service.ApplyAsync(new RecordDiffApplyRequest(diff.DiffId, true, true));

        Assert.Equal(1, applied.ProtectedCount);
        Assert.Equal(1, applied.AppliedCount);
        Assert.Equal("25.03", (await db.Records.AsNoTracking()
            .SingleAsync(r => r.Style == "freestyle")).Time);
        Assert.Equal("28.21", (await db.Records.AsNoTracking()
            .SingleAsync(r => r.Style == "backstroke")).Time);
    }
}

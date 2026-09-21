using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Журнал проверок источников рекордов (docs/plans/records-freshness-plan.md, U2–U3, U7).
///
/// Стережём обещания плана:
/// <list type="number">
/// <item>проверка в базу рекордов НЕ пишет — только строку журнала;</item>
/// <item>сбой источника — строка <c>failed</c>, и «проверено» от неё НЕ сдвигается: 504 не
/// должен выглядеть свежестью;</item>
/// <item>Apply отмечает СВОЮ проверку и не применяет дифф, который перекрыт более свежей;</item>
/// <item>«пора проверить» — старше 7 дней или последняя попытка упала;</item>
/// <item>хэш не зависит от порядка строк (у <c>wa-junior</c> четыре запроса параллельно).</item>
/// </list>
/// </summary>
public class RecordSourceCheckServiceTests
{
    private static readonly DateTime T0 = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Провайдер, который отдаёт то, что ему скажут, или падает, как World Aquatics с 504.</summary>
    private sealed class FakeProvider(string source) : IRecordSourceProvider
    {
        public string Source => source;
        public IReadOnlyList<ParsedRecordDto> Rows { get; set; } = [];
        public Exception? Fail { get; set; }

        public Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(RecordSourceRequest request, CancellationToken ct = default) =>
            Fail != null ? Task.FromException<IReadOnlyList<ParsedRecordDto>>(Fail) : Task.FromResult(Rows);
    }

    private sealed class Clock
    {
        public DateTime Now { get; set; } = T0;
    }

    private static (RecordSourceCheckService Service, FakeProvider Provider, Clock Clock) Build(SwimmDbContext db)
    {
        var provider = new FakeProvider(RecordSources.WaJunior);
        var clock = new Clock();
        var diff = new RecordDiffService(db, new MemoryCache(new MemoryCacheOptions()));
        return (new RecordSourceCheckService(db, [provider], diff, () => clock.Now), provider, clock);
    }

    private static ParsedRecordDto Wjr(string distance, string time) =>
        new("world", "", "junior", "14-17", "female", "50m", "freestyle", distance, time,
            "Claire Curzan", null, "USA", "14/05/2021");

    [Fact]
    public async Task Check_WritesJournal_ButNotRecords()
    {
        await using var db = CreateDb(nameof(Check_WritesJournal_ButNotRecords));
        var (service, provider, _) = Build(db);
        provider.Rows = [Wjr("50m", "24.17"), Wjr("100m", "52.70")];

        var result = await service.CheckAsync(RecordSources.WaJunior);

        Assert.Equal(RecordSourceCheckOutcomes.ChangesFound, result.Outcome);
        Assert.Equal(2, result.Diff!.AddedCount);
        Assert.Empty(db.Records);                         // проверка — не Apply
        var row = Assert.Single(db.RecordSourceChecks);
        Assert.Equal(result.Diff.DiffId, row.DiffId);
        Assert.Equal(2, row.AddedCount);
        Assert.NotNull(row.ContentHash);
        Assert.Null(row.AppliedAt);
    }

    [Fact]
    public async Task Check_SameAsBase_IsUnchanged()
    {
        await using var db = CreateDb(nameof(Check_SameAsBase_IsUnchanged));
        db.Records.Add(new Record
        {
            RegionType = "world", RegionCode = "", Category = "junior", AgeKey = "14-17",
            Gender = "female", PoolType = "50m", Style = "freestyle", Distance = "50m",
            Time = "24.17", HolderName = "Claire Curzan", RecordDate = "14/05/2021", UpdatedAt = T0,
        });
        await db.SaveChangesAsync();
        var (service, provider, _) = Build(db);
        provider.Rows = [Wjr("50m", "24.17")];

        var result = await service.CheckAsync(RecordSources.WaJunior);

        Assert.Equal(RecordSourceCheckOutcomes.Unchanged, result.Outcome);
    }

    /// <summary>
    /// 504 — это сбой, а не «проверено, изменений нет»: строка failed с текстом, и дата
    /// «checked» остаётся от прошлой УСПЕШНОЙ проверки.
    /// </summary>
    [Fact]
    public async Task Failure_IsJournaled_AndDoesNotMoveCheckedAt()
    {
        await using var db = CreateDb(nameof(Failure_IsJournaled_AndDoesNotMoveCheckedAt));
        var (service, provider, clock) = Build(db);
        provider.Rows = [Wjr("50m", "24.17")];
        await service.CheckAsync(RecordSources.WaJunior);

        clock.Now = T0.AddDays(2);
        provider.Fail = new HttpRequestException("504 Gateway Timeout");
        var failed = await service.CheckAsync(RecordSources.WaJunior);

        Assert.Equal(RecordSourceCheckOutcomes.Failed, failed.Outcome);
        Assert.Null(failed.Diff);
        Assert.Contains("504", failed.Error);

        var f = Assert.Single(await service.GetFreshnessAsync(), x => x.Source == RecordSources.WaJunior);
        Assert.Equal(T0, f.CheckedAt);                    // не сдвинулась
        Assert.Equal(T0.AddDays(2), f.LastAttemptAt);
        Assert.Equal(RecordSourceCheckOutcomes.Failed, f.LastOutcome);
        Assert.Contains("504", f.LastError);
        Assert.True(f.NeedsCheck);                        // упавшая — жёлтая сразу, хоть и 2 дня
    }

    [Fact]
    public async Task EmptySource_IsFailure_NotUnchanged()
    {
        await using var db = CreateDb(nameof(EmptySource_IsFailure_NotUnchanged));
        var (service, provider, _) = Build(db);
        provider.Rows = [];

        var result = await service.CheckAsync(RecordSources.WaJunior);

        Assert.Equal(RecordSourceCheckOutcomes.Failed, result.Outcome);
    }

    [Fact]
    public async Task Apply_MarksItsOwnCheck_AndClearsPending()
    {
        await using var db = CreateDb(nameof(Apply_MarksItsOwnCheck_AndClearsPending));
        var (service, provider, clock) = Build(db);
        provider.Rows = [Wjr("50m", "24.17")];
        var check = await service.CheckAsync(RecordSources.WaJunior);

        var before = Assert.Single(await service.GetFreshnessAsync(), x => x.Source == RecordSources.WaJunior);
        Assert.Equal(1, before.PendingAdded);

        clock.Now = T0.AddMinutes(5);
        var applied = await service.ApplyAsync(new RecordDiffApplyRequest(check.Diff!.DiffId, true, true));

        Assert.True(applied.Success);
        Assert.Single(db.Records);
        Assert.Equal(T0.AddMinutes(5), db.RecordSourceChecks.Single().AppliedAt);
        var after = Assert.Single(await service.GetFreshnessAsync(), x => x.Source == RecordSources.WaJunior);
        Assert.Equal(0, after.PendingAdded);
        Assert.Equal(T0.AddMinutes(5), after.AppliedAt);
    }

    /// <summary>
    /// Дифф вчерашней проверки после сегодняшней не применяется: журнал не должен врать,
    /// какое состояние источника легло в базу.
    /// </summary>
    [Fact]
    public async Task Apply_SupersededCheck_IsRefused()
    {
        await using var db = CreateDb(nameof(Apply_SupersededCheck_IsRefused));
        var (service, provider, clock) = Build(db);
        provider.Rows = [Wjr("50m", "24.17")];
        var old = await service.CheckAsync(RecordSources.WaJunior);

        clock.Now = T0.AddDays(1);
        await service.CheckAsync(RecordSources.WaJunior);

        var result = await service.ApplyAsync(new RecordDiffApplyRequest(old.Diff!.DiffId, true, true));

        Assert.False(result.Success);
        Assert.Contains("перепроверен", result.Error);
        Assert.Empty(db.Records);
    }

    /// <summary>Упавшая проверка после успешной НЕ перекрывает её: применять есть что.</summary>
    [Fact]
    public async Task Apply_AfterLaterFailure_StillWorks()
    {
        await using var db = CreateDb(nameof(Apply_AfterLaterFailure_StillWorks));
        var (service, provider, clock) = Build(db);
        provider.Rows = [Wjr("50m", "24.17")];
        var ok = await service.CheckAsync(RecordSources.WaJunior);

        clock.Now = T0.AddHours(1);
        provider.Fail = new HttpRequestException("504");
        await service.CheckAsync(RecordSources.WaJunior);

        var result = await service.ApplyAsync(new RecordDiffApplyRequest(ok.Diff!.DiffId, true, true));

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(8, true)]
    public async Task NeedsCheck_AfterSevenDays(int days, bool expected)
    {
        await using var db = CreateDb($"stale-{days}");
        var (service, provider, clock) = Build(db);
        provider.Rows = [Wjr("50m", "24.17")];
        await service.CheckAsync(RecordSources.WaJunior);

        clock.Now = T0.AddDays(days);

        var f = Assert.Single(await service.GetFreshnessAsync(), x => x.Source == RecordSources.WaJunior);
        Assert.Equal(expected, f.NeedsCheck);
    }

    /// <summary>Источник, который ни разу не проверяли, — «пора проверить», а не «свежий».</summary>
    [Fact]
    public async Task NeverChecked_NeedsCheck()
    {
        await using var db = CreateDb(nameof(NeverChecked_NeedsCheck));
        var (service, _, _) = Build(db);

        var all = await service.GetFreshnessAsync();

        Assert.Equal(RecordSources.Order, all.Select(f => f.Source));
        Assert.All(all, f =>
        {
            Assert.Null(f.CheckedAt);
            Assert.True(f.NeedsCheck);
        });
    }

    /// <summary>
    /// Клетка с двумя хозяевами (country/*/open, И-13): WA приносит туда устаревшее значение
    /// при каждой проверке, федерация кладёт своё обратно следующим шагом. Такие строки — не
    /// «изменения, ждущие Apply»: живой замер 21.09.2026 дал 86 штук, и все — Израиль open.
    /// </summary>
    [Fact]
    public async Task ContestedSlotChanges_DoNotCountAsPending()
    {
        await using var db = CreateDb(nameof(ContestedSlotChanges_DoNotCountAsPending));
        db.Records.Add(new Record
        {
            RegionType = "country", RegionCode = "ISR", Category = "open", AgeKey = "",
            Gender = "female", PoolType = "25m", Style = "freestyle", Distance = "50m",
            Time = "24.46", HolderName = "Federation value", RecordDate = "01/01/2026", UpdatedAt = T0,
        });
        await db.SaveChangesAsync();
        var provider = new FakeProvider(RecordSources.WorldRecords)
        {
            Rows = [new ParsedRecordDto("country", "ISR", "open", "", "female", "25m", "freestyle", "50m",
                "24.53", "WA value", null, "ISR", "01/01/2025")],
        };
        var service = new RecordSourceCheckService(db, [provider],
            new RecordDiffService(db, new MemoryCache(new MemoryCacheOptions())), () => T0);

        var result = await service.CheckAsync(RecordSources.WorldRecords);

        Assert.Equal(1, result.Diff!.ChangedCount);          // дифф честный — строка там есть
        Assert.Equal(RecordSourceCheckOutcomes.Unchanged, result.Outcome);
        Assert.Equal(0, db.RecordSourceChecks.Single().ChangedCount);
    }

    [Fact]
    public void ContentHash_IgnoresRowOrder_ButNotValues()
    {
        var a = Wjr("50m", "24.17");
        var b = Wjr("100m", "52.70");

        Assert.Equal(
            RecordSourceCheckService.ContentHash([a, b]),
            RecordSourceCheckService.ContentHash([b, a]));
        Assert.NotEqual(
            RecordSourceCheckService.ContentHash([a, b]),
            RecordSourceCheckService.ContentHash([a, b with { Time = "52.69" }]));
    }

    [Fact]
    public async Task CheckAll_OneFailure_DoesNotStopOthers()
    {
        await using var db = CreateDb(nameof(CheckAll_OneFailure_DoesNotStopOthers));
        var broken = new FakeProvider(RecordSources.WaMasters) { Fail = new HttpRequestException("504") };
        var fine = new FakeProvider(RecordSources.WaJunior) { Rows = [Wjr("50m", "24.17")] };
        var service = new RecordSourceCheckService(db, [broken, fine],
            new RecordDiffService(db, new MemoryCache(new MemoryCacheOptions())), () => T0);

        var results = await service.CheckAllAsync();

        Assert.Equal([RecordSources.WaMasters, RecordSources.WaJunior], results.Select(r => r.Source));
        Assert.Equal(RecordSourceCheckOutcomes.Failed, results[0].Outcome);
        Assert.Equal(RecordSourceCheckOutcomes.ChangesFound, results[1].Outcome);
    }
}

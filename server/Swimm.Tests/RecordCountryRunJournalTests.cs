using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Прогон по странам в журнале свежести (16.11.2026). Боевой прогон на проде скачал 16 стран
/// из 215 — источник в это время падал, — а журнал записал его как обычную проверку:
/// <list type="number">
/// <item>после Apply сайт подписал «checked 16 Nov» рекорды ВСЕХ стран, хотя ~200 не
/// скачивались. Неполный прогон теперь — <c>failed</c> и «checked» не двигает;</item>
/// <item>«изменено: 1» при 28 применённых: фильтр клетки с двумя хозяевами (country/*/open,
/// И-13) выкидывал всё, что прогон приносит. У прогона по странам он не действует.</item>
/// </list>
/// </summary>
public class RecordCountryRunJournalTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 17, 0, 0, DateTimeKind.Utc);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ParsedRecordDto Nr(string code, string time, string date) =>
        new("country", code, "open", "", "male", "25m", "freestyle", "50m", time,
            "Holder " + code, null, code, date);

    private static async Task<(RecordSourceCheckService Service, RecordDiffResult Diff)> BuildAsync(
        SwimmDbContext db, DateTime now)
    {
        db.Records.Add(new Record
        {
            RegionType = "country", RegionCode = "AUS", Category = "open", AgeKey = "",
            Gender = "male", PoolType = "25m", Style = "freestyle", Distance = "50m",
            Time = "20.24", HolderName = "Old holder", HolderCountry = "AUS", RecordDate = "01/01/2022",
            UpdatedAt = T0.AddDays(-60),
        });
        await db.SaveChangesAsync();

        var diffService = new RecordDiffService(db, new MemoryCache(new MemoryCacheOptions()));
        // Источник прогона — worldrecords (так его видит RecordCountryRunner).
        var diff = await diffService.BuildDiffAsync("worldrecords",
            [Nr("AUS", "20.13", "08/10/2026"), Nr("ARU", "24.88", "25/10/2026")]);
        return (new RecordSourceCheckService(db, [], diffService, () => now), diff);
    }

    private static async Task<RecordSourceFreshnessDto> CountriesAsync(RecordSourceCheckService s) =>
        Assert.Single(await s.GetFreshnessAsync(), f => f.Source == RecordSources.WorldRecordsCountries);

    [Fact]
    public async Task Full_run_counts_country_rows_as_changes()
    {
        await using var db = CreateDb(nameof(Full_run_counts_country_rows_as_changes));
        var (service, diff) = await BuildAsync(db, T0);

        await service.LogRunAsync(RecordSources.WorldRecordsCountries, diff, null);

        var row = Assert.Single(db.RecordSourceChecks);
        Assert.Equal(RecordSourceCheckOutcomes.ChangesFound, row.Outcome);
        Assert.Equal((1, 1), (row.AddedCount, row.ChangedCount));
    }

    [Fact]
    public async Task Incomplete_run_is_failed_and_does_not_move_checked_even_after_apply()
    {
        await using var db = CreateDb(nameof(Incomplete_run_is_failed_and_does_not_move_checked_even_after_apply));
        var (service, diff) = await BuildAsync(db, T0);

        await service.LogRunAsync(RecordSources.WorldRecordsCountries, diff, "Прогон неполный: …");

        var row = Assert.Single(db.RecordSourceChecks);
        Assert.Equal(RecordSourceCheckOutcomes.Failed, row.Outcome);
        Assert.Equal(diff.DiffId, row.DiffId);           // Apply должен найти свою строку
        Assert.Equal((1, 1), (row.AddedCount, row.ChangedCount));

        var apply = await service.ApplyAsync(new RecordDiffApplyRequest(diff.DiffId, true, true));
        Assert.True(apply.Success);
        Assert.Equal(T0, db.RecordSourceChecks.Single().AppliedAt);

        var f = await CountriesAsync(service);
        Assert.Null(f.VerifiedAt);                       // витринное «checked» — не сдвинулось
        Assert.Null(f.CheckedAt);                        // админское «проверено» — тоже
        Assert.Equal(T0, f.AppliedAt);                   // а применение видно
        Assert.True(f.NeedsCheck);
        Assert.Equal((0, 0), (f.PendingAdded, f.PendingChanged));
    }

    [Fact]
    public void Contested_slot_filter_stays_for_world_records_source()
    {
        var entry = new RecordDiffEntry(
            "country", "ISR", "open", "", "female", "25m", "freestyle", "50m",
            "24.46", "Federation", "01/01/2026", "24.53", "WA", "01/01/2025");

        Assert.False(RecordSourceCheckService.CountsAsChange(RecordSources.WorldRecords, entry));
        Assert.True(RecordSourceCheckService.CountsAsChange(RecordSources.WorldRecordsCountries, entry));
    }

    // ── IncompleteError ───────────────────────────────────────────────────────────────

    private static RecordCountryRunStatus Status(int total, params string[] failed) => new()
    {
        Total = total,
        Failed = failed.Select(c => new RecordCountryRunFailure(c, "504")).ToArray(),
    };

    [Fact]
    public void No_failures_is_complete() =>
        Assert.Null(RecordCountryRun.IncompleteError(Status(215)));

    [Fact]
    public void World_failure_alone_does_not_make_run_incomplete() =>
        Assert.Null(RecordCountryRun.IncompleteError(Status(215, RecordCountryRun.WorldCode)));

    [Fact]
    public void Country_failures_are_counted_and_listed()
    {
        var codes = Enumerable.Range(0, 25).Select(i => $"C{i:00}").ToArray();
        var error = RecordCountryRun.IncompleteError(Status(215, [RecordCountryRun.WorldCode, .. codes]));

        Assert.NotNull(error);
        Assert.Contains("25 из 215", error);
        Assert.Contains("C00", error);
        Assert.DoesNotContain("C24", error);
        Assert.Contains("и ещё 5", error);
        Assert.DoesNotContain("WR", error);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Реестр проверок (docs/data-integrity.md, фаза Д3). Главное здесь — жизнь находки МЕЖДУ
/// прогонами: она живёт до устранения, а решение «принято как есть» переживает прогоны.
/// Иначе неустранимые находки (ошибка в протоколе федерации) пришлось бы закрывать заново
/// каждый раз — ровно та боль, ради которой в качестве результатов появился SuspectIsManual.
/// </summary>
public class DataCheckRunnerTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Проверка-заглушка: отдаёт то, что ей положили, и умеет падать.</summary>
    private sealed class FakeCheck(string id, DataCheckSeverity severity = DataCheckSeverity.Error) : IDataCheck
    {
        public string Id => id;
        public string Title => "Тестовая проверка";
        public string Description => "—";
        public DataCheckSeverity Severity => severity;

        public List<DataCheckItem> Items { get; } = [];
        public bool Throws { get; set; }

        public Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("проверка сломалась");
            return Task.FromResult(new DataCheckOutcome(Items.Count, Items.ToList()));
        }
    }

    private static DataCheckItem Item(int id, string message = "находка") =>
        new("Result", id, message);

    [Fact]
    public async Task Run_RecordsFindings_AndCounts()
    {
        await using var db = CreateDb(nameof(Run_RecordsFindings_AndCounts));
        var check = new FakeCheck("test.check");
        check.Items.Add(Item(1));
        check.Items.Add(Item(2));

        var run = await new DataCheckRunner(db, [check]).RunAllAsync("manual");

        Assert.Equal(2, run.ErrorCount);
        Assert.Equal(2, await db.DataCheckFindings.CountAsync());
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task Run_SameFindingTwice_NotDuplicated_ButRefreshed()
    {
        await using var db = CreateDb(nameof(Run_SameFindingTwice_NotDuplicated_ButRefreshed));
        var check = new FakeCheck("test.check");
        check.Items.Add(Item(1, "было"));
        var runner = new DataCheckRunner(db, [check]);
        await runner.RunAllAsync("manual");

        check.Items.Clear();
        check.Items.Add(Item(1, "стало"));
        await runner.RunAllAsync("manual");

        var f = Assert.Single(await db.DataCheckFindings.ToListAsync());
        Assert.Equal("стало", f.Message);
        Assert.Null(f.Resolution);
        Assert.True(f.LastSeenAt >= f.FirstSeenAt);
    }

    [Fact]
    public async Task Run_FindingGone_MarkedFixed()
    {
        await using var db = CreateDb(nameof(Run_FindingGone_MarkedFixed));
        var check = new FakeCheck("test.check");
        check.Items.Add(Item(1));
        var runner = new DataCheckRunner(db, [check]);
        await runner.RunAllAsync("manual");

        check.Items.Clear();
        var run = await runner.RunAllAsync("manual");

        var f = Assert.Single(await db.DataCheckFindings.ToListAsync());
        Assert.Equal(DataCheckResolutions.Fixed, f.Resolution);
        Assert.NotNull(f.ResolvedAt);
        Assert.Equal(1, run.FixedCount);
        Assert.Equal(0, run.ErrorCount);
    }

    [Fact]
    public async Task Accepted_SurvivesNextRun()
    {
        // Ключевое решение фазы Д3: принятое решение не переспрашиваем каждый прогон.
        await using var db = CreateDb(nameof(Accepted_SurvivesNextRun));
        var check = new FakeCheck("test.check");
        check.Items.Add(Item(1));
        var runner = new DataCheckRunner(db, [check]);
        await runner.RunAllAsync("manual");

        var findingId = (await db.DataCheckFindings.SingleAsync()).Id;
        Assert.True(await runner.AcceptAsync(findingId, "ошибка протокола федерации"));

        await runner.RunAllAsync("manual");

        var f = await db.DataCheckFindings.SingleAsync();
        Assert.Equal(DataCheckResolutions.Accepted, f.Resolution);
        Assert.Equal("ошибка протокола федерации", f.Note);

        var group = Assert.Single(await runner.GetCurrentAsync());
        Assert.Equal(0, group.OpenCount);
        Assert.Equal(1, group.AcceptedCount);
    }

    [Fact]
    public async Task Reopen_ReturnsFindingToWork()
    {
        await using var db = CreateDb(nameof(Reopen_ReturnsFindingToWork));
        var check = new FakeCheck("test.check");
        check.Items.Add(Item(1));
        var runner = new DataCheckRunner(db, [check]);
        await runner.RunAllAsync("manual");
        var id = (await db.DataCheckFindings.SingleAsync()).Id;
        await runner.AcceptAsync(id, "пока так");

        Assert.True(await runner.ReopenAsync(id));

        var f = await db.DataCheckFindings.SingleAsync();
        Assert.Null(f.Resolution);
        Assert.Null(f.Note);
    }

    [Fact]
    public async Task BrokenCheck_DoesNotKillRun_AndBecomesFindingItself()
    {
        // Упавшая проверка не должна лишать админа всей картины — и молчать о ней нельзя.
        await using var db = CreateDb(nameof(BrokenCheck_DoesNotKillRun_AndBecomesFindingItself));
        var broken = new FakeCheck("test.broken") { Throws = true };
        var healthy = new FakeCheck("test.ok", DataCheckSeverity.Warning);
        healthy.Items.Add(Item(7));

        var run = await new DataCheckRunner(db, [broken, healthy]).RunAllAsync("manual");

        Assert.Equal(1, run.WarningCount);
        Assert.Equal(1, run.ErrorCount);
        var brokenFinding = await db.DataCheckFindings.SingleAsync(f => f.CheckId == "test.broken");
        Assert.Contains("проверка не выполнилась", brokenFinding.Message);
    }

    [Fact]
    public async Task GetCurrent_ShowsAllChecks_IncludingClean()
    {
        // «Проверка есть и молчит» — информация, отличная от «проверки нет».
        await using var db = CreateDb(nameof(GetCurrent_ShowsAllChecks_IncludingClean));
        var withFinding = new FakeCheck("test.dirty");
        withFinding.Items.Add(Item(1));
        var clean = new FakeCheck("test.clean", DataCheckSeverity.Info);

        var runner = new DataCheckRunner(db, [withFinding, clean]);
        await runner.RunAllAsync("manual");

        var groups = await runner.GetCurrentAsync();
        Assert.Equal(2, groups.Count);
        Assert.Equal("test.dirty", groups[0].CheckId);   // с находками — первым
        Assert.Equal(0, groups[1].OpenCount);
    }

    [Fact]
    public async Task History_NewestFirst()
    {
        await using var db = CreateDb(nameof(History_NewestFirst));
        var runner = new DataCheckRunner(db, [new FakeCheck("test.check")]);
        await runner.RunAllAsync("manual");
        await runner.RunAllAsync("import");

        var history = await runner.GetHistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("import", history[0].Trigger);
    }
}

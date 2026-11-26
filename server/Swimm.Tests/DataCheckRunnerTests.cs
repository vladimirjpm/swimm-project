using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
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
        /// <summary>Полное число, если список «капнут» (больше, чем Items).</summary>
        public int? Total { get; set; }

        public Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("проверка сломалась");
            return Task.FromResult(new DataCheckOutcome(Total ?? Items.Count, Items.ToList()));
        }
    }

    /// <summary>
    /// Счётчики прогона (история на /Admin/Health) — открытые, тем же правилом: 26.11 история
    /// писала «предупреждений 39» при нуле открытых, потому что считала и принятые.
    /// </summary>
    [Fact]
    public async Task Run_CountsOpenOnly_SameRuleAsHealth()
    {
        await using var db = CreateDb(nameof(Run_CountsOpenOnly_SameRuleAsHealth));
        var warn = new FakeCheck("test.warn", DataCheckSeverity.Warning);
        warn.Items.Add(Item(1));
        warn.Items.Add(Item(2));
        var capped = new FakeCheck("test.capped") { Total = 120 };
        capped.Items.Add(Item(3));
        var runner = new DataCheckRunner(db, [warn, capped]);
        await runner.RunAllAsync("manual");

        foreach (var f in await db.DataCheckFindings.Where(f => f.CheckId == "test.warn").ToListAsync())
            await runner.AcceptAsync(f.Id, "не чиним");

        var run = await runner.RunAllAsync("manual");

        Assert.Equal(0, run.WarningCount);   // оба приняты
        Assert.Equal(120, run.ErrorCount);   // капнутый список — полным числом
        var health = await runner.GetCurrentAsync();
        Assert.Equal(health.Where(g => g.Severity == DataCheckSeverity.Error).Sum(g => g.OpenCount), run.ErrorCount);
    }

    // ── Одно правило «сколько открыто» для /Admin/Health и дашборда ─────────────────────

    [Theory]
    [InlineData(null, 3, 1, 3)]   // не гонялась — только список
    [InlineData(7, 0, 7, 0)]      // всё принято — ноль
    [InlineData(120, 50, 2, 118)] // список капнут — полное минус принятые
    [InlineData(5, 5, 3, 5)]      // открытые в списке число не отрицает
    public void OpenCount_OneRule(int? total, int openListed, int accepted, int expected) =>
        Assert.Equal(expected, DataCheckOpenCount.Of(total, openListed, accepted));

    /// <summary>
    /// Health (GetCurrentAsync) и дашборд (GetStateAsync) на одних данных дают одно число:
    /// 26.11 они разошлись — 0 в Health и 37 предупреждений на дашборде.
    /// </summary>
    [Theory]
    [InlineData(2, null, 0)] // всё как есть
    [InlineData(2, null, 2)] // всё принято
    [InlineData(3, 120, 1)]  // капнутый список, одна принята
    public async Task OpenCount_HealthAndDashboardAgree(int listed, int? total, int acceptCount)
    {
        await using var db = CreateDb($"{nameof(OpenCount_HealthAndDashboardAgree)}-{listed}-{total}-{acceptCount}");
        var check = new FakeCheck("test.check", DataCheckSeverity.Warning) { Total = total };
        for (var i = 1; i <= listed; i++) check.Items.Add(Item(i));
        var runner = new DataCheckRunner(db, [check]);
        await runner.RunAllAsync("manual");

        foreach (var f in (await db.DataCheckFindings.ToListAsync()).Take(acceptCount))
            await runner.AcceptAsync(f.Id, "не чиним");

        var health = Assert.Single(await runner.GetCurrentAsync());
        var (_, states) = await runner.GetStateAsync();
        var dashboard = Assert.Single(states);

        Assert.Equal(health.OpenCount, dashboard.Open);
        Assert.Equal((total ?? listed) - acceptCount, health.OpenCount);
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

    /// <summary>Проверка, нашедшая много, а положившая в список мало (срез на 50).</summary>
    private sealed class TruncatingCheck(string id, int total, int shown) : IDataCheck
    {
        public string Id => id;
        public string Title => "Проверка со срезом";
        public string Description => "—";
        public DataCheckSeverity Severity => DataCheckSeverity.Warning;

        public Task<DataCheckOutcome> RunAsync(CancellationToken ct = default) =>
            Task.FromResult(new DataCheckOutcome(total,
                Enumerable.Range(1, shown).Select(i => new DataCheckItem("Result", i, "находка")).ToList()));
    }

    [Fact]
    public async Task State_KeepsFullTotal_EvenWhenFindingListTruncated()
    {
        // Ради этого состояние и заведено: по таблице находок полное число не восстановить,
        // а дашборд должен показывать 8071, а не 50.
        await using var db = CreateDb(nameof(State_KeepsFullTotal_EvenWhenFindingListTruncated));
        var runner = new DataCheckRunner(db, [new TruncatingCheck("test.truncated", total: 8071, shown: 50)]);
        await runner.RunAllAsync("manual");

        var (lastRun, states) = await runner.GetStateAsync();

        var state = Assert.Single(states);
        Assert.Equal(8071, state.Total);
        Assert.Equal(50, state.Shown);
        Assert.False(state.Failed);
        Assert.Equal(lastRun!.Id, state.LastRunId);

        // И то же самое видно на /Admin/Health — «показано 50 из 8071».
        var group = Assert.Single(await runner.GetCurrentAsync());
        Assert.Equal(8071, group.Total);
        Assert.Equal(50, group.Findings.Count);
    }

    [Fact]
    public async Task State_MarksFailedCheck()
    {
        // Упавшая проверка: её Total ничего не значит, и потребитель должен это видеть.
        await using var db = CreateDb(nameof(State_MarksFailedCheck));
        var runner = new DataCheckRunner(db, [new FakeCheck("test.broken") { Throws = true }]);
        await runner.RunAllAsync("manual");

        var (_, states) = await runner.GetStateAsync();
        Assert.True(Assert.Single(states).Failed);
    }

    [Fact]
    public async Task State_IsEmpty_BeforeFirstRun()
    {
        // Ни одного прогона — потребитель обязан отличить это от «всё чисто».
        await using var db = CreateDb(nameof(State_IsEmpty_BeforeFirstRun));
        var (lastRun, states) = await new DataCheckRunner(db, [new FakeCheck("test.check")]).GetStateAsync();

        Assert.Null(lastRun);
        Assert.Empty(states);
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

    /// <summary>
    /// compID на isr.org.il для ссылки на протокол. У многодневки штамп живёт на событии
    /// (или на одном из дней — UNIQUE не даёт проставить его всем), поэтому день без своего
    /// compID обязан унаследовать событийный: иначе ссылка пропадала бы у дней 2..N.
    /// </summary>
    [Fact]
    public async Task GetCurrent_ResolvesOrgCompId_OwnThenEventThenSiblingDay()
    {
        await using var db = CreateDb(nameof(GetCurrent_ResolvesOrgCompId_OwnThenEventThenSiblingDay));

        var stampedEvent = new CompetitionEvent { Name = "Событие со штампом", OrgCompId = 6621 };
        var barEvent = new CompetitionEvent { Name = "Событие без штампа" };
        db.CompetitionEvents.AddRange(stampedEvent, barEvent);
        await db.SaveChangesAsync();

        var own = new Competition { Name = "Свой compID", Date = "01/06/2026", PoolType = "25m", OrgCompId = 7001 };
        var byEvent = new Competition
        {
            Name = "День 2 события", Date = "02/06/2026", PoolType = "25m", EventId = stampedEvent.Id
        };
        var stampedDay = new Competition
        {
            Name = "День 1 без штампа события", Date = "03/06/2026", PoolType = "25m",
            EventId = barEvent.Id, OrgCompId = 7100
        };
        var bySibling = new Competition
        {
            Name = "День 2 без штампа события", Date = "04/06/2026", PoolType = "25m", EventId = barEvent.Id
        };
        var orphan = new Competition { Name = "Не сопоставлено", Date = "05/06/2026", PoolType = "25m" };
        db.Competitions.AddRange(own, byEvent, stampedDay, bySibling, orphan);
        await db.SaveChangesAsync();

        var check = new FakeCheck("test.competitions");
        foreach (var c in new[] { own, byEvent, bySibling, orphan })
            check.Items.Add(new DataCheckItem("Competition", c.Id, c.Name));
        // Находка не про соревнование: compID ей взять неоткуда, и подставлять чужой нельзя.
        check.Items.Add(Item(1, "строка результата"));

        var runner = new DataCheckRunner(db, [check]);
        await runner.RunAllAsync("manual");

        var byMessage = (await runner.GetCurrentAsync())
            .Single().Findings.ToDictionary(f => f.Message, f => f.SubjectOrgCompId);

        Assert.Equal(7001, byMessage["Свой compID"]);
        Assert.Equal(6621, byMessage["День 2 события"]);
        Assert.Equal(7100, byMessage["День 2 без штампа события"]);
        Assert.Null(byMessage["Не сопоставлено"]);
        Assert.Null(byMessage["строка результата"]);
    }
}

using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Application.Validation;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р119 (docs/data-integrity.md): протокол свёрнут в одну дату, а у этого compID в базе уже есть
/// дни с другими датами и файл покрывает их строки — импорт положил бы те же результаты дублем.
/// Живой случай — e62 «אליפות אילת הפתוחה 2026» (compID 16891): дни 05.11 (766) и 06.11 (813)
/// затянуты путём Б по дням, а PDF loglig печатает все 1579 строк датой 07/11 (И-60).
/// </summary>
public class CollapsedProtocolTests
{
    private const int CompId = 16891;

    // ── правило ─────────────────────────────────────────────────────────────────

    private static CollapsedProtocolDay D(int id, string date, int rows) => new(id, date, rows);

    [Fact]
    public void E62_SingleDateFile_CoveringBothDays_IsCollapsed()
    {
        var f = CollapsedProtocolRule.Detect(["07/11/2026"], 1579, [D(1874, "05/11/2026", 766), D(1875, "06/11/2026", 813)]);

        Assert.NotNull(f);
        Assert.Equal("07/11/2026", f.FileDate);
        Assert.Equal(1579, f.OtherDaysRows);
        Assert.StartsWith(CollapsedProtocolRule.Marker, f.Message);
        Assert.Contains("те же результаты", f.Message);
        Assert.Contains("05/11/2026 — #1874, 766 строк", f.Message);
    }

    [Fact]
    public void InProgress_WholeProtocolOverDayOne_IsCollapsed()
    {
        // И-60: день 1 затянут (657), PDF идущего старта печатает все события одной датой (1556).
        var f = CollapsedProtocolRule.Detect(["07/11/2026"], 1556, [D(1874, "05/11/2026", 657)]);
        Assert.NotNull(f);
        Assert.DoesNotContain("те же результаты", f.Message);
    }

    [Fact]
    public void FileDateEqualsExistingLastDay_StillCollapsed()
    {
        // Если третий день в базе есть, compID сопоставит файл с ним — и с перезаписью строки
        // дней 1–2 легли бы в день 3. Тоже дубль.
        Assert.NotNull(CollapsedProtocolRule.Detect(["07/11/2026"], 1700,
            [D(1, "05/11/2026", 766), D(2, "06/11/2026", 813), D(3, "07/11/2026", 120)]));
    }

    [Fact]
    public void NotCollapsed_WhenFileHasSeveralDates() =>
        Assert.Null(CollapsedProtocolRule.Detect(["05/11/2026", "06/11/2026"], 1579,
            [D(1, "05/11/2026", 766), D(2, "06/11/2026", 813)]));

    [Fact]
    public void NotCollapsed_WhenOnlyTheSameDateIsInDb() =>
        // Обычный перезабор однодневки.
        Assert.Null(CollapsedProtocolRule.Detect(["21/11/2026"], 369, [D(1, "21/11/2026", 369)]));

    [Fact]
    public void NotCollapsed_WhenFileIsSmallerThanOtherDays() =>
        // Эстафеты одного дня со страниц событий loglig (--pull-relays) — законный перезабор дня.
        Assert.Null(CollapsedProtocolRule.Detect(["05/11/2026"], 40,
            [D(1, "05/11/2026", 766), D(2, "06/11/2026", 813)]));

    [Fact]
    public void NotCollapsed_WhenOtherDaysHaveNoResults() =>
        Assert.Null(CollapsedProtocolRule.Detect(["07/11/2026"], 500, [D(1, "05/11/2026", 0)]));

    [Fact]
    public void NotCollapsed_WhenNoDaysInDb() =>
        Assert.Null(CollapsedProtocolRule.Detect(["07/11/2026"], 500, []));

    // ── база: превью и импорт ────────────────────────────────────────────────────

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

    private const string Name = "אליפות אילת הפתוחה 2026";

    /// <summary>Строки одного «дня»: свои пловцы, по 8 на заплыв.</summary>
    private static IEnumerable<object> Rows(string date, string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => (object)new
        {
            country = "ISR",
            competition = Name,
            date,
            event_style_name = "Freestyle",
            event_style_len = "50",
            event_style_gender = "male",
            pool_type = "25m",
            position = i + 1,
            heat = i / 8 + 1,
            lane = i % 8 + 1,
            last_name = $"{prefix}{i}",
            first_name = "Tal",
            birth_year = 2012,
            club = "Club",
            time = $"00:{30 + i % 20:00}.00"
        });

    private static Stream ToStream(IEnumerable<object> rows) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows.ToList())));

    /// <summary>e62 в миниатюре: событие из двух дней (путь Б), штамп compID — только у первого.</summary>
    private static async Task SeedE62Async(SwimmDbContext db)
    {
        var svc = new JsonImportService(db, new NullCache());
        var r = await svc.ImportAsync(
            ToStream(Rows("05/11/2026", "a", 12).Concat(Rows("06/11/2026", "b", 14))), "days.json", null,
            new ImportEventOptions(null, Name));
        Assert.Equal(26, r.Created);
        var day1 = await db.Competitions.SingleAsync(c => c.Date == "05/11/2026");
        day1.OrgCompId = CompId;
        await db.SaveChangesAsync();
    }

    /// <summary>Тот же протокол, свёрнутый loglig в одну последнюю дату.</summary>
    private static IEnumerable<object> CollapsedFile() =>
        Rows("07/11/2026", "a", 12).Concat(Rows("07/11/2026", "b", 14));

    [Fact]
    public async Task Preview_Detects_E62()
    {
        // Ровно тот вызов, что делает DiscoveryPreviewService.PreviewAsync («Затянуть»/«Перезатянуть», пачка).
        await using var db = CreateDb(nameof(Preview_Detects_E62));
        await SeedE62Async(db);

        var f = await CollapsedProtocolGuard.DetectAsync(db, CompId, ["07/11/2026"], 26);

        Assert.NotNull(f);
        Assert.Equal(2, f.Days.Count);
        Assert.Equal(26, f.OtherDaysRows);
    }

    [Fact]
    public async Task Preview_IgnoresDaysWithForeignStamp()
    {
        // Событие из окружных протоколов: дни разных compID под одним EventId — чужой день не наш дубль.
        await using var db = CreateDb(nameof(Preview_IgnoresDaysWithForeignStamp));
        await SeedE62Async(db);
        var day2 = await db.Competitions.SingleAsync(c => c.Date == "06/11/2026");
        day2.OrgCompId = 99999;
        await db.SaveChangesAsync();

        // Файл одной датой дня 1 и того же размера, что день 1: чужой день 2 в расчёт не входит.
        Assert.Null(await CollapsedProtocolGuard.DetectAsync(db, CompId, ["05/11/2026"], 12));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // «Перезатянуть весь протокол события»: перезапись не обходит защиту
    public async Task Import_CollapsedFile_IsRefused_NoDuplicateDay(bool overwrite)
    {
        await using var db = CreateDb(nameof(Import_CollapsedFile_IsRefused_NoDuplicateDay) + overwrite);
        await SeedE62Async(db);

        var r = await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(CollapsedFile()), "isrorg-16891-loglig-15896-he.pdf", null,
            new ImportEventOptions(null, null, OverwriteExisting: overwrite), CompId);

        Assert.StartsWith(CollapsedProtocolRule.Marker, r.Message);
        Assert.Equal(0, r.Created);
        Assert.Equal(2, await db.Competitions.CountAsync());
        Assert.Equal(26, await db.Results.CountAsync());
    }

    [Fact]
    public async Task Import_CollapsedFile_WithExplicitFlag_GoesThrough()
    {
        await using var db = CreateDb(nameof(Import_CollapsedFile_WithExplicitFlag_GoesThrough));
        await SeedE62Async(db);

        var r = await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(CollapsedFile()), "x.json", null,
            new ImportEventOptions(null, null, AllowCollapsedProtocol: true), CompId);

        Assert.Equal(26, r.Created); // осознанный обход: правило ошиблось — человек берёт на себя
        Assert.Equal(3, await db.Competitions.CountAsync());
        // След обхода — в журнале импорта, вместе с тем, на что согласились.
        var trace = Assert.Single(r.DiagnosticLog, l => l.StartsWith(CollapsedProtocolRule.OverrideMarker));
        Assert.Contains(CollapsedProtocolRule.Marker, trace);
    }

    [Fact]
    public async Task Import_FlagWithoutCollapse_LeavesNoOverrideTrace()
    {
        // Флаг на обычном перезаборе ничего не обходит — и в журнале не врёт про обход.
        await using var db = CreateDb(nameof(Import_FlagWithoutCollapse_LeavesNoOverrideTrace));
        await SeedE62Async(db);

        var r = await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(Rows("05/11/2026", "a", 12)), "x.json", null,
            new ImportEventOptions(null, null, OverwriteExisting: true, AllowCollapsedProtocol: true), CompId);

        Assert.DoesNotContain(r.DiagnosticLog, l => l.StartsWith(CollapsedProtocolRule.OverrideMarker));
    }

    // ── сценарий ревью: соседний compID в общем событии (ложный блок, ради него — галочка) ──

    /// <summary>
    /// Событие из протоколов разных номеров федерации (окружные e1/e7/e10/e12): день 2 — соседний
    /// compID, но штампа у него нет (импорт файлом). Детектор считает его «своим» — и законный
    /// перезабор дня 1 своего compID блокируется. Это поведение фиксируем: правило не чиним
    /// (решение Влада 23.11), выход — галочка «Я проверил» в превью.
    /// </summary>
    private static IEnumerable<object> GrownDayOne() => Rows("05/11/2026", "a", 14); // день 1 дорос до 14 строк

    [Fact]
    public async Task Review_NeighbourCompIdDayWithoutStamp_InSharedEvent_Blocks()
    {
        await using var db = CreateDb(nameof(Review_NeighbourCompIdDayWithoutStamp_InSharedEvent_Blocks));
        await SeedE62Async(db); // день 06/11 (14 строк) без штампа — здесь он «соседний compID»

        var f = await CollapsedProtocolGuard.DetectAsync(db, CompId, ["05/11/2026"], 14);
        Assert.NotNull(f); // ложный блок: дубля не было бы — это другой номер федерации

        var refused = await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(GrownDayOne()), "isrorg-16891.pdf", null,
            new ImportEventOptions(null, null, OverwriteExisting: true), CompId);
        Assert.StartsWith(CollapsedProtocolRule.Marker, refused.Message);
        Assert.Equal(26, await db.Results.CountAsync());
    }

    [Fact]
    public async Task Review_NeighbourCompIdDay_WithOverride_RepullsDayOne()
    {
        await using var db = CreateDb(nameof(Review_NeighbourCompIdDay_WithOverride_RepullsDayOne));
        await SeedE62Async(db);

        var r = await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(GrownDayOne()), "isrorg-16891.pdf", null,
            new ImportEventOptions(null, null, OverwriteExisting: true, AllowCollapsedProtocol: true), CompId);

        Assert.DoesNotContain(CollapsedProtocolRule.Marker, r.Message ?? "");
        Assert.Equal(2, await db.Competitions.CountAsync()); // нового дня нет
        Assert.Equal(14 + 14, await db.Results.CountAsync()); // день 1 дорос, соседний день не тронут
        Assert.Contains(r.DiagnosticLog, l => l.StartsWith(CollapsedProtocolRule.OverrideMarker));
    }

    // ── галочка из превью: что сервер принимает ─────────────────────────────────

    private static DiscoveryPreviewEntry Entry(CollapsedProtocolFinding? f) =>
        new(new ParsedCompetition { Format = "IsrOrg", ResultsJson = "[]" }, "f.pdf", 1, null, CollapsedProtocol: f);

    private static Swimm.API.Controllers.DiscoveryAdminController.DiscoveryImportRequest Req(bool allow) =>
        new(Guid.NewGuid(), null, null, null, AllowCollapsedProtocol: allow);

    [Fact]
    public void Controller_Override_OnlyWithCheckbox_AndOnlyForShownBlock()
    {
        var finding = CollapsedProtocolRule.Detect(["07/11/2026"], 1579, [D(1874, "05/11/2026", 766), D(1875, "06/11/2026", 813)]);

        Assert.True(Swimm.API.Controllers.DiscoveryAdminController.AllowsCollapsedProtocol(Req(true), Entry(finding)));
        // Без галочки — отказ, как раньше.
        Assert.False(Swimm.API.Controllers.DiscoveryAdminController.AllowsCollapsedProtocol(Req(false), Entry(finding)));
        // Галочка на превью без блока ничего не даёт: согласие — на показанный блок, не вслепую.
        Assert.False(Swimm.API.Controllers.DiscoveryAdminController.AllowsCollapsedProtocol(Req(true), Entry(null)));
    }

    [Fact]
    public async Task Bulk_CollapsedRow_ImportedFromBatch_NeverCarriesTheFlag()
    {
        // Пачка ставит NeedsReview, но админ может отметить строку и нажать «Импортировать» —
        // флаг обхода она не передаёт никогда, и импорт откажет (Р119).
        var (service, queue, previews) = BulkPullServiceTests.Build(BulkPullServiceTests.Row(1));
        previews.Collapsed = CollapsedProtocolRule.Detect(["01/11/2025"], 10, [D(1, "31/10/2025", 10)]);
        var started = await service.StartAsync([1], includeChampionships: false);
        var batch = await BulkPullServiceTests.WaitFinishedAsync(service, started.BatchId);
        Assert.Equal(BulkPullVerdict.NeedsReview, Assert.Single(batch.Rows).Verdict);

        await service.ImportAsync(started.BatchId, [1]);

        var job = Assert.Single(queue.Jobs);
        Assert.False(job.Options!.AllowCollapsedProtocol);
    }

    [Fact]
    public async Task Import_WithoutCompId_IsNotGuarded()
    {
        // Файл на /Admin/Import (путь Б по дням) — compID не передаётся, дату выбрал человек.
        await using var db = CreateDb(nameof(Import_WithoutCompId_IsNotGuarded));
        await SeedE62Async(db);

        var r = await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(CollapsedFile()), "day3.json");

        Assert.Equal(26, r.Created);
    }

    [Fact]
    public async Task Import_OrdinaryRepullOfOneDay_IsNotGuarded()
    {
        // Перезабор однодневки по compID — как раньше: перезапись, без отказа Р119.
        await using var db = CreateDb(nameof(Import_OrdinaryRepullOfOneDay_IsNotGuarded));
        var svc = new JsonImportService(db, new NullCache());
        await svc.ImportAsync(ToStream(Rows("21/11/2026", "a", 10)), "a.json");
        var c = await db.Competitions.SingleAsync();
        c.OrgCompId = 16926;
        await db.SaveChangesAsync();

        var r = await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(Rows("21/11/2026", "a", 10)), "b.json", null,
            new ImportEventOptions(null, null, OverwriteExisting: true), 16926);

        Assert.DoesNotContain(CollapsedProtocolRule.Marker, r.Message ?? "");
        Assert.Equal(10, await db.Results.CountAsync());
    }

    // ── пачка и results-auto ────────────────────────────────────────────────────

    private static DiscoveryPreviewResult CollapsedPreview()
    {
        var parsed = new ParsedCompetition
        {
            Format = "IsrOrg",
            ResultsJson = "[]",
            ResultCount = 1579,
            Competitions = [new ParsedCompetitionSummary(Name, "07/11/2026", 1579)],
            Warnings = []
        };
        var finding = CollapsedProtocolRule.Detect(["07/11/2026"], 1579, [D(1874, "05/11/2026", 766), D(1875, "06/11/2026", 813)]);
        // ExistingCompetitionId нет — ровно e62: по ключу и compID день 07/11 не найден.
        return new DiscoveryPreviewResult(Guid.NewGuid(), parsed, ["he"], null, [],
            new ImportRecordPreviewDto { Count = 0 }, null, CollapsedProtocol: finding);
    }

    [Fact]
    public void Bulk_CollapsedProtocol_NeedsReview()
    {
        var regulation = new RegulationFetchDto(true, "u", new RegulationAnalysisDto(true, false, false, [], PoolType: "25m"));

        var (verdict, reasons) = BulkPullClassifier.Classify(CollapsedPreview(), regulation, isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.NeedsReview, verdict);
        Assert.StartsWith(CollapsedProtocolRule.Marker, reasons[0]);
        Assert.EndsWith("нужна ручная проверка", reasons[0]);
    }

    [Fact]
    public void ResultsAuto_CollapsedProtocol_IsBlocked_NotTaken()
    {
        var regulation = new RegulationFetchDto(true, "u", new RegulationAnalysisDto(true, false, false, [], PoolType: "25m"));
        var (verdict, reasons) = BulkPullClassifier.Classify(CollapsedPreview(), regulation, isChampionshipByName: false);
        var progress = new CompetitionProgressSummary([new EventProgress("50 חופשי", false, 10, 10, true)]);
        var venue = new VenuePoolLookup(VenuePoolLookupStatus.NotInDirectory, "Eilat", "Eilat", null, null);

        var d = ResultsAutoRules.Decide(new ResultsAutoGateInput(verdict, reasons, false, 1, progress, "25m", venue));

        Assert.Equal(ResultsAutoVerdicts.Blocked, d.Verdict);
        Assert.Contains(d.Reasons, r => r.StartsWith(CollapsedProtocolRule.Marker));
    }
}

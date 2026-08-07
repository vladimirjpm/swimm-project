using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Точечное исправление «проставить пол» прямо из находки `results.no-gender`.
///
/// Ключевое: правим НЕ ТОЛЬКО пловца, но и его строки без пола. Проверка смотрит на
/// `Results.Gender`, а он заполняется на импорте из пловца — поставь пол только пловцу, и
/// находка висела бы до переимпорта, то есть кнопка выглядела бы сломанной.
/// </summary>
public class DataCheckFixGenderTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<(SwimmDbContext Db, int FindingId, int SwimmerId)> SeedAsync(string name)
    {
        var db = CreateDb(name);
        var swimmer = new Swimmer { LastName = "בואנו", FirstName = "בלה", BirthYear = 2012, Gender = "" };
        var club = new Club { Name = "Club" };
        var style = new Style { Name = "freestyle" };
        var comp = new Competition { Name = "Meet", Date = "01/02/2025", PoolType = "25m" };
        db.AddRange(swimmer, club, style, comp);
        await db.SaveChangesAsync();

        db.Results.AddRange(
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "200", Gender = "", TimeOriginal = "02:58.01", CompetitionDate = new DateTime(2025, 2, 1)
            },
            // Строка с УЖЕ проставленным полом — её трогать нельзя: там пол из протокола.
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "100", Gender = "female", TimeOriginal = "01:30.00", CompetitionDate = new DateTime(2025, 2, 1)
            });

        var finding = new DataCheckFinding
        {
            CheckId = "results.no-gender", Severity = 1, EntityType = "Result", EntityId = 1,
            Message = "בואנו בלה · 200 freestyle",
            SubjectName = "בואנו בלה",
            FixKind = DataCheckFixKinds.SwimmerGender,
            FixEntityId = swimmer.Id
        };
        db.DataCheckFindings.Add(finding);
        await db.SaveChangesAsync();

        return (db, finding.Id, swimmer.Id);
    }

    [Fact]
    public async Task SetsGender_OnSwimmerAndOnHisGenderlessRows()
    {
        var (db, findingId, swimmerId) = await SeedAsync(nameof(SetsGender_OnSwimmerAndOnHisGenderlessRows));
        await using var _ = db;

        var rows = await new DataCheckRunner(db, []).FixSwimmerGenderAsync(findingId, "female");

        Assert.Equal(1, rows);
        Assert.Equal("female", (await db.Swimmers.SingleAsync(s => s.Id == swimmerId)).Gender);
        Assert.All(await db.Results.ToListAsync(), r => Assert.Equal("female", r.Gender));
    }

    [Fact]
    public async Task DoesNotOverwriteGenderPrintedInProtocol()
    {
        // Пол из протокола — источник; перезаписывать его нельзя даже «правильным» значением.
        var (db, findingId, _) = await SeedAsync(nameof(DoesNotOverwriteGenderPrintedInProtocol));
        await using var _d = db;

        await new DataCheckRunner(db, []).FixSwimmerGenderAsync(findingId, "male");

        var printed = await db.Results.SingleAsync(r => r.Distance == "100");
        Assert.Equal("female", printed.Gender);
    }

    [Fact]
    public async Task CurrentGenderAndLogligId_AreVisibleInTheList()
    {
        // Проставленное должно быть видно сразу: иначе после нажатия непонятно, что уже
        // сделано, и человек жмёт по второму разу. Значения ЖИВЫЕ — читаются на выдаче,
        // а не берутся из находки: находку обновляет только прогон.
        var (db, findingId, swimmerId) = await SeedAsync(nameof(CurrentGenderAndLogligId_AreVisibleInTheList));
        await using var _ = db;

        var runner = new DataCheckRunner(db, [new FakeNoGenderCheck()]);
        await runner.FixSwimmerGenderAsync(findingId, "female");

        var swimmer = await db.Swimmers.SingleAsync(s => s.Id == swimmerId);
        swimmer.LogligId = 296445;
        await db.SaveChangesAsync();

        var finding = (await runner.GetCurrentAsync())
            .SelectMany(g => g.Findings)
            .Single(f => f.Id == findingId);

        Assert.Equal("female", finding.SubjectGender);
        Assert.Equal(296445, finding.SubjectLogligId);
    }

    [Fact]
    public async Task GenderOfTheRowWins_WhenSwimmerAlreadyHasOneButTheRowDoesNot()
    {
        // Пол пловца бывает известен из другого протокола, а спорная строка всё равно пустая.
        // Галочка обязана показывать строку — иначе кнопка выглядит нажатой, а находка висит.
        var (db, findingId, swimmerId) = await SeedAsync(
            nameof(GenderOfTheRowWins_WhenSwimmerAlreadyHasOneButTheRowDoesNot));
        await using var _ = db;

        var swimmer = await db.Swimmers.SingleAsync(s => s.Id == swimmerId);
        swimmer.Gender = "female";
        await db.SaveChangesAsync();

        var finding = (await new DataCheckRunner(db, [new FakeNoGenderCheck()]).GetCurrentAsync())
            .SelectMany(g => g.Findings)
            .Single(f => f.Id == findingId);

        Assert.Null(finding.SubjectGender);
    }

    [Fact]
    public async Task BulkFix_WritesKnownSwimmerGenderIntoRows_AndSkipsUnknown()
    {
        // Массовая кнопка не гадает: она лишь дописывает в строки тот пол, который у пловца
        // уже есть. Пловец без пола остаётся человеку — иначе мы бы придумывали данные.
        var (db, _, swimmerId) = await SeedAsync(nameof(BulkFix_WritesKnownSwimmerGenderIntoRows_AndSkipsUnknown));
        await using var _d = db;

        var unknown = new Swimmer { LastName = "כהן", FirstName = "טל", BirthYear = 2011, Gender = "" };
        db.Swimmers.Add(unknown);
        await db.SaveChangesAsync();
        var known = await db.Swimmers.SingleAsync(s => s.Id == swimmerId);
        known.Gender = "female";

        var row = await db.Results.SingleAsync(r => r.Distance == "200");
        db.Results.Add(new ResultRecord
        {
            CompetitionId = row.CompetitionId, SwimmerId = unknown.Id, ClubId = row.ClubId, StyleId = row.StyleId,
            Distance = "50", Gender = "", TimeOriginal = "00:40.00", CompetitionDate = new DateTime(2025, 2, 1)
        });
        db.DataCheckFindings.Add(new DataCheckFinding
        {
            CheckId = "results.no-gender", Severity = 1, EntityType = "Result", EntityId = 3,
            Message = "כהן טל · 50 freestyle",
            FixKind = DataCheckFixKinds.SwimmerGender, FixEntityId = unknown.Id
        });
        await db.SaveChangesAsync();

        var (findings, rows) = await new DataCheckRunner(db, []).FixAllKnownSwimmerGendersAsync();

        Assert.Equal(1, findings);
        Assert.Equal(1, rows);
        Assert.Equal("female", (await db.Results.SingleAsync(r => r.Distance == "200")).Gender);
        Assert.Equal("", (await db.Results.SingleAsync(r => r.Distance == "50")).Gender);
    }

    /// <summary>Проверка-заглушка: реестр показывает находки только зарегистрированных проверок.</summary>
    private sealed class FakeNoGenderCheck : Swimm.Application.Abstractions.IDataCheck
    {
        public string Id => "results.no-gender";
        public string Title => "Результаты без пола";
        public string Description => "—";
        public DataCheckSeverity Severity => DataCheckSeverity.Warning;
        public Task<DataCheckOutcome> RunAsync(CancellationToken ct = default) =>
            Task.FromResult(DataCheckOutcome.Empty);
    }

    [Fact]
    public async Task RejectsGarbageGender()
    {
        var (db, findingId, _) = await SeedAsync(nameof(RejectsGarbageGender));
        await using var _ = db;

        Assert.Null(await new DataCheckRunner(db, []).FixSwimmerGenderAsync(findingId, "none"));
    }

    [Fact]
    public async Task RejectsFindingWithoutThisFix()
    {
        // У находки другой проверки нет FixKind — кнопки там и не будет, но эндпоинт
        // обязан отказать, а не молча поправить кого-то.
        var (db, _, _) = await SeedAsync(nameof(RejectsFindingWithoutThisFix));
        await using var _d = db;
        var other = new DataCheckFinding
        {
            CheckId = "relays.empty", Severity = 1, EntityType = "Relay", EntityId = 5, Message = "x"
        };
        db.DataCheckFindings.Add(other);
        await db.SaveChangesAsync();

        Assert.Null(await new DataCheckRunner(db, []).FixSwimmerGenderAsync(other.Id, "male"));
    }
}

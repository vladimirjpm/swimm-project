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

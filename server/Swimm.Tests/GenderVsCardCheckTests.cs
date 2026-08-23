using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Проверка `results.gender-vs-card` и её исправление «выровнять пол».
///
/// Пол человека живёт в ОДНОМ месте — в карточке пловца; в строке результата он законен
/// только у эстафет (пол команды) и как пол зачёта смешанного заплыва. На живой базе копии
/// разошлись у 64 пловцов, причём В ОБЕ СТОРОНЫ: у 46 врала карточка (женские имена с полом
/// male и ни одного мужского заплыва), у 18 — единичные строки одного протокола. Поэтому
/// находка не решает за человека, а показывает расклад и даёт выбрать.
/// </summary>
public class GenderVsCardCheckTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<(Swimmer Swimmer, Club Club, Style Style, Competition Comp)> SeedRefsAsync(
        SwimmDbContext db, string cardGender)
    {
        var swimmer = new Swimmer { LastName = "טנא", FirstName = "יהלי", BirthYear = 2011, Gender = cardGender };
        var club = new Club { Name = "Club" };
        var style = new Style { Name = "freestyle" };
        var comp = new Competition { Name = "Meet", Date = "01/02/2026", PoolType = "25m" };
        db.AddRange(swimmer, club, style, comp);
        await db.SaveChangesAsync();
        return (swimmer, club, style, comp);
    }

    private static ResultRecord Swim(
        Swimmer swimmer, Club club, Style style, Competition comp, string gender,
        string distance = "50", int? relayId = null) => new()
    {
        CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
        Distance = distance, Gender = gender, TimeOriginal = "00:31.00",
        TimeMillisecond = 31_000, RelayId = relayId,
        CompetitionDate = new DateTime(2026, 2, 1),
    };

    [Fact]
    public async Task FindsSwimmerWhoseRowsDisagreeWithCard()
    {
        await using var db = CreateDb(nameof(FindsSwimmerWhoseRowsDisagreeWithCard));
        var (swimmer, club, style, comp) = await SeedRefsAsync(db, "male");
        db.Results.AddRange(
            Swim(swimmer, club, style, comp, "male", "50"),
            Swim(swimmer, club, style, comp, "male", "100"),
            Swim(swimmer, club, style, comp, "female", "200")); // ошибка в шапке протокола
        await db.SaveChangesAsync();

        var outcome = await new GenderVsCardCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
        var item = Assert.Single(outcome.Items);
        Assert.Equal("Swimmer", item.EntityType);
        Assert.Equal(swimmer.Id, item.EntityId);
        Assert.Contains("1 из 3", item.Message);
        Assert.Equal(DataCheckFixKinds.SwimmerGenderAlign, item.FixKind);
    }

    [Fact]
    public async Task IgnoresRelayRows()
    {
        // У эстафеты пол команды, а не пловца: мужчина в женской эстафете — не расхождение.
        await using var db = CreateDb(nameof(IgnoresRelayRows));
        var (swimmer, club, style, comp) = await SeedRefsAsync(db, "male");
        db.Results.AddRange(
            Swim(swimmer, club, style, comp, "male", "50"),
            Swim(swimmer, club, style, comp, "female", "4X50", relayId: 7));
        await db.SaveChangesAsync();

        Assert.Equal(0, (await new GenderVsCardCheck(db).RunAsync()).Total);
    }

    [Fact]
    public async Task TreatsShortAndLongSpellingAsSameGender()
    {
        // Пол в базе живёт как male/female и как M/F: «M» в карточке против «male» в строках
        // это одно и то же, и находкой быть не должно.
        await using var db = CreateDb(nameof(TreatsShortAndLongSpellingAsSameGender));
        var (swimmer, club, style, comp) = await SeedRefsAsync(db, "M");
        db.Results.AddRange(
            Swim(swimmer, club, style, comp, "male", "50"),
            Swim(swimmer, club, style, comp, "male", "100"));
        await db.SaveChangesAsync();

        Assert.Equal(0, (await new GenderVsCardCheck(db).RunAsync()).Total);
    }

    [Fact]
    public async Task AllRowsAgainstCard_ReadsAsCardBeingWrong()
    {
        // Второй сценарий: врёт карточка. Текст находки обязан это различать — лечение
        // противоположное (там правим карточку, тут строки).
        await using var db = CreateDb(nameof(AllRowsAgainstCard_ReadsAsCardBeingWrong));
        var (swimmer, club, style, comp) = await SeedRefsAsync(db, "male");
        db.Results.AddRange(
            Swim(swimmer, club, style, comp, "female", "50"),
            Swim(swimmer, club, style, comp, "female", "100"));
        await db.SaveChangesAsync();

        var item = Assert.Single((await new GenderVsCardCheck(db).RunAsync()).Items);
        Assert.Contains("все 2 заплывов", item.Message);
    }

    [Fact]
    public async Task Align_WritesGenderToCardAndAllPersonalRows()
    {
        await using var db = CreateDb(nameof(Align_WritesGenderToCardAndAllPersonalRows));
        var (swimmer, club, style, comp) = await SeedRefsAsync(db, "male");
        db.Results.AddRange(
            Swim(swimmer, club, style, comp, "male", "50"),
            Swim(swimmer, club, style, comp, "female", "200"),
            Swim(swimmer, club, style, comp, "female", "4X50", relayId: 7));
        db.DataCheckFindings.Add(new DataCheckFinding
        {
            CheckId = "results.gender-vs-card", Severity = 1, EntityType = "Swimmer", EntityId = swimmer.Id,
            Message = "טנא יהלי", SubjectName = "טנא יהלי",
            FixKind = DataCheckFixKinds.SwimmerGenderAlign, FixEntityId = swimmer.Id,
        });
        await db.SaveChangesAsync();
        var findingId = await db.DataCheckFindings.Select(f => f.Id).SingleAsync();

        var rows = await new DataCheckRunner(db, []).AlignSwimmerGenderAsync(findingId, "male");

        Assert.Equal(1, rows); // переписана одна личная строка…
        Assert.Equal("male", (await db.Swimmers.SingleAsync()).Gender);
        Assert.All(await db.Results.Where(r => r.RelayId == null).ToListAsync(),
            r => Assert.Equal("male", r.Gender));
        // …а эстафетная осталась как есть: там пол команды.
        Assert.Equal("female", (await db.Results.SingleAsync(r => r.RelayId != null)).Gender);
    }

    [Fact]
    public async Task Align_RejectsWrongFindingKindAndBadGender()
    {
        await using var db = CreateDb(nameof(Align_RejectsWrongFindingKindAndBadGender));
        var (swimmer, _, _, _) = await SeedRefsAsync(db, "male");
        db.DataCheckFindings.Add(new DataCheckFinding
        {
            CheckId = "results.no-gender", Severity = 1, EntityType = "Result", EntityId = 1,
            Message = "x", FixKind = DataCheckFixKinds.SwimmerGender, FixEntityId = swimmer.Id,
        });
        await db.SaveChangesAsync();
        var findingId = await db.DataCheckFindings.Select(f => f.Id).SingleAsync();

        var runner = new DataCheckRunner(db, []);
        Assert.Null(await runner.AlignSwimmerGenderAsync(findingId, "male"));   // чужой вид находки
        Assert.Null(await runner.AlignSwimmerGenderAsync(findingId, "none"));   // пол не пол
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сшивка заявок с результатами (docs/plans/start-list-plan.md, шаг С9): после импорта
/// протокола каждая заявка либо получает свой результат, либо становится неявкой.
/// Ради этого числа заявки и не стираются после соревнования.
/// </summary>
public class StartListStitchServiceTests
{
    private const int OrgCompId = 16786;

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static StartListStitchService Service(SwimmDbContext db) =>
        new(db, NullLogger<StartListStitchService>.Instance);

    private static async Task<SwimmDbContext> SeedAsync(
        string name, bool withCompetition = true, string date = "19/02/2026")
    {
        var db = CreateDb(name);
        db.AddRange(
            new Club { Id = 1, Name = "Клуб" },
            new Style { Id = 1, Name = "freestyle" },
            new Swimmer { Id = 10, LastName = "Первый", FirstName = "П", BirthYear = 2016 },
            new Swimmer { Id = 11, LastName = "Второй", FirstName = "В", BirthYear = 2016 });

        if (withCompetition)
            db.Competitions.Add(new Competition
            {
                Id = 100, Name = "Чемпионат", Date = date, PoolType = "25m", OrgCompId = OrgCompId
            });

        await db.SaveChangesAsync();
        return db;
    }

    private static CompetitionEntry Entry(
        long id, int swimmer, int heat, int lane, string dist = "50",
        DateTime? day = null) =>
        new()
        {
            Id = id,
            OrgCompId = OrgCompId,
            CompDate = day ?? new DateTime(2026, 2, 19),
            CompName = "Чемпионат",
            OrgDisciplineId = 76321,
            SwimmerId = swimmer,
            ClubId = 1,
            StyleId = 1,
            Distance = dist,
            Gender = "female",
            Heat = heat,
            Lane = lane,
            SeedTimeOriginal = string.Empty
        };

    private static ResultRecord Result(long id, int swimmer, int heat, int lane, string dist = "50") =>
        new()
        {
            Id = id,
            CompetitionId = 100,
            SwimmerId = swimmer,
            ClubId = 1,
            StyleId = 1,
            Distance = dist,
            Gender = "female",
            CompetitionDate = new DateTime(2026, 2, 19),
            Heat = heat,
            Lane = lane,
            TimeMillisecond = 30000,
            TimeOriginal = "00:30.00"
        };

    [Fact]
    public async Task Stitch_LinksEntryToItsDayAndResult()
    {
        await using var db = await SeedAsync(nameof(Stitch_LinksEntryToItsDayAndResult));
        db.CompetitionEntries.Add(Entry(1, swimmer: 10, heat: 2, lane: 5));
        db.Results.Add(Result(500, swimmer: 10, heat: 2, lane: 5));
        await db.SaveChangesAsync();

        var report = await Service(db).StitchAsync(OrgCompId);

        Assert.Equal(1, report.Linked);
        Assert.Equal(1, report.Swum);
        Assert.Equal(0, report.NoShow);

        var entry = await db.CompetitionEntries.SingleAsync();
        Assert.Equal(100, entry.CompetitionId);   // до импорта дня не было и быть не могло
        Assert.Equal(500, entry.ResultId);
        Assert.Equal(CompetitionEntryStatus.Swum, entry.Status);
    }

    [Fact]
    public async Task Stitch_EntryWithoutResult_BecomesNoShow()
    {
        // Неявка дня старта — единственный ответ на «почему в протоколе меньше, чем заявлено».
        await using var db = await SeedAsync(nameof(Stitch_EntryWithoutResult_BecomesNoShow));
        db.CompetitionEntries.AddRange(
            Entry(1, swimmer: 10, heat: 1, lane: 3),
            Entry(2, swimmer: 11, heat: 1, lane: 4));
        db.Results.Add(Result(500, swimmer: 10, heat: 1, lane: 3));
        await db.SaveChangesAsync();

        var report = await Service(db).StitchAsync(OrgCompId);

        Assert.Equal(1, report.Swum);
        Assert.Equal(1, report.NoShow);

        var noShow = await db.CompetitionEntries.SingleAsync(e => e.SwimmerId == 11);
        Assert.Equal(CompetitionEntryStatus.NoShow, noShow.Status);
        Assert.Null(noShow.ResultId);
    }

    [Fact]
    public async Task Stitch_ReseatedOnTheDay_StillFindsItsResult()
    {
        // Снятия сдвигают посев уже в день старта: дорожка в протоколе не та, что в заявке.
        // Мягкий проход «тот же пловец в той же дисциплине» это ловит.
        await using var db = await SeedAsync(nameof(Stitch_ReseatedOnTheDay_StillFindsItsResult));
        db.CompetitionEntries.Add(Entry(1, swimmer: 10, heat: 1, lane: 3));
        db.Results.Add(Result(500, swimmer: 10, heat: 2, lane: 7));
        await db.SaveChangesAsync();

        var report = await Service(db).StitchAsync(OrgCompId);

        Assert.Equal(1, report.Swum);
        Assert.Equal(1, report.MatchedByDiscipline);   // шов «мягкий» — он виден в отчёте
        Assert.Equal(500, (await db.CompetitionEntries.SingleAsync()).ResultId);
    }

    [Fact]
    public async Task Stitch_PrelimAndFinal_AreNotGuessed()
    {
        // У дисциплины бывают предварительные и финал: два результата одного пловца.
        // Приписать заявку не тому заплыву значило бы соврать про исход старта.
        await using var db = await SeedAsync(nameof(Stitch_PrelimAndFinal_AreNotGuessed));
        db.CompetitionEntries.Add(Entry(1, swimmer: 10, heat: 1, lane: 3));
        db.Results.AddRange(
            Result(500, swimmer: 10, heat: 5, lane: 1),
            Result(501, swimmer: 10, heat: 6, lane: 2));
        await db.SaveChangesAsync();

        var report = await Service(db).StitchAsync(OrgCompId);

        Assert.Equal(0, report.MatchedByDiscipline);
        Assert.Equal(1, report.NoShow);   // честнее «не знаю», чем угаданная связь
    }

    [Fact]
    public async Task Stitch_RelayLegs_KeepTheirOwnResults()
    {
        // Четыре ноги делят заплыв и дорожку — различает их только пловец.
        await using var db = await SeedAsync(nameof(Stitch_RelayLegs_KeepTheirOwnResults));
        db.CompetitionEntries.AddRange(
            Entry(1, swimmer: 10, heat: 1, lane: 3, dist: "4X50"),
            Entry(2, swimmer: 11, heat: 1, lane: 3, dist: "4X50"));
        db.Results.AddRange(
            Result(500, swimmer: 10, heat: 1, lane: 3, dist: "4x50"),   // регистр у источников разный
            Result(501, swimmer: 11, heat: 1, lane: 3, dist: "4X50"));
        await db.SaveChangesAsync();

        var report = await Service(db).StitchAsync(OrgCompId);

        Assert.Equal(2, report.Swum);
        var ids = await db.CompetitionEntries.OrderBy(e => e.Id).Select(e => e.ResultId).ToListAsync();
        Assert.Equal([500L, 501L], ids);
    }

    [Fact]
    public async Task Stitch_BeforeImport_LeavesEntriesAlone()
    {
        // Протокола ещё нет — заявки остаются «entered». Объявить их неявками значило бы
        // сказать «не приплыл» про соревнование, которое ещё даже не состоялось.
        await using var db = await SeedAsync(nameof(Stitch_BeforeImport_LeavesEntriesAlone), withCompetition: false);
        db.CompetitionEntries.Add(Entry(1, swimmer: 10, heat: 1, lane: 3));
        await db.SaveChangesAsync();

        var report = await Service(db).StitchAsync(OrgCompId);

        Assert.Equal(0, report.Days);
        Assert.Equal(1, report.Unlinked);
        var entry = await db.CompetitionEntries.SingleAsync();
        Assert.Equal(CompetitionEntryStatus.Entered, entry.Status);
        Assert.Null(entry.CompetitionId);
    }

    [Fact]
    public async Task Stitch_SingleDay_TolerantToDateDrift()
    {
        // У однодневного старта дата заявки и дата протокола расходятся: источник и файл
        // печатают её по-разному. День один — спорить не о чем.
        await using var db = await SeedAsync(nameof(Stitch_SingleDay_TolerantToDateDrift), date: "20/02/2026");
        db.CompetitionEntries.Add(Entry(1, swimmer: 10, heat: 1, lane: 3, day: new DateTime(2026, 2, 19)));
        db.Results.Add(Result(500, swimmer: 10, heat: 1, lane: 3));
        await db.SaveChangesAsync();

        var report = await Service(db).StitchAsync(OrgCompId);

        Assert.Equal(1, report.Linked);
        Assert.Equal(0, report.Unlinked);
    }

    [Fact]
    public async Task Stitch_IsIdempotent()
    {
        await using var db = await SeedAsync(nameof(Stitch_IsIdempotent));
        db.CompetitionEntries.Add(Entry(1, swimmer: 10, heat: 2, lane: 5));
        db.Results.Add(Result(500, swimmer: 10, heat: 2, lane: 5));
        await db.SaveChangesAsync();

        await Service(db).StitchAsync(OrgCompId);
        var second = await Service(db).StitchAsync(OrgCompId);

        Assert.Equal(1, second.Swum);
        Assert.Equal(0, second.NoShow);
        Assert.Equal(500, (await db.CompetitionEntries.SingleAsync()).ResultId);
    }

    [Fact]
    public async Task StitchCompetitions_ResolvesOrgCompIdFromTheDay()
    {
        // Точка вызова из конца импорта знает только id дней справочника.
        await using var db = await SeedAsync(nameof(StitchCompetitions_ResolvesOrgCompIdFromTheDay));
        db.CompetitionEntries.Add(Entry(1, swimmer: 10, heat: 2, lane: 5));
        db.Results.Add(Result(500, swimmer: 10, heat: 2, lane: 5));
        await db.SaveChangesAsync();

        var reports = await Service(db).StitchCompetitionsAsync([100]);

        var report = Assert.Single(reports);
        Assert.Equal(OrgCompId, report.OrgCompId);
        Assert.Equal(1, report.Swum);
    }

    [Fact]
    public async Task Stitch_UnknownCompetition_IsQuiet()
    {
        await using var db = await SeedAsync(nameof(Stitch_UnknownCompetition_IsQuiet));

        var report = await Service(db).StitchAsync(999);

        Assert.Equal(0, report.Entries);
        Assert.Equal(0, report.NoShow);
    }
}

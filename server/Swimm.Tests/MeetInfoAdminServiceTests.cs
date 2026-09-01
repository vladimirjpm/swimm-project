using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Редактор справки о старте (docs/plans/start-list-ticket-plan.md, шаг Т1): разминка руками
/// + переопределение флага «чемпионат».
/// </summary>
public class MeetInfoAdminServiceTests
{
    private const int OrgCompId = 16786;

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<SwimmDbContext> SeedEntriesAsync(string name)
    {
        var db = CreateDb(name);
        db.Clubs.Add(new Club { Id = 1, Name = "Клуб" });
        db.Styles.Add(new Style { Id = 1, Name = "freestyle" });
        db.Swimmers.Add(new Swimmer { Id = 10, LastName = "Ф", FirstName = "И", BirthYear = 2016 });
        db.CompetitionEntries.AddRange(
            Entry(1, new DateTime(2026, 2, 19)),
            Entry(2, new DateTime(2026, 2, 19)),
            Entry(3, new DateTime(2026, 2, 20)));
        await db.SaveChangesAsync();
        return db;
    }

    private static CompetitionEntry Entry(long id, DateTime day) => new()
    {
        Id = id,
        OrgCompId = OrgCompId,
        CompDate = day,
        CompName = "Чемпионат",
        OrgDisciplineId = 76321,
        SwimmerId = 10,
        ClubId = 1,
        StyleId = 1,
        Distance = "50",
        Gender = "female",
        Heat = 1,
        Lane = (int)id,
        Round = "timed-final",
        PulledAt = new DateTime(2026, 2, 18, 20, 0, 0, DateTimeKind.Utc)
    };

    private static MeetInfoSaveRequest Save(bool? champ, params (DateTime Date, string? Time)[] days) =>
        new(champ, days.Select(d => new MeetInfoDaySaveDto(d.Date, d.Time)).ToList());

    [Fact]
    public async Task Get_DaysComeFromEntries_WithCounts()
    {
        await using var db = await SeedEntriesAsync(nameof(Get_DaysComeFromEntries_WithCounts));

        var info = await new MeetInfoAdminService(db).GetAsync(OrgCompId);

        Assert.NotNull(info);
        Assert.Equal("Чемпионат", info!.CompName);
        Assert.Equal(2, info.Days.Count);
        Assert.Equal(2, info.Days[0].Entries);
        Assert.Equal(1, info.Days[1].Entries);
        Assert.All(info.Days, d => Assert.Null(d.WarmUpLocal));
        Assert.False(info.ChampionshipEffective);
    }

    /// <summary>
    /// Разминку вводят ДО забора протокола: админ читает регламент, когда заявок ещё нет.
    /// Дни тогда берутся из диапазона дат «Входящих».
    /// </summary>
    [Fact]
    public async Task Get_WithoutEntries_DaysComeFromDiscoveredRange()
    {
        await using var db = CreateDb(nameof(Get_WithoutEntries_DaysComeFromDiscoveredRange));
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = OrgCompId,
            Name = "Будущий старт",
            DateStart = new DateTime(2026, 2, 19),
            DateEnd = new DateTime(2026, 2, 21)
        });
        await db.SaveChangesAsync();

        var info = await new MeetInfoAdminService(db).GetAsync(OrgCompId);

        Assert.Equal("Будущий старт", info!.CompName);
        Assert.Equal(3, info.Days.Count);
        Assert.All(info.Days, d => Assert.Equal(0, d.Entries));
    }

    [Fact]
    public async Task Get_UnknownCompetition_IsNull()
    {
        await using var db = CreateDb(nameof(Get_UnknownCompetition_IsNull));

        Assert.Null(await new MeetInfoAdminService(db).GetAsync(999));
    }

    /// <summary>
    /// Админ вводит СТЕННЫЕ часы из регламента, в базу идёт момент времени. В феврале
    /// Израиль на UTC+2, значит 08:00 местного — это 06:00 UTC; обратно в форму приходит
    /// снова 08:00. Хранить «часы» вместо момента нельзя: витрина показывает время в поясе
    /// браузера, рядом со временем заплывов, которое уже UTC.
    /// </summary>
    [Fact]
    public async Task Save_WarmUpConvertsLocalToUtc_AndBack()
    {
        await using var db = await SeedEntriesAsync(nameof(Save_WarmUpConvertsLocalToUtc_AndBack));

        var info = await new MeetInfoAdminService(db)
            .SaveAsync(OrgCompId, Save(null, (new DateTime(2026, 2, 19), "08:00")));

        Assert.Equal("08:00", info!.Days[0].WarmUpLocal);
        Assert.Null(info.Days[1].WarmUpLocal);

        var stored = await db.CompetitionWarmUps.SingleAsync();
        Assert.Equal(new DateTime(2026, 2, 19, 6, 0, 0, DateTimeKind.Utc), stored.WarmUpAt.ToUniversalTime());
        Assert.Equal(DateTimeKind.Unspecified, stored.Date.Kind);
    }

    /// <summary>Пустое поле — «стереть»: иначе однажды введённое время нечем убрать.</summary>
    [Fact]
    public async Task Save_EmptyTime_RemovesWarmUp()
    {
        await using var db = await SeedEntriesAsync(nameof(Save_EmptyTime_RemovesWarmUp));
        var service = new MeetInfoAdminService(db);
        var day = new DateTime(2026, 2, 19);

        await service.SaveAsync(OrgCompId, Save(null, (day, "08:00")));
        var info = await service.SaveAsync(OrgCompId, Save(null, (day, "")));

        Assert.Null(info!.Days[0].WarmUpLocal);
        Assert.Equal(0, await db.CompetitionWarmUps.CountAsync());
    }

    /// <summary>
    /// Ручная правка живёт в своём поле и НЕ трогает то, что определил забор: перезабор
    /// перепишет <c>IsChampionship</c> и обязан оставить решение админа на месте.
    /// </summary>
    [Fact]
    public async Task Save_OverrideDoesNotTouchPulledFlag()
    {
        await using var db = await SeedEntriesAsync(nameof(Save_OverrideDoesNotTouchPulledFlag));
        db.CompetitionMeetInfos.Add(new CompetitionMeetInfo
        {
            OrgCompId = OrgCompId,
            IsChampionship = true,
            RegulationUrl = "https://loglig/reg.pdf"
        });
        await db.SaveChangesAsync();

        var info = await new MeetInfoAdminService(db).SaveAsync(OrgCompId, Save(false));

        Assert.True(info!.IsChampionship);            // что определил забор — не тронуто
        Assert.False(info.IsChampionshipOverride);    // решение админа
        Assert.False(info.ChampionshipEffective);     // витрина показывает решение админа
        Assert.Equal("https://loglig/reg.pdf", info.RegulationUrl);
    }

    /// <summary>Мусор во времени не должен ронять сохранение — день просто остаётся пустым.</summary>
    [Fact]
    public async Task Save_UnparsableTime_LeavesDayEmpty()
    {
        await using var db = await SeedEntriesAsync(nameof(Save_UnparsableTime_LeavesDayEmpty));

        var info = await new MeetInfoAdminService(db)
            .SaveAsync(OrgCompId, Save(null, (new DateTime(2026, 2, 19), "не время")));

        Assert.Null(info!.Days[0].WarmUpLocal);
        Assert.Equal(0, await db.CompetitionWarmUps.CountAsync());
    }
}

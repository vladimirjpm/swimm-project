using Microsoft.EntityFrameworkCore;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пара-очки (Р67, docs/data-integrity.md): loglig пара-пловцам в общем заплыве считает очки по
/// шкале класса (бугрим-2026: 1:22.76 на 100 в/с → 931), и они забирали Best swim чемпионата.
/// Детектор находит их по соседям по дисциплине, без таблиц базовых времён FINA.
/// </summary>
public class ParaPointsDetectorTests
{
    /// <summary>FINA-очки ровно по формуле 1000·(B/T)³ с базой 47.00 — как их даёт источник.</summary>
    private static int Fina(int ms, double baseSec = 47.0) => (int)Math.Round(1000 * Math.Pow(baseSec / (ms / 1000.0), 3));

    private static ParaPointsDetector.Row R(long id, int swimmer, int ms, int? points = null, string gender = "male",
        string? category = "14-99", int comp = 1, int style = 1, string distance = "100", bool relay = false, bool fail = false) =>
        new(id, comp, swimmer, style, distance, gender, category, points ?? Fina(ms), ms, fail, relay);

    /// <summary>Обычный заплыв 100 в/с: пять честных строк — эталон.</summary>
    private static List<ParaPointsDetector.Row> Heat() =>
    [
        R(1, 101, 54_590), R(2, 102, 55_060), R(3, 103, 56_000), R(4, 104, 58_300), R(5, 105, 61_200),
    ];

    [Fact]
    public void HonestHeat_NothingFlagged()
    {
        Assert.Empty(ParaPointsDetector.Detect(Heat()));
    }

    [Fact]
    public void ParaPointsInOpenHeat_FlagsAllSwimsOfThatSwimmerThatDay()
    {
        var rows = Heat();
        rows.Add(R(6, 200, 82_760, points: 931));                                   // бугрим: 931 за 1:22.76
        rows.Add(R(7, 200, 150_000, points: 24, style: 2, distance: "200"));       // его же — пара-шкала «ниже нормы»
        rows.Add(R(8, 200, 60_000, points: 900, comp: 2));                         // другой день — не этот сигнал

        var para = ParaPointsDetector.Detect(rows);

        Assert.Contains(6L, para);
        Assert.Contains(7L, para);       // пара — свойство пловца на соревновании, не заплыва
        Assert.DoesNotContain(8L, para); // на другом дне у него своя проверка (там группы нет)
        Assert.Equal(2, para.Count);
    }

    [Fact]
    public void ParaProgramme_FlaggedWithoutMath_AndNotPartOfReference()
    {
        var rows = Heat();
        rows.Add(R(6, 300, 79_640, points: 994, category: "para"));
        rows.Add(R(7, 301, 90_000, points: 100, category: "PARA"));

        var para = ParaPointsDetector.Detect(rows);

        Assert.Equal(new HashSet<long> { 6, 7 }, para);
    }

    [Fact]
    public void SmallGroup_NotTrusted()
    {
        var rows = new List<ParaPointsDetector.Row>
        {
            R(1, 101, 54_590), R(2, 102, 55_060), R(3, 103, 56_000), R(4, 200, 82_760, points: 931),
        };

        Assert.Empty(ParaPointsDetector.Detect(rows));
    }

    [Fact]
    public void MixedGroupsAndRelays_Skipped()
    {
        // Смешанная группа без пола: у мужчин и женщин разные базы, разброс естественный.
        var mixed = Heat().Select(r => r with { Gender = "", EventCategory = "mix-0" }).ToList();
        mixed.Add(R(6, 200, 82_760, points: 931, gender: "", category: "mix-0"));
        Assert.Empty(ParaPointsDetector.Detect(mixed));

        var rows = Heat();
        rows.Add(R(6, 200, 82_760, points: 931));
        rows.Add(R(7, 200, 240_000, points: 700, style: 5, distance: "400", relay: true));  // эстафета — очки команды
        Assert.DoesNotContain(7L, ParaPointsDetector.Detect(rows));
    }

    [Fact]
    public void ProtocolTimeError_NotPara()
    {
        // 400 в/с за 0:44.93 (И-ошибка протокола, 1512): источник посчитал очки ОТ неверного
        // времени, произведение «очки × время³» то же — это не пара, а забота проверок качества.
        var rows = Heat().Select(r => r with { Distance = "400" }).ToList();
        rows.Add(R(6, 200, 44_930, distance: "400"));

        Assert.Empty(ParaPointsDetector.Detect(rows));
    }

    [Fact]
    public void FailedOrPointlessRows_NotReference()
    {
        var rows = Heat();
        rows.Add(R(6, 200, 82_760, points: 931, fail: true));  // TimeFail — сигналом не считается
        rows.Add(R(7, 201, 82_760, points: 0));

        Assert.Empty(ParaPointsDetector.Detect(rows));
    }

    [Fact]
    public void ResultPointsFina_ZeroForPara()
    {
        Assert.Equal(0, ResultPoints.Fina(931, isParaPoints: true));
        Assert.Equal(614, ResultPoints.Fina(614, isParaPoints: false));
    }

    // ── Пересчёт: флаг производный — ставится и снимается по данным ────────────────

    [Fact]
    public async Task RecalculateAll_SetsAndClearsFlag()
    {
        await using var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(nameof(RecalculateAll_SetsAndClearsFlag)).Options);
        var comp = new Competition { Name = "Bugrim", Date = "23/05/2026", PoolType = "50m" };
        var club = new Club { Name = "C" };
        var style = new Style { Name = "freestyle" };
        db.AddRange(comp, club, style);
        await db.SaveChangesAsync();

        ResultRecord Row(int swimmerId, int ms, int points) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmerId, ClubId = club.Id, StyleId = style.Id,
            Distance = "100", Gender = "male", CompetitionDate = new DateTime(2026, 5, 23), EventCategory = "14-99",
            TimeMillisecond = ms, TimeOriginal = "", TimeSplit = "", InternationalPoints = points,
        };
        var honest = new[] { 54_590, 55_060, 56_000, 58_300, 61_200 }.Select((ms, i) => Row(100 + i, ms, Fina(ms))).ToList();
        var para = Row(200, 82_760, 931);
        var wronglyFlagged = honest[0];
        wronglyFlagged.IsParaPoints = true;  // старое состояние, которое данные уже не подтверждают
        db.Results.AddRange(honest);
        db.Results.Add(para);
        await db.SaveChangesAsync();

        var svc = new CompetitionRecalculationService(db, Mock.Of<IClubStandingService>());
        var changed = await svc.RecalculateAllParaPointsAsync();

        Assert.Equal(2, changed);
        db.ChangeTracker.Clear();
        Assert.True((await db.Results.SingleAsync(r => r.Id == para.Id)).IsParaPoints);
        Assert.False((await db.Results.SingleAsync(r => r.Id == wronglyFlagged.Id)).IsParaPoints);
        Assert.Equal(0, await svc.RecalculateAllParaPointsAsync());  // повторный прогон ничего не меняет
    }
}

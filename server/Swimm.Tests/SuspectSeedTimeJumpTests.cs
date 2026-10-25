using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Правило «заплыв намного быстрее заявок» (<see cref="SuspectReasons.SeedTimeJump"/>, И-55).
///
/// Эталон — comp 1772 (23.10.2026), 200 вольным девушки 14-15, заплыв 1: сбой хронометража
/// целого заплыва. Заявки дорожек 2–6 — 2:26–2:27, в протоколе 1:44–2:06. Автоматически тогда
/// пометилась одна строка (быстрее мирового рекорда), остальные четыре дали ложный возрастной
/// рекорд и Best swim. Дорожки 1 и 7 того же заплыва (−5 и −8 с) — честные.
/// </summary>
public class SuspectSeedTimeJumpTests
{
    private static readonly DateTime Day = new(2026, 10, 23);

    /// <summary>Мировые рекорды короткой воды — настоящие значения, бассейн 1772 — 25 м.</summary>
    private static readonly WorldBestReference Wr = WorldBestReference.Build(
    [
        ("female", "freestyle", "200m", "25m", "01:49.36"),
        ("male", "butterfly", "50m", "25m", "21.32"),
    ]);

    private static SuspectCandidateRow Row(
        long id, int ms, int? seedMs, string heatKey = "85282/1",
        string style = "freestyle", string distance = "200", string gender = "female")
        => new(id, SwimmerId: (int)id, style, distance, gender, ms, Day, IsRelay: false, TimeFail: false,
            AgeGroup: "15-16", Heat: 1, PoolType: "25m", SeedTimeMs: seedMs, EntryHeatKey: heatKey);

    /// <summary>Заплыв 1 из протокола 1772 — цифры с прода как есть, Id строки = дорожка.</summary>
    private static List<SuspectCandidateRow> Heat1772() =>
    [
        Row(1, 143_810, 148_760), // дорожка 1: −3.3% — честно
        Row(2, 120_100, 147_310), // −18.5%
        Row(3, 126_100, 146_710), // −14.0% — самая слабая из сломанных, на ней порог
        Row(4, 114_380, 146_400), // −21.9%
        Row(5, 117_160, 146_600), // −20.1%
        Row(6, 104_660, 147_160), // −28.9%, к тому же быстрее мирового рекорда 1:49.36
        Row(7, 140_350, 148_630), // дорожка 7: −5.6% — честно
    ];

    [Fact]
    public void Reference1772_BrokenLanesFlagged_HonestLanesNot()
    {
        var verdicts = SuspectResultDetector.Detect(Heat1772(), worldBests: Wr).ToDictionary(v => v.ResultId);

        foreach (var lane in new long[] { 2, 3, 4, 5 })
            Assert.Equal(SuspectReasons.SeedTimeJump, verdicts[lane].Reason);
        // Шестая быстрее мирового рекорда — более надёжная причина побеждает.
        Assert.Equal(SuspectReasons.TimeVsDistance, verdicts[6].Reason);
        Assert.False(verdicts.ContainsKey(1));
        Assert.False(verdicts.ContainsKey(7));

        var note = verdicts[3].Note;
        Assert.Contains("02:06.10 против заявочного 02:26.71 (−14.0%)", note);
        Assert.Contains("у 4 других дорожек заплыва", note);
    }

    [Fact]
    public void SingleLaneJump_NotFlagged()
    {
        // Одна дорожка — скорее опечатка в сиде, чем сбой отсчёта; на проде одиночные скачки
        // 12.4–12.6% при сиде от 250 очков встречаются у честных пловцов.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 120_100, 147_310),
            Row(2, 143_810, 148_760),
            Row(3, 140_350, 148_630),
        };

        Assert.Empty(SuspectResultDetector.Detect(rows, worldBests: Wr));
    }

    [Fact]
    public void BeginnerSeeds_NotFlagged_EvenWhenSeveralLanesJump()
    {
        // Сиды новичков — прикидка: 50 баттерфляем 1:06.00 → 41.51 и 47.59 → 33.74 в одном
        // заплыве 1773 — живые дети, а не сбой. Уровень сида ~34 и ~92 очка, ниже 250.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 41_510, 66_000, "85344/3", "butterfly", "50", "male"),
            Row(2, 33_740, 47_590, "85344/3", "butterfly", "50", "male"),
        };

        Assert.Empty(SuspectResultDetector.Detect(rows, worldBests: Wr));
    }

    [Fact]
    public void SameHeatNumber_DifferentEvent_NotOneHeat()
    {
        // «Заплыв 1» у двух событий одной дисциплины в один день — разные заплывы.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 120_100, 147_310, "85282/1"),
            Row(2, 114_380, 146_400, "85283/1"),
        };

        Assert.Empty(SuspectResultDetector.Detect(rows, worldBests: Wr));
    }

    [Fact]
    public void NoSeed_IsSkipped()
    {
        // «NT» — сида нет, сравнивать не с чем; такая строка не считается второй дорожкой.
        var rows = new List<SuspectCandidateRow>
        {
            Row(1, 120_100, 147_310),
            Row(2, 114_380, null),
        };

        Assert.Empty(SuspectResultDetector.Detect(rows, worldBests: Wr));
    }

    [Fact]
    public void WithoutWorldRecords_RuleStaysSilent()
    {
        // Уровень сида меряется по мировому рекорду; нет рекорда — нет и уровня.
        Assert.DoesNotContain(SuspectResultDetector.Detect(Heat1772()),
            v => v.Reason == SuspectReasons.SeedTimeJump);
    }

    [Fact]
    public void Reason_IsAutomatic()
    {
        // Автоматическая причина пересчитывается и снимается сканом; ручная — нет.
        Assert.Contains(SuspectReasons.SeedTimeJump, SuspectReasons.Automatic);
    }

    /* ── Сервис: заявки приходят из CompetitionEntries.ResultId ──────────────────── */

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    [Fact]
    public async Task Scan_FlagsBrokenHeat_FromStitchedEntries()
    {
        await using var db = CreateDb(nameof(Scan_FlagsBrokenHeat_FromStitchedEntries));
        var comp = new Competition { Id = 1772, Name = "רודובסקי ונימרי", Date = "23/10/2026", PoolType = "25m" };
        var club = new Club { Id = 1, Name = "Club" };
        var free = new Style { Id = 1, Name = "freestyle" };
        db.AddRange(comp, club, free);
        db.Records.Add(new Swimm.Domain.Entities.Record
        {
            RegionType = "world", RegionCode = string.Empty, Category = "open", AgeKey = "open",
            Gender = "female", PoolType = "25m", Style = "freestyle", Distance = "200m", Time = "01:49.36",
            RecordDate = "01/01/2025",
        });

        // Без шестой дорожки (она быстрее мирового рекорда) — чтобы все находки были этим правилом.
        var lanes = new (int Lane, int Ms, int? Seed)[]
        {
            (1, 143_810, 148_760), (2, 120_100, 147_310), (3, 126_100, 146_710),
            (4, 114_380, 146_400), (5, 117_160, 146_600), (7, 140_350, 148_630),
        };
        foreach (var (lane, ms, seed) in lanes)
        {
            var swimmer = new Swimmer { Id = 100 + lane, FirstName = "ש", LastName = $"פ{lane}" };
            db.Swimmers.Add(swimmer);
            db.Results.Add(new ResultRecord
            {
                Id = lane, CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = free.Id,
                Distance = "200", Gender = "female", TimeMillisecond = ms, Heat = 1, Lane = lane,
                CompetitionDate = Day,
            });
            db.CompetitionEntries.Add(new CompetitionEntry
            {
                OrgCompId = 16854, CompetitionId = comp.Id, CompDate = Day, SwimmerId = swimmer.Id,
                ClubId = club.Id, StyleId = free.Id, Distance = "200", Gender = "female",
                OrgDisciplineId = 85282, Heat = 1, Lane = lane, SeedTimeMs = seed,
                ResultId = lane, Status = CompetitionEntryStatus.Swum,
            });
        }
        await db.SaveChangesAsync();

        var scan = await new SuspectResultService(db).ScanAsync(null, comp.Id);

        Assert.Equal(new long[] { 2, 3, 4, 5 },
            scan.Rows.Where(r => r.Reason == SuspectReasons.SeedTimeJump).Select(r => r.ResultId).OrderBy(x => x));
        Assert.Equal(4, scan.Flagged);
    }
}

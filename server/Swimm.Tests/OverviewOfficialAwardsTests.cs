using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Карточка «Official awards» (Overview): награды по регламенту серии турнира, по дням
/// (docs/plans/official-awards-plan.md §5.4). Арифметика наград — в SeriesAwardScoringTests;
/// здесь то, что делает сам обзор: какой день к какой части, какая версия регламента, сезонный
/// возраст, отбор строк.
/// </summary>
public class OverviewOfficialAwardsTests
{
    private sealed record Setup(CompetitionSeries Series, CompetitionSeriesPart Seniors, CompetitionSeriesPart Youth);

    /// <summary>Серия как у רודובסקי/נימרי: деньги взрослым (без полос, тройка) и кубки младшим (полосы).</summary>
    private static async Task<Setup> SeriesAsync(OverviewTestKit kit, DateOnly effectiveFrom, string moneyPrize = "1,000 ₪ + Cup")
    {
        var series = new CompetitionSeries { Name = "רודובסקי/נימרי", MatchPhrases = "רודובסקי ועדן נימרי" };
        var seniors = new CompetitionSeriesPart { Series = series, Name = "נוער ובוגרים", NameEn = "Juniors & Seniors", SortOrder = 1 };
        var youth = new CompetitionSeriesPart { Series = series, Name = "צעירים", NameEn = "Youth", SortOrder = 2 };
        kit.Db.AddRange(series, seniors, youth);
        await kit.Db.SaveChangesAsync();
        await AddRegulationAsync(kit, series, seniors, youth, effectiveFrom, moneyPrize);
        return new Setup(series, seniors, youth);
    }

    private static async Task AddRegulationAsync(OverviewTestKit kit, CompetitionSeries series,
        CompetitionSeriesPart seniors, CompetitionSeriesPart youth, DateOnly effectiveFrom, string moneyPrize)
    {
        kit.Db.Add(new SeriesRegulation
        {
            SeriesId = series.Id, EffectiveFrom = effectiveFrom, SourceUrl = "https://example.test/takanon.pdf",
            Awards =
            [
                new SeriesAward
                {
                    PartId = seniors.Id, Title = "Top juniors & seniors", CountBest = 3, Places = 3,
                    Prizes = [new SeriesAwardPrize { Place = 1, Text = moneyPrize }, new SeriesAwardPrize { Place = 2, Text = "500 ₪" }]
                },
                new SeriesAward
                {
                    PartId = youth.Id, Title = "Outstanding young swimmers", CountBest = 3, Places = 1,
                    Bands = [new SeriesAwardBand { Gender = "female", MinSeasonAge = 10, MaxSeasonAge = 11 }],
                    Prizes = [new SeriesAwardPrize { Place = 1, Text = "Cup" }]
                },
            ]
        });
        await kit.Db.SaveChangesAsync();
    }

    private static Task<Competition> DayAsync(OverviewTestKit kit, CompetitionSeriesPart? part, string date,
        int? eventId = null, int? dayNumber = null) =>
        kit.AddCompetitionAsync(c =>
        {
            c.Name = "Meet " + date;
            c.Date = date;
            c.SeriesPartId = part?.Id;
            c.EventId = eventId;
            c.DayNumber = dayNumber;
        });

    [Fact]
    public async Task Event_TwoDaysInDifferentParts_GiveTwoBlocks_EachByItsOwnAwards()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Event_TwoDaysInDifferentParts_GiveTwoBlocks_EachByItsOwnAwards));
        var s = await SeriesAsync(kit, new DateOnly(2026, 9, 1));
        var ev = new CompetitionEvent { Name = "רודובסקי 2026" };
        kit.Db.Add(ev);
        await kit.Db.SaveChangesAsync();
        var day1 = await DayAsync(kit, s.Seniors, "23/10/2026", ev.Id, 1);
        var day2 = await DayAsync(kit, s.Youth, "24/10/2026", ev.Id, 2);

        var woman = await kit.AddSwimmerAsync("Ofir", "female", birthYear: 2008);
        var girl = await kit.AddSwimmerAsync("Elior", "female", birthYear: 2016);
        await kit.AddRowsAsync(
            kit.Row(day1, woman, points: 650), kit.Row(day1, woman, points: 640),
            kit.Row(day2, girl, points: 300), kit.Row(day2, girl, points: 310));

        var blocks = (await kit.OverviewAsync(new ResultFilter { EventId = ev.Id })).OfficialAwards;

        Assert.Equal([day1.Id, day2.Id], blocks.Select(b => b.CompetitionId));
        var money = blocks[0].Awards.Single();
        Assert.Equal("Top juniors & seniors", money.Title);
        Assert.Equal("Juniors & Seniors", blocks[0].PartNameEn);
        var first = money.Groups.Single(g => g.Gender == "female").Places.Single();
        Assert.Equal((woman.Id, 1290, "1,000 ₪ + Cup"), (first.SwimmerId, first.Points, first.Prize));
        Assert.Equal([650, 640], first.Parts);

        var cups = blocks[1].Awards.Single();
        Assert.Equal(girl.Id, cups.Groups.Single().Places.Single().SwimmerId);
        Assert.Equal("https://example.test/takanon.pdf", blocks[1].Regulation.SourceUrl);
    }

    [Fact]
    public async Task DayMode_OnlyItsOwnBlock()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(DayMode_OnlyItsOwnBlock));
        var s = await SeriesAsync(kit, new DateOnly(2026, 9, 1));
        var day2 = await DayAsync(kit, s.Youth, "24/10/2026");
        var girl = await kit.AddSwimmerAsync("Elior", "female", birthYear: 2016);
        await kit.AddRowsAsync(kit.Row(day2, girl, points: 300));

        var blocks = (await kit.OverviewAsync(day2)).OfficialAwards;

        Assert.Equal("Outstanding young swimmers", blocks.Single().Awards.Single().Title);
    }

    [Fact]
    public async Task DayWithoutPart_HasNoBlock()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(DayWithoutPart_HasNoBlock));
        await SeriesAsync(kit, new DateOnly(2026, 9, 1));
        var day = await DayAsync(kit, part: null, "23/10/2026");
        var woman = await kit.AddSwimmerAsync("W", "female", birthYear: 2008);
        await kit.AddRowsAsync(kit.Row(day, woman, points: 650));

        Assert.Empty((await kit.OverviewAsync(day)).OfficialAwards);
    }

    [Fact]
    public async Task Regulation_IsTheLatestEffectiveOnTheDayDate()
    {
        // Регламент поменялся — прошлый выпуск продолжает считаться по старой версии.
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Regulation_IsTheLatestEffectiveOnTheDayDate));
        var s = await SeriesAsync(kit, new DateOnly(2025, 1, 1), moneyPrize: "800 ₪");
        await AddRegulationAsync(kit, s.Series, s.Seniors, s.Youth, new DateOnly(2026, 9, 1), "1,000 ₪ + Cup");
        var day2025 = await DayAsync(kit, s.Seniors, "25/04/2025");
        var day2026 = await DayAsync(kit, s.Seniors, "23/10/2026");
        var woman = await kit.AddSwimmerAsync("W", "female", birthYear: 2006);
        await kit.AddRowsAsync(kit.Row(day2025, woman, points: 600), kit.Row(day2026, woman, points: 650));

        string PrizeOf(CompetitionOverviewDto o) =>
            o.OfficialAwards.Single().Awards.Single().Groups.Single(g => g.Gender == "female").Places.Single().Prize!;

        var o2025 = await kit.OverviewAsync(day2025);
        Assert.Equal("800 ₪", PrizeOf(o2025));
        Assert.Equal("2025-01-01", o2025.OfficialAwards.Single().Regulation.EffectiveFrom);
        Assert.Equal("1,000 ₪ + Cup", PrizeOf(await kit.OverviewAsync(day2026)));
    }

    [Fact]
    public async Task DayBeforeFirstRegulation_HasNoBlock()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(DayBeforeFirstRegulation_HasNoBlock));
        var s = await SeriesAsync(kit, new DateOnly(2026, 9, 1));
        var day2025 = await DayAsync(kit, s.Seniors, "25/04/2025");
        var woman = await kit.AddSwimmerAsync("W", "female", birthYear: 2006);
        await kit.AddRowsAsync(kit.Row(day2025, woman, points: 600));

        Assert.Empty((await kit.OverviewAsync(day2025)).OfficialAwards);
    }

    [Fact]
    public async Task Bands_UseSeasonAge_NotCalendarAge()
    {
        // 24.10.2026 — сезон 2026/27: девочка 2017 г.р. сезонно 10 лет и в «10-11» входит,
        // хотя календарно ей 9 (§2 плана). 2015 г.р. — сезонно 12, в полосу не входит.
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Bands_UseSeasonAge_NotCalendarAge));
        var s = await SeriesAsync(kit, new DateOnly(2026, 9, 1));
        var day = await DayAsync(kit, s.Youth, "24/10/2026");
        var young = await kit.AddSwimmerAsync("Young", "female", birthYear: 2017);
        var older = await kit.AddSwimmerAsync("Older", "female", birthYear: 2015);
        await kit.AddRowsAsync(kit.Row(day, young, points: 300), kit.Row(day, older, points: 900));

        var group = (await kit.OverviewAsync(day)).OfficialAwards.Single().Awards.Single().Groups.Single();

        Assert.Equal((10, 11), (group.MinAge, group.MaxAge));
        Assert.Equal(young.Id, group.Places.Single().SwimmerId);
    }

    [Fact]
    public async Task Rows_Suspect_Relay_TimeFail_Para_AreNotCounted()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Rows_Suspect_Relay_TimeFail_Para_AreNotCounted));
        var s = await SeriesAsync(kit, new DateOnly(2026, 9, 1));
        var day = await DayAsync(kit, s.Seniors, "23/10/2026");
        var woman = await kit.AddSwimmerAsync("W", "female", birthYear: 2006);
        var relay = new Relay { TeamName = "Alpha A" };
        kit.Db.Add(relay);
        await kit.Db.SaveChangesAsync();

        var clean = kit.Row(day, woman, points: 600);
        var suspect = kit.Row(day, woman, points: 700);
        suspect.SuspectReason = "time-order";
        var relayRow = kit.Row(day, woman, points: 800);
        relayRow.RelayId = relay.Id;
        var failed = kit.Row(day, woman, points: 900);
        failed.TimeFail = true;
        var para = kit.Row(day, woman, points: 950);
        para.IsParaPoints = true;
        await kit.AddRowsAsync(clean, suspect, relayRow, failed, para);

        var place = (await kit.OverviewAsync(day)).OfficialAwards.Single().Awards.Single()
            .Groups.Single(g => g.Gender == "female").Places.Single();

        Assert.Equal(600, place.Points);
        Assert.Equal([600], place.Parts);
    }

    [Fact]
    public async Task RegulationChecked_ComesFromTheDay()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(RegulationChecked_ComesFromTheDay));
        var s = await SeriesAsync(kit, new DateOnly(2026, 9, 1));
        var unchecked_ = await DayAsync(kit, s.Seniors, "23/10/2026");
        var checked_ = await kit.AddCompetitionAsync(c =>
        {
            c.Name = "Checked"; c.Date = "24/10/2026"; c.SeriesPartId = s.Seniors.Id;
            c.SeriesRegulationCheckedAt = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        });
        var woman = await kit.AddSwimmerAsync("W", "female", birthYear: 2006);
        await kit.AddRowsAsync(kit.Row(unchecked_, woman, points: 600), kit.Row(checked_, woman, points: 600));

        Assert.False((await kit.OverviewAsync(unchecked_)).OfficialAwards.Single().Regulation.Checked);
        Assert.True((await kit.OverviewAsync(checked_)).OfficialAwards.Single().Regulation.Checked);
    }
}

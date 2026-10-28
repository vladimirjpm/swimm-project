using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Узнавание выпуска серии при импорте (docs/plans/official-awards-plan.md §5.8): серия — по
/// фразе в названии, часть — по возрастам пловцов дня; привязывается только однозначное и только
/// непривязанное. Правило долей — в SeriesMatchTests; здесь — что делает сервис над базой.
/// </summary>
public class SeriesBinderTests
{
    private const string Name2026 = "תחרות השחייה הארצית ע\"ש גל רודובסקי ועדן נימרי - 23/10/2026";

    private sealed class Kit(SwimmDbContext db)
    {
        public SwimmDbContext Db { get; } = db;
        public CompetitionSeriesPart Seniors { get; set; } = null!;
        public CompetitionSeriesPart Youth { get; set; } = null!;
        private readonly Club _club = new() { Name = "Club", NameEn = "Club" };
        private readonly Style _style = new() { Name = "freestyle" };

        public async Task<Competition> DayAsync(string name, string date, int dayNumber, int[] birthYears)
        {
            var comp = new Competition { Name = name, Date = date, DayNumber = dayNumber, PoolType = "50m" };
            Db.Add(comp);
            foreach (var (year, i) in birthYears.Select((y, i) => (y, i)))
            {
                var s = new Swimmer { FirstName = $"S{dayNumber}-{i}", LastName = "T", BirthYear = year, Gender = "female" };
                Db.Add(s);
                Db.Results.Add(new ResultRecord
                {
                    Competition = comp, Swimmer = s, Club = _club, Style = _style, Distance = "100",
                    Gender = "female", CompetitionDate = DateTime.UtcNow, TimeOriginal = "1:00.00", AgeGroup = "x",
                    EventStyleAge = "100 freestyle x"
                });
            }
            await Db.SaveChangesAsync();
            return comp;
        }
    }

    private static async Task<Kit> CreateAsync(string name, string? phrases = "רודובסקי ועדן נימרי")
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var series = new CompetitionSeries { Name = "רודובסקי/נימרי", MatchPhrases = phrases };
        var kit = new Kit(db)
        {
            Seniors = new CompetitionSeriesPart { Series = series, Name = "נוער ובוגרים", SortOrder = 1, MinSeasonAge = 14 },
            Youth = new CompetitionSeriesPart { Series = series, Name = "צעירים", SortOrder = 2, MinSeasonAge = 9, MaxSeasonAge = 14 },
        };
        db.AddRange(series, kit.Seniors, kit.Youth,
            new SeriesRegulation { Series = series, EffectiveFrom = new DateOnly(2026, 9, 1) });
        await db.SaveChangesAsync();
        return kit;
    }

    // Сезон 2026/27: сезонный возраст = 2027 − год рождения.
    private static readonly int[] SeniorYears = [2000, 2005, 2008, 2010, 2013];   // 27, 22, 19, 17, 14
    private static readonly int[] YouthYears = [2017, 2016, 2015, 2014, 2013];    // 10…14

    [Fact]
    public async Task NewEdition_BothDays_BoundToPartsByAges_NotByDayNumber()
    {
        var kit = await CreateAsync(nameof(NewEdition_BothDays_BoundToPartsByAges_NotByDayNumber));
        // Дни нарочно в обратном порядке: младшие в первый день.
        var youngDay = await kit.DayAsync(Name2026, "23/10/2026", 1, YouthYears);
        var olderDay = await kit.DayAsync(Name2026, "24/10/2026", 2, SeniorYears);

        var result = await new SeriesBinder(kit.Db).BindAsync([youngDay.Id, olderDay.Id]);

        Assert.All(result, r => Assert.True(r.BoundNow));
        Assert.Equal(kit.Youth.Id, (await kit.Db.Competitions.FindAsync(youngDay.Id))!.SeriesPartId);
        Assert.Equal(kit.Seniors.Id, (await kit.Db.Competitions.FindAsync(olderDay.Id))!.SeriesPartId);
    }

    [Fact]
    public async Task OtherMeet_IsNotTouched()
    {
        var kit = await CreateAsync(nameof(OtherMeet_IsNotTouched));
        var other = await kit.DayAsync("אליפות הים האדום", "23/10/2026", 1, SeniorYears);

        Assert.Empty(await new SeriesBinder(kit.Db).BindAsync([other.Id]));
        Assert.Null((await kit.Db.Competitions.FindAsync(other.Id))!.SeriesPartId);
    }

    [Fact]
    public async Task ManualBinding_IsNotOverwritten_ButMismatchIsVisible()
    {
        var kit = await CreateAsync(nameof(ManualBinding_IsNotOverwritten_ButMismatchIsVisible));
        var olderDay = await kit.DayAsync(Name2026, "23/10/2026", 1, SeniorYears);
        olderDay.SeriesPartId = kit.Youth.Id; // руками — не туда
        await kit.Db.SaveChangesAsync();

        var row = Assert.Single(await new SeriesBinder(kit.Db).BindAsync([olderDay.Id]));

        Assert.False(row.BoundNow);
        Assert.Equal(kit.Youth.Id, (await kit.Db.Competitions.FindAsync(olderDay.Id))!.SeriesPartId);
        Assert.True(row.CurrentPartShare < 0.8);           // series.part-mismatch это увидит
        Assert.Equal(kit.Seniors.Id, row.PickedPartId);
    }

    [Fact]
    public async Task MixedAges_NotBound_ReasonGiven()
    {
        var kit = await CreateAsync(nameof(MixedAges_NotBound_ReasonGiven));
        var mixed = await kit.DayAsync(Name2026, "23/10/2026", 1, [2000, 2005, 2016, 2017]);

        var row = Assert.Single(await new SeriesBinder(kit.Db).BindAsync([mixed.Id]));

        Assert.False(row.BoundNow);
        Assert.Null(row.PickedPartId);
        Assert.Contains("80", row.Reason);
        Assert.Null((await kit.Db.Competitions.FindAsync(mixed.Id))!.SeriesPartId);
    }

    [Fact]
    public async Task SeriesWithoutPhrases_IsNotRecognised()
    {
        var kit = await CreateAsync(nameof(SeriesWithoutPhrases_IsNotRecognised), phrases: null);
        var day = await kit.DayAsync(Name2026, "23/10/2026", 1, SeniorYears);

        Assert.Empty(await new SeriesBinder(kit.Db).BindAsync([day.Id]));
    }

    [Fact]
    public async Task Evaluate_BoundDayWithRegulation_ReportsUnchecked()
    {
        var kit = await CreateAsync(nameof(Evaluate_BoundDayWithRegulation_ReportsUnchecked));
        var day = await kit.DayAsync(Name2026, "23/10/2026", 1, SeniorYears);
        await new SeriesBinder(kit.Db).BindAsync([day.Id]);

        var row = Assert.Single(await new SeriesBinder(kit.Db).EvaluateAsync());

        Assert.True(row.HasRegulation);
        Assert.Null(row.RegulationCheckedAt);       // series.regulation-unchecked это увидит
        Assert.Equal(1.0, row.CurrentPartShare);
    }
}

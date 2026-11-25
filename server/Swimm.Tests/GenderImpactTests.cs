using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р126 (Влад, 25.11.2026: «если есть переписывания рекордов или что то серьезное — тогда ручная
/// проверка»): что считается серьёзным последствием проставления пола. Сравнение карточек Overview —
/// чистое; сухой расчёт — настоящими <see cref="GenderImpactService"/> и <see cref="ResultRepository"/>.
/// </summary>
public class GenderImpactTests
{
    // ── Сравнение карточек Overview «до» и «после» ───────────────────────────────────────

    private static GenderOverviewCards Cards(
        OverviewBestSwimDto? bestMale = null,
        IReadOnlyList<OverviewMedalistDto>? medalsMale = null,
        IReadOnlyList<OverviewHighPointDto>? highPoint = null,
        IReadOnlyList<ClubSummaryDto>? clubs = null,
        IReadOnlyList<ClubSummaryDto>? clubsMen = null,
        IReadOnlyList<OverviewRecordDto>? records = null) =>
        new(bestMale, null, [], medalsMale ?? [], [], highPoint ?? [], [], clubs ?? [], clubsMen ?? [], [], records ?? []);

    private static ClubSummaryDto ClubRow(int id, int points) => new() { ClubId = id, Club = $"C{id}", Points = points };

    private static string[] Codes(GenderOverviewCards before, GenderOverviewCards after) =>
        GenderImpactRules.CompareOverviews(before, after, "«Meet»").Select(i => i.Code).ToArray();

    [Fact]
    public void NothingChanged_NoConsequences()
    {
        var cards = Cards(clubs: [ClubRow(1, 10)], clubsMen: [ClubRow(1, 4)]);
        Assert.Empty(Codes(cards, Cards(clubs: [ClubRow(1, 10)], clubsMen: [ClubRow(1, 4)])));
    }

    // (а) рекорд появился / исчез / сменил владельца — любое отличие списка.
    [Fact]
    public void RecordAppears_OrChangesOwner_IsSerious()
    {
        var mine = new OverviewRecordDto { Kind = "NR", StyleName = "freestyle", Distance = "50", Gender = "male", Time = "00:21.00", SwimmerId = 1, ResultId = 10 };
        var theirs = new OverviewRecordDto { Kind = "NR", StyleName = "freestyle", Distance = "50", Gender = "male", Time = "00:21.50", SwimmerId = 2, ResultId = 20 };

        var appeared = GenderImpactRules.CompareOverviews(Cards(), Cards(records: [mine]), "«Meet»");
        Assert.Equal(GenderImpactRules.Record, Assert.Single(appeared).Code);
        Assert.Contains("появится рекорд 50 freestyle male", appeared[0].Text);

        Assert.Equal([GenderImpactRules.Record], Codes(Cards(records: [theirs]), Cards(records: [mine])));
        Assert.Equal([GenderImpactRules.Record], Codes(Cards(records: [theirs]), Cards()));
    }

    // (б) медали и очки.
    [Fact]
    public void MedalList_AndOverallClubPoints_AreSerious()
    {
        var medalist = new OverviewMedalistDto { SwimmerId = 1, LastName = "A", Gold = 1 };
        Assert.Equal([GenderImpactRules.Medals], Codes(Cards(), Cards(medalsMale: [medalist])));
        Assert.Equal([GenderImpactRules.ClubPoints], Codes(Cards(clubs: [ClubRow(1, 10)]), Cards(clubs: [ClubRow(1, 12)])));
    }

    // ♂/♀-вкладка клубов: очки клуба выросли на очки дописанной строки — это закрытая дыра, не
    // последствие; серьёзно — только когда клубы поменялись местами или составом.
    [Fact]
    public void GenderClubTab_PointsGrow_NotSerious_OrderChanges_Serious()
    {
        var before = Cards(clubsMen: [ClubRow(1, 10), ClubRow(2, 8)]);

        Assert.Empty(Codes(before, Cards(clubsMen: [ClubRow(1, 13), ClubRow(2, 8)])));
        Assert.Equal([GenderImpactRules.ClubOrder], Codes(before, Cards(clubsMen: [ClubRow(2, 11), ClubRow(1, 10)])));
        Assert.Equal([GenderImpactRules.ClubOrder], Codes(before, Cards(clubsMen: [ClubRow(1, 10), ClubRow(2, 8), ClubRow(3, 1)])));
    }

    // (в) топы.
    [Fact]
    public void BestSwim_AndHighPoint_AreSerious()
    {
        var best = new OverviewBestSwimDto { ResultId = 10, SwimmerId = 1, LastName = "A", Distance = "50", StyleName = "freestyle", Time = "00:25.00" };
        Assert.Equal([GenderImpactRules.BestSwim], Codes(Cards(), Cards(bestMale: best)));

        var hp = new OverviewHighPointDto { SwimmerId = 1, LastName = "A", Gender = "male", AgeGroup = "12", Points = 40 };
        var items = GenderImpactRules.CompareOverviews(Cards(), Cards(highPoint: [hp]), "«Meet»");
        Assert.Equal(GenderImpactRules.HighPoint, Assert.Single(items).Code);
        Assert.Contains("войдёт A", items[0].Text);
    }

    // ── Сухой расчёт настоящими сервисами ────────────────────────────────────────────────

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static readonly DateTime Date = new(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

    private sealed class World
    {
        public required SwimmDbContext Db;
        public required SwimmReadDbContext Read;
        public required Competition Comp;
        public required Style Style;
        public required Club Club;
        public int NextId = 100;

        public GenderImpactService Service() =>
            new(Db, new ResultRepository(Read, new NullCache()), NullLogger<GenderImpactService>.Instance);

        public async Task<string[]> CodesAsync(int swimmerId, string gender = "male") =>
            (await Service().AssessAsync(swimmerId, gender)).Serious.Select(i => i.Code).Distinct().ToArray();

        public Swimmer AddSwimmer(string? gender, int birthYear = 2014)
        {
            var id = NextId++;
            var s = new Swimmer { Id = id, LastName = $"L{id}", FirstName = $"F{id}", LastNameEn = $"L{id}", FirstNameEn = $"F{id}", BirthYear = birthYear, Gender = gender };
            Db.Swimmers.Add(s);
            return s;
        }

        public ResultRecord AddRow(Swimmer s, string gender, int timeMs, int? position = null, int points = 0,
            string? category = "mix-shabbat", Competition? comp = null)
        {
            var r = new ResultRecord
            {
                CompetitionId = (comp ?? Comp).Id, SwimmerId = s.Id, ClubId = Club.Id, StyleId = Style.Id,
                Distance = "50", Gender = gender, CompetitionDate = Date, TimeOriginal = "x", TimeMillisecond = timeMs,
                Position = position, InternationalPoints = points, EventCategory = category,
                AgeGroup = "12", EventStyleAge = "12",
            };
            Db.Results.Add(r);
            return r;
        }

        /// <summary>Десять сверстников быстрее — чтобы заплыв не входил в топ Season best страны.</summary>
        public void AddFasterPeers(int birthYear = 2014, string gender = "male")
        {
            for (var i = 0; i < GenderImpactRules.SeasonBestTop; i++)
                AddRow(AddSwimmer(gender, birthYear), gender, 20000 + i, points: 500, category: null);
        }
    }

    private static async Task<World> SeedAsync(string name, bool combined = false)
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var read = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>().UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "21/11/2026", PoolType = "25m", ShowCombineAllResults = combined,
        };
        db.AddRange(style, club, comp);
        await db.SaveChangesAsync();
        return new World { Db = db, Read = read, Comp = comp, Style = style, Club = club };
    }

    // Строка без места и очков, в топы не входит — последствий нет, пол можно писать автоматически.
    [Fact]
    public async Task PlainSwim_NoConsequences()
    {
        var w = await SeedAsync(nameof(PlainSwim_NoConsequences));
        w.AddFasterPeers();
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000);
        await w.Db.SaveChangesAsync();

        var impact = await w.Service().AssessAsync(me.Id, "male");

        Assert.False(impact.IsSerious, string.Join(" | ", impact.Serious.Select(i => $"{i.Code}: {i.Text}")));
    }

    // (г) личные заплывы противоположного пола в несмешанной программе.
    [Fact]
    public async Task SwimsOfOppositeGender_InRegularProgramme_AreSerious_MixedOnesAreNot()
    {
        var w = await SeedAsync(nameof(SwimsOfOppositeGender_InRegularProgramme_AreSerious_MixedOnesAreNot));
        w.AddFasterPeers();
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000);
        var other = w.AddRow(me, "female", 46000, category: "mix-0");
        await w.Db.SaveChangesAsync();

        Assert.DoesNotContain(GenderImpactRules.OppositeGenderSwims, await w.CodesAsync(me.Id));

        other.EventCategory = "12";
        await w.Db.SaveChangesAsync();
        Assert.Contains(GenderImpactRules.OppositeGenderSwims, await w.CodesAsync(me.Id));
        // К женскому полу те же строки претензий не дают.
        Assert.DoesNotContain(GenderImpactRules.OppositeGenderSwims, await w.CodesAsync(me.Id, "female"));
    }

    // (б) медаль: место 1–3 войдёт в мужской список Most decorated.
    [Fact]
    public async Task MedalPlace_ChangesGenderedMedalList()
    {
        var w = await SeedAsync(nameof(MedalPlace_ChangesGenderedMedalList));
        w.AddFasterPeers();
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000, position: 1);
        await w.Db.SaveChangesAsync();

        Assert.Contains(GenderImpactRules.Medals, await w.CodesAsync(me.Id));
    }

    // (в) Best swim ♂: лучший по очкам заплыв соревнования — у пловца без пола.
    [Fact]
    public async Task TopPointsSwim_BecomesBestSwimOfItsGender()
    {
        var w = await SeedAsync(nameof(TopPointsSwim_BecomesBestSwimOfItsGender));
        w.AddFasterPeers();
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000, points: 900);
        await w.Db.SaveChangesAsync();

        Assert.Contains(GenderImpactRules.BestSwim, await w.CodesAsync(me.Id));
    }

    // (б) объединённые места: в несмешанной программе строка уйдёт из своей «группы без пола» в общую.
    [Fact]
    public async Task CombinedPlaces_Change_WhenCompetitionUsesCombineAllResults()
    {
        var w = await SeedAsync(nameof(CombinedPlaces_Change_WhenCompetitionUsesCombineAllResults), combined: true);
        w.AddFasterPeers();
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000, category: null);
        await w.Db.SaveChangesAsync();

        Assert.Contains(GenderImpactRules.CombinedPlace, await w.CodesAsync(me.Id));
    }

    // Смешанная программа при включённом Combine All Results: пол входит в ключ дисциплины
    // (И-62: «mix-0» — это и «כללי» обоих полов, где места считаются внутри пола), значит
    // проставление части пловцов разбило бы общий список — такое автоматика не пишет.
    [Fact]
    public async Task MixedProgramme_WithCombineAllResults_IsLeftToHuman_NotSilentlyReRanked()
    {
        var w = await SeedAsync(nameof(MixedProgramme_WithCombineAllResults_IsLeftToHuman_NotSilentlyReRanked), combined: true);
        w.AddFasterPeers();
        var me = w.AddSwimmer(null);
        var other = w.AddSwimmer(null);
        w.AddRow(me, "", 45000, category: "mix-shabbat");
        w.AddRow(other, "", 44000, category: "mix-shabbat");
        await w.Db.SaveChangesAsync();

        Assert.Contains(GenderImpactRules.CombinedPlace, await w.CodesAsync(me.Id));
    }

    // (в) Season best: в своей ступени пловец окажется в топе страны.
    [Fact]
    public async Task SwimEntersSeasonBestTop_IsSerious()
    {
        var w = await SeedAsync(nameof(SwimEntersSeasonBestTop_IsSerious));
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000);
        await w.Db.SaveChangesAsync();

        var impact = await w.Service().AssessAsync(me.Id, "male");

        var sb = Assert.Single(impact.Serious, i => i.Code == GenderImpactRules.SeasonBest);
        Assert.Contains("1-е место", sb.Text);
        Assert.Contains("SB", sb.Text);
    }

    // Сезон новее витринного на /season-best не показан и отметку SB не даёт — на сайте ничего не меняется.
    [Fact]
    public async Task SeasonNewerThanShowcase_IsNotASeasonBestConsequence()
    {
        var w = await SeedAsync(nameof(SeasonNewerThanShowcase_IsNotASeasonBestConsequence));
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000);
        await w.Db.SaveChangesAsync();
        GenderImpactService With(int showcaseYear) => new(w.Db, new ResultRepository(w.Read, new NullCache()),
            NullLogger<GenderImpactService>.Instance,
            Moq.Mock.Of<IShowcaseSeasonProvider>(p => p.CurrentStartYearAsync(It.IsAny<CancellationToken>()) == Task.FromResult(showcaseYear)));

        Assert.DoesNotContain((await With(2025).AssessAsync(me.Id, "male")).Serious, i => i.Code == GenderImpactRules.SeasonBest);
        Assert.Contains((await With(2026).AssessAsync(me.Id, "male")).Serious, i => i.Code == GenderImpactRules.SeasonBest);
    }

    // Ревью PR #286, дыра 1: у многодневки сайт показывает и обзор ДНЯ. Лидер Most decorated ♂
    // события не меняется (у Y два золота), а в обзоре второго дня мужских медалей не было — X
    // станет его лидером. Без расчёта по дню пол записался бы сам.
    [Fact]
    public async Task MultiDayEvent_DayOverviewChange_IsSerious_EvenWhenEventOverviewIsSame()
    {
        var w = await SeedAsync(nameof(MultiDayEvent_DayOverviewChange_IsSerious_EvenWhenEventOverviewIsSame));
        var evt = new CompetitionEvent { Name = "Meet" };
        w.Db.Add(evt);
        await w.Db.SaveChangesAsync();
        w.Comp.EventId = evt.Id;
        var day2 = new Competition
        {
            Name = "Meet", Country = w.Comp.Country, Date = "22/11/2026", PoolType = "25m", EventId = evt.Id,
        };
        w.Db.Add(day2);
        await w.Db.SaveChangesAsync();

        w.AddFasterPeers();
        var y = w.AddSwimmer("male");
        w.AddRow(y, "male", 30000, position: 1, category: null);
        w.AddRow(y, "male", 31000, position: 1, category: "13");
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000, position: 1, comp: day2);
        await w.Db.SaveChangesAsync();

        var impact = await w.Service().AssessAsync(me.Id, "male");

        Assert.Contains(impact.Serious, i => i.Code == GenderImpactRules.Medals && i.Text.Contains("день 22/11/2026"));
        Assert.DoesNotContain(impact.Serious, i => i.Code == GenderImpactRules.Medals && !i.Text.Contains("день"));
    }

    // Дыра 2: медаль в смешанной эстафете другого соревнования. Личных строк без пола там нет,
    // но пол ноги в Most decorated берётся у пловца — после записи X войдёт в мужской список.
    [Fact]
    public async Task RelayMedal_InCompetitionWithoutEmptyPersonalRows_IsSerious()
    {
        var w = await SeedAsync(nameof(RelayMedal_InCompetitionWithoutEmptyPersonalRows_IsSerious));
        w.AddFasterPeers();
        var me = w.AddSwimmer(null);
        w.AddRow(me, "", 45000);   // в своём соревновании последствий нет

        var other = new Competition { Name = "Relays", Country = w.Comp.Country, Date = "28/11/2026", PoolType = "25m" };
        var relay = new Relay { TeamName = "Alpha A", SwimmersName = "x" };
        w.Db.AddRange(other, relay);
        await w.Db.SaveChangesAsync();
        var anchor = w.AddSwimmer("female");
        w.Db.Results.Add(new ResultRecord
        {
            CompetitionId = other.Id, SwimmerId = anchor.Id, ClubId = w.Club.Id, StyleId = w.Style.Id, RelayId = relay.Id,
            Distance = "4x50", Gender = "mixed", CompetitionDate = Date, TimeOriginal = "x", TimeMillisecond = 120000,
            Position = 1, AgeGroup = "12", EventStyleAge = "12",
        });
        w.Db.AddRange(new RelayMember { RelayId = relay.Id, SwimmerId = anchor.Id, LegOrder = 1 },
            new RelayMember { RelayId = relay.Id, SwimmerId = me.Id, LegOrder = 2 });
        await w.Db.SaveChangesAsync();

        var impact = await w.Service().AssessAsync(me.Id, "male");

        Assert.Contains(impact.Serious, i => i.Code == GenderImpactRules.Medals && i.Text.Contains("«Relays»"));
    }

    // Дыра 3: Combine All Results, объединённое место не меняется (пловец один в своей группе),
    // но в режиме combined медаль считается по нему: 4-е в заплыве, 1-е объединённое — золото ♂
    // появится только при включённом тоггле.
    [Fact]
    public async Task CombinedToggleView_MedalByCombinedPlace_IsSerious()
    {
        var w = await SeedAsync(nameof(CombinedToggleView_MedalByCombinedPlace_IsSerious), combined: true);
        w.AddFasterPeers();
        var me = w.AddSwimmer(null);
        var row = w.AddRow(me, "", 45000, position: 4);
        row.CombinedPlace = 1;
        await w.Db.SaveChangesAsync();

        var impact = await w.Service().AssessAsync(me.Id, "male");

        Assert.DoesNotContain(impact.Serious, i => i.Code == GenderImpactRules.CombinedPlace);
        Assert.DoesNotContain(impact.Serious, i => i.Code == GenderImpactRules.Medals && !i.Text.Contains("[Combine All Results]"));
        Assert.Contains(impact.Serious, i => i.Code == GenderImpactRules.Medals && i.Text.Contains("[Combine All Results]"));
    }
}

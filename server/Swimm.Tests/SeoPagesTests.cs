using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.API.Hosting;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Открытие для поиска (A10): тексты голов (SeoText), разбор пути и вставка в html (SeoPages),
/// состав sitemap.xml (SeoRepository) — скрытые из поиска, пловцы групп, псевдо- и склеенные
/// клубы в него не попадают.
/// </summary>
public class SeoPagesTests
{
    // ── SeoText ──────────────────────────────────────────────────────────────

    private static SeoSwimmerFacts Swimmer(string he = "נועה", string heLast = "כהן",
        string? club = "מכבי חיפה", bool hidden = false, int swims = 12, int comps = 4, int birthYear = 1990) =>
        new(7, he, heLast, "Noa", "Cohen", club, "Maccabi Haifa", hidden, birthYear, swims, comps);

    [Fact]
    public void Swimmer_HebrewNameByDefault_NoBirthYear()
    {
        var head = SeoText.Swimmer(Swimmer(), Year);

        Assert.Equal("נועה כהן — swimming results · SwimHub", head.Title);
        Assert.Contains("נועה כהן (מכבי חיפה)", head.Description);
        Assert.Contains("12 swims at 4 competitions", head.Description);
        Assert.Equal("/swimmers/7", head.CanonicalPath);
        Assert.False(head.NoIndex);
    }

    [Fact]
    public void Swimmer_NoHebrewName_FallsBackToEnglish()
    {
        var head = SeoText.Swimmer(Swimmer(he: "", heLast: " "), Year);
        Assert.StartsWith("Noa Cohen —", head.Title);
    }

    [Fact]
    public void Swimmer_HiddenFromSearch_IsNoIndex()
    {
        Assert.True(SeoText.Swimmer(Swimmer(hidden: true), Year).NoIndex);
    }

    // ── Дети не индексируются (решение Влада 14.10.2026) ─────────────────────

    private const int Year = 2026;

    [Theory]
    [InlineData(2007, true)]   // 2026 − 2007 = 19 → 18 исполнилось наверняка
    [InlineData(2008, false)]  // в 2026 может быть ещё 17 — до дня рождения
    [InlineData(2014, false)]
    [InlineData(0, false)]     // год неизвестен — считаем ребёнком
    public void IsCertainlyAdult_ByBirthYearOnly(int birthYear, bool adult)
    {
        Assert.Equal(adult, SeoText.IsCertainlyAdult(birthYear, Year));
        Assert.Equal(!adult, SeoText.Swimmer(Swimmer(birthYear: birthYear), Year).NoIndex);
    }

    [Fact]
    public void Swimmer_MinorPage_KeepsTitleButNoIndex()
    {
        var head = SeoText.Swimmer(Swimmer(birthYear: 2014), Year);
        Assert.True(head.NoIndex);
        Assert.DoesNotContain("2014", head.Title + head.Description);
    }

    [Fact]
    public void Swimmer_Singular_AndNoClub()
    {
        var head = SeoText.Swimmer(Swimmer(club: null, swims: 1, comps: 1) with { ClubNameEn = null }, Year);
        Assert.Contains("נועה כהן: 1 swim at 1 competition.", head.Description);
    }

    [Fact]
    public void Club_MergedOrPseudo_NoIndexAndCanonicalToTarget()
    {
        var merged = SeoText.Club(new SeoClubFacts(5, "הפועל", "Hapoel", false, 9, 30));
        Assert.True(merged.NoIndex);
        Assert.Equal("/clubs/9", merged.CanonicalPath);

        var pseudo = SeoText.Club(new SeoClubFacts(6, "MIX", "MIX", true, null, 0));
        Assert.True(pseudo.NoIndex);

        var live = SeoText.Club(new SeoClubFacts(7, "מכבי חיפה", "Maccabi Haifa", false, null, 120));
        Assert.False(live.NoIndex);
        Assert.Equal("מכבי חיפה — swimming club · SwimHub", live.Title);
        Assert.Contains("120 swimmers", live.Description);
    }

    [Fact]
    public void Competition_TitleWithDate_DescriptionWithCounts()
    {
        var head = SeoText.Competition(new SeoCompetitionFacts(1768, "ליגה אזורית", null, "09/10/2026", "25m", 1530, 412));

        Assert.Equal("ליגה אזורית 09/10/2026 — results · SwimHub", head.Title);
        Assert.Contains("412 swimmers, 1,530 swims", head.Description);
        Assert.Contains("(25m pool)", head.Description);
    }

    [Fact]
    public void Description_IsClippedForSnippets()
    {
        var head = SeoText.Club(new SeoClubFacts(1, new string('א', 300), "", false, null, 1));
        Assert.True(head.Description.Length <= SeoText.MaxDescriptionLength);
        Assert.EndsWith("…", head.Description);
    }

    // ── SeoPages.Match ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("/swimmers/42", SeoPages.Kind.Swimmer, 42)]
    [InlineData("/clubs/7/", SeoPages.Kind.Club, 7)]
    [InlineData("/competitions/1768", SeoPages.Kind.Competition, 1768)]
    [InlineData("/competitions/last", SeoPages.Kind.NoIndexOnly, 0)]
    [InlineData("/competitions/upcoming/555", SeoPages.Kind.NoIndexOnly, 0)]
    [InlineData("/groups/dolphins", SeoPages.Kind.NoIndexOnly, 0)]
    [InlineData("/groups/dolphins/results", SeoPages.Kind.NoIndexOnly, 0)]
    public void Match_KnownPages(string path, SeoPages.Kind kind, int id)
    {
        Assert.Equal(new SeoPages.Target(kind, id), SeoPages.Match(path));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/results")]
    [InlineData("/swimmers")]
    [InlineData("/swimmers/abc")]
    [InlineData("/swimmers/0")]
    [InlineData("/help/groups")]
    [InlineData("/records/compare")]
    public void Match_OtherPages_Null(string path)
    {
        Assert.Null(SeoPages.Match(path));
    }

    // ── SeoPages.Inject ──────────────────────────────────────────────────────

    private const string Html =
        "<!doctype html><html><head><meta charset=\"utf-8\" />\n" +
        "<meta name=\"robots\" content=\"noindex, nofollow\" />\n<title>Swimmer</title>\n</head><body><div id=\"root\"></div></body></html>";

    [Fact]
    public void Inject_ReplacesTitle_AddsMeta_DropsStaticRobots()
    {
        var head = new SeoHead("A — swimming results · SwimHub", "desc", "/swimmers/1", false);
        var html = SeoPages.Inject(Html, head, noIndex: false, "https://swimhub.co.il/");

        Assert.Contains("<title>A — swimming results · SwimHub</title>", html);
        Assert.DoesNotContain("<title>Swimmer</title>", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://swimhub.co.il/swimmers/1\" />", html);
        Assert.Contains("<meta name=\"description\" content=\"desc\" />", html);
        Assert.Contains("og:title", html);
        Assert.DoesNotContain("name=\"robots\"", html);
        Assert.True(html.IndexOf("canonical", StringComparison.Ordinal) < html.IndexOf("</head>", StringComparison.Ordinal));
    }

    [Fact]
    public void Inject_EncodesDataFromProtocols()
    {
        var head = new SeoHead("<script>alert(1)</script>", "\"><img src=x>", "/swimmers/1", false);
        var html = SeoPages.Inject(Html, head, noIndex: false, "https://swimhub.co.il");

        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("\"><img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Inject_DollarInName_IsNotRegexSubstitution()
    {
        var head = new SeoHead("Club $1 $0", "d", "/clubs/1", false);
        var html = SeoPages.Inject(Html, head, noIndex: false, "https://swimhub.co.il");
        Assert.Contains("<title>Club $1 $0</title>", html);
    }

    [Fact]
    public void Inject_NoIndexOnly_KeepsTitle_AddsRobots()
    {
        var html = SeoPages.Inject(Html, head: null, noIndex: true, "https://swimhub.co.il");

        Assert.Contains("<title>Swimmer</title>", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, follow\" />", html);
        Assert.DoesNotContain("canonical", html);
    }

    [Theory]
    [InlineData("www.swimhub.co.il", "/swimmers/5", "/swimmers/5?tab=h2h", "https://swimhub.co.il/swimmers/5?tab=h2h")]
    [InlineData("WWW.SwimHub.co.il", "/", "/", "https://swimhub.co.il/")]
    [InlineData("swimhub.azurewebsites.net", "/clubs/7", "/clubs/7?x=1", "https://swimhub.co.il/clubs/7?x=1")]
    [InlineData("swimhub.azurewebsites.net", "/auth/login/google", "/auth/login/google", "https://swimhub.co.il/auth/login/google")]
    [InlineData("swimhub.co.il", "/", "/", null)]
    [InlineData("localhost", "/", "/", null)]
    // Служебные пути — на любом хосте без перенаправления: их ждут health check и проверка деплоя.
    [InlineData("swimhub.azurewebsites.net", "/healthz", "/healthz", null)]
    [InlineData("swimhub.azurewebsites.net", "/readyz", "/readyz", null)]
    [InlineData("swimhub.azurewebsites.net", "/version", "/version", null)]
    [InlineData("www.swimhub.co.il", "/healthz", "/healthz", null)]
    public void CanonicalRedirect_WwwAndAzureDefaultHost_ExceptServicePaths(
        string host, string path, string pathAndQuery, string? expected)
    {
        Assert.Equal(expected, SeoPages.CanonicalRedirect(host, path, pathAndQuery, "https://swimhub.co.il"));
    }

    [Fact]
    public void BuildSitemap_AbsoluteUrlsOnCanonicalHost()
    {
        var xml = SeoPages.BuildSitemap(["/", "/swimmers/5"], "https://swimhub.co.il/");

        Assert.Contains("<loc>https://swimhub.co.il/</loc>", xml);
        Assert.Contains("<loc>https://swimhub.co.il/swimmers/5</loc>", xml);
        Assert.StartsWith("<?xml", xml);
    }

    // ── SeoRepository: состав sitemap и факты пловца ─────────────────────────

    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static ResultRecord Result(int compId, int swimmerId, int styleId) => new()
    {
        CompetitionId = compId, SwimmerId = swimmerId, StyleId = styleId,
        Distance = "100", Gender = "male", CompetitionDate = new DateTime(2026, 10, 9),
        TimeOriginal = "1:00.00", TimeMillisecond = 60000,
        AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
    };

    private static Swimmer NewSwimmer(string last, bool hidden = false, int? privateGroup = null, int birthYear = 1990) => new()
    {
        FirstName = "שם", LastName = last, FirstNameEn = "Name", LastNameEn = last,
        BirthYear = birthYear, HideFromSearch = hidden, PrivateHubGroupId = privateGroup
    };

    [Fact]
    public async Task Sitemap_ExcludesHiddenPrivateNoResultsMinorsPseudoAndMerged()
    {
        await using var db = CreateDb(nameof(Sitemap_ExcludesHiddenPrivateNoResultsMinorsPseudoAndMerged));
        var style = new Style { Name = "freestyle" };
        var live = new Club { Name = "מכבי", NameEn = "Maccabi" };
        var pseudo = new Club { Name = "MIX", NameEn = "MIX", IsPseudo = true };
        var comp = new Competition { Name = "ליגה", Date = "09/10/2026", PoolType = "25m" };
        var open = NewSwimmer("open");
        var hidden = NewSwimmer("hidden", hidden: true);
        var groupOnly = NewSwimmer("group", privateGroup: 1);
        var noResults = NewSwimmer("none");
        var minor = NewSwimmer("minor", birthYear: DateTime.UtcNow.Year - 12);
        var unknownYear = NewSwimmer("unknown", birthYear: 0);
        db.AddRange(style, live, pseudo, comp, open, hidden, groupOnly, noResults, minor, unknownYear);
        await db.SaveChangesAsync();
        var merged = new Club { Name = "ישן", NameEn = "Old", MergedIntoId = live.Id };
        db.Add(merged);
        db.Results.AddRange(Result(comp.Id, open.Id, style.Id), Result(comp.Id, hidden.Id, style.Id),
            Result(comp.Id, groupOnly.Id, style.Id), Result(comp.Id, minor.Id, style.Id),
            Result(comp.Id, unknownYear.Id, style.Id));
        await db.SaveChangesAsync();

        var paths = await new SeoRepository(db, new NullCacheService()).GetSitemapPathsAsync();

        Assert.Contains($"/competitions/{comp.Id}", paths);
        Assert.Contains($"/clubs/{live.Id}", paths);
        Assert.DoesNotContain($"/clubs/{pseudo.Id}", paths);
        Assert.DoesNotContain($"/clubs/{merged.Id}", paths);
        Assert.Contains($"/swimmers/{open.Id}", paths);
        Assert.DoesNotContain($"/swimmers/{hidden.Id}", paths);
        Assert.DoesNotContain($"/swimmers/{groupOnly.Id}", paths);
        Assert.DoesNotContain($"/swimmers/{noResults.Id}", paths);
        Assert.DoesNotContain($"/swimmers/{minor.Id}", paths);
        Assert.DoesNotContain($"/swimmers/{unknownYear.Id}", paths);
    }

    [Fact]
    public async Task SwimmerFacts_PrivateGroupSwimmer_IsNull_NameNeverReachesHtml()
    {
        await using var db = CreateDb(nameof(SwimmerFacts_PrivateGroupSwimmer_IsNull_NameNeverReachesHtml));
        var groupOnly = NewSwimmer("group", privateGroup: 1);
        db.Add(groupOnly);
        await db.SaveChangesAsync();

        Assert.Null(await new SeoRepository(db, new NullCacheService()).GetSwimmerAsync(groupOnly.Id));
    }

    [Fact]
    public async Task SwimmerFacts_CountsSwimsAndCompetitions()
    {
        await using var db = CreateDb(nameof(SwimmerFacts_CountsSwimsAndCompetitions));
        var style = new Style { Name = "freestyle" };
        var c1 = new Competition { Name = "A", Date = "01/10/2026", PoolType = "25m" };
        var c2 = new Competition { Name = "B", Date = "09/10/2026", PoolType = "25m" };
        var s = NewSwimmer("x");
        db.AddRange(style, c1, c2, s);
        await db.SaveChangesAsync();
        db.Results.AddRange(Result(c1.Id, s.Id, style.Id), Result(c1.Id, s.Id, style.Id), Result(c2.Id, s.Id, style.Id));
        await db.SaveChangesAsync();

        var facts = await new SeoRepository(db, new NullCacheService()).GetSwimmerAsync(s.Id);

        Assert.NotNull(facts);
        Assert.Equal(3, facts!.Swims);
        Assert.Equal(2, facts.Competitions);
    }
}

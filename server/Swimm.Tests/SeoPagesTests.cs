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

    [Theory]
    [InlineData("גביע - 23/10/2026", "גביע", "גביע - 23/10/2026")]          // подзаголовок уже в названии
    [InlineData("אליפות קיץ", "יום ב", "אליפות קיץ — יום ב")]
    [InlineData(" אליפות ", null, "אליפות")]
    public void CompetitionName_SubNameNotRepeated(string name, string? subName, string expected)
    {
        Assert.Equal(expected, SeoText.CompetitionName(name, subName));
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
    [InlineData("/", SeoPages.Kind.Home, 0)]
    [InlineData("/competitions", SeoPages.Kind.CompetitionsIndex, 0)]
    [InlineData("/competitions/", SeoPages.Kind.CompetitionsIndex, 0)]
    public void Match_KnownPages(string path, SeoPages.Kind kind, int id)
    {
        Assert.Equal(new SeoPages.Target(kind, id), SeoPages.Match(path));
    }

    [Theory]
    [InlineData("/records")]
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

    private static readonly DateOnly Today = new(2026, 11, 1);

    [Fact]
    public void BuildSitemap_AbsoluteUrlsOnCanonicalHost_WithLastmod()
    {
        var xml = SeoPages.BuildSitemap(
            [new("/", null), new("/swimmers/5", new DateOnly(2026, 10, 9))], "https://swimhub.co.il/", Today);

        Assert.Contains("<url><loc>https://swimhub.co.il/</loc></url>", xml);
        Assert.Contains("<loc>https://swimhub.co.il/swimmers/5</loc><lastmod>2026-10-09</lastmod>", xml);
        Assert.StartsWith("<?xml", xml);
    }

    [Fact]
    public void BuildSitemap_FutureDate_NoLastmod()
    {
        var xml = SeoPages.BuildSitemap([new("/competitions/9", Today.AddDays(1))], "https://swimhub.co.il", Today);
        Assert.DoesNotContain("<lastmod>", xml);
    }

    [Fact]
    public void SitemapEntries_HomeAndCompetitionsGetLatestDate_OtherStaticNone()
    {
        var entries = SeoPages.SitemapEntries(
        [
            new("/competitions/1", new DateOnly(2026, 9, 1)),
            new("/clubs/2", new DateOnly(2026, 10, 9)),
            new("/swimmers/3", null),
        ]).ToList();

        Assert.Equal(new DateOnly(2026, 10, 9), entries.Single(e => e.Path == "/").LastModified);
        Assert.Equal(new DateOnly(2026, 10, 9), entries.Single(e => e.Path == "/competitions").LastModified);
        Assert.Null(entries.Single(e => e.Path == "/about").LastModified);
        Assert.Contains(entries, e => e.Path == "/swimmers/3");
    }

    // ── Тело страницы в голом HTML (Search Console 01.11.2026) ──────────────

    private static SeoCompetitionRef Comp(int id, string date, string name = "ליגה") => new(id, name, null, date);
    private static SeoSwimmerRef SwimmerRef(int id, string he, string en = "En") => new(id, he, "", en, "");

    [Fact]
    public void CompetitionsBody_NewestFirstByDate_NotByDateString()
    {
        // Строкой «09/10/2026» < «30/09/2026», датой — наоборот.
        var body = SeoText.CompetitionsBody([Comp(1, "30/09/2026"), Comp(2, "09/10/2026"), Comp(3, "01/01/2025")]);

        Assert.Equal(["/competitions/2", "/competitions/1", "/competitions/3"],
            body.Sections.Single().Links.Select(l => l.Path));
        Assert.Equal("09/10/2026", body.Sections.Single().Links[0].Note);
    }

    [Fact]
    public void HomeBody_OnlyLatestCompetitions()
    {
        var comps = Enumerable.Range(1, 30).Select(i => Comp(i, $"{i:00}/10/2026")).ToList();
        var links = SeoText.HomeBody(comps).Sections.Single().Links;

        Assert.Equal(SeoText.HomeLatestCompetitions, links.Count);
        Assert.Equal("/competitions/30", links[0].Path);
    }

    [Fact]
    public void ClubBody_SwimmersByName_CappedWithMoreCount_EnglishFallback()
    {
        var swimmers = Enumerable.Range(1, SeoText.MaxSwimmerLinks + 5).Select(i => SwimmerRef(i, $"שם {i:0000}")).ToList();
        swimmers.Add(SwimmerRef(9999, " ", "Noa Cohen"));
        var facts = new SeoClubFacts(7, "מכבי חיפה", "Maccabi Haifa", false, null, 400);

        var body = SeoText.ClubBody(facts, SeoText.Club(facts), new SeoClubLinks(swimmers, []));
        var section = body.Sections.Single(s => s.Title == "Swimmers");

        Assert.Equal("מכבי חיפה", body.Heading);
        Assert.Equal(SeoText.MaxSwimmerLinks, section.Links.Count);
        Assert.Equal(6, section.More);
        Assert.Equal("Noa Cohen", section.Links[0].Text); // латиница раньше иврита при Ordinal
    }

    private static readonly SeoBody SampleBody = new("נועה <כהן>", "lead",
    [
        new SeoSection("Competitions", [new SeoLink("/competitions/5", "A & B", "09/10/2026")], More: 1234),
        new SeoSection("Empty", []),
    ]);

    [Fact]
    public void RenderBody_LinksEncodedMoreCountNav_EmptySectionsSkipped()
    {
        var html = SeoPages.RenderBody(SampleBody);

        Assert.Contains("<h1 dir=\"auto\">נועה &lt;כהן&gt;</h1>", html);
        Assert.Contains("<li><a href=\"/competitions/5\" dir=\"auto\">A &amp; B</a> · 09/10/2026</li>", html);
        Assert.Contains("and 1,234 more", html);
        Assert.Contains("<a href=\"/competitions\">Competitions</a>", html);
        Assert.DoesNotContain("<h2>Empty</h2>", html);
        Assert.StartsWith("\n<div class=\"seo-static\">", html);
    }

    [Fact]
    public void Inject_Body_GoesIntoEmptyRoot_StyleHidesItWhileScriptRuns()
    {
        var html = SeoPages.Inject(Html, head: null, noIndex: false, "https://swimhub.co.il", SampleBody);

        Assert.Contains("<div id=\"root\">\n<div class=\"seo-static\">", html);
        Assert.Contains(SeoPages.BodyStyle, html);
        Assert.True(html.IndexOf(SeoPages.BodyStyle, StringComparison.Ordinal) < html.IndexOf("</head>", StringComparison.Ordinal));
        Assert.Contains("<title>Swimmer</title>", html);
    }

    [Fact]
    public void Inject_NoEmptyRoot_NoBodyNoStyle()
    {
        var html = SeoPages.Inject(Html.Replace("<div id=\"root\"></div>", "<main></main>"),
            head: null, noIndex: false, "https://swimhub.co.il", SampleBody);

        Assert.DoesNotContain("seo-static", html);
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

        var entries = await new SeoRepository(db, new NullCacheService()).GetSitemapAsync();
        var paths = entries.Select(e => e.Path).ToList();

        Assert.Equal(new DateOnly(2026, 10, 9), entries.Single(e => e.Path == $"/swimmers/{open.Id}").LastModified);
        Assert.Equal(new DateOnly(2026, 10, 9), entries.Single(e => e.Path == $"/competitions/{comp.Id}").LastModified);
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

    /// <summary>
    /// Многодневка — ОДНИМ адресом (первый день по дате), lastmod — по всему событию. Адрес
    /// любого дня открывает на витрине всё событие; раньше sitemap отдавал каждый день, и из
    /// поиска приходили на день 1 чемпионата 6594 — 43 строки из 3968 (08.11.2026).
    /// </summary>
    [Fact]
    public async Task Sitemap_MultiDayEvent_OneEntry_FirstDay_LastmodOfWholeEvent()
    {
        await using var db = CreateDb(nameof(Sitemap_MultiDayEvent_OneEntry_FirstDay_LastmodOfWholeEvent));
        var style = new Style { Name = "freestyle" };
        var ev = new CompetitionEvent { Name = "אליפות" };
        var single = new Competition { Name = "ליגה", Date = "01/10/2026", PoolType = "25m" };
        db.AddRange(style, ev, single);
        await db.SaveChangesAsync();
        // Id дня 2 меньше, чем у дня 1 (день доимпортирован раньше) — первым всё равно день по дате.
        var day2 = new Competition { Name = "אליפות", Date = "25/12/2024", PoolType = "25m", EventId = ev.Id, DayNumber = 2 };
        var day1 = new Competition { Name = "אליפות", Date = "24/12/2024", PoolType = "25m", EventId = ev.Id, DayNumber = 1 };
        var s = NewSwimmer("x");
        db.AddRange(day2, day1, s);
        await db.SaveChangesAsync();
        var late = Result(day2.Id, s.Id, style.Id);
        late.CompetitionDate = new DateTime(2024, 12, 25);
        var early = Result(day1.Id, s.Id, style.Id);
        early.CompetitionDate = new DateTime(2024, 12, 24);
        db.Results.AddRange(late, early, Result(single.Id, s.Id, style.Id));
        await db.SaveChangesAsync();

        var entries = await new SeoRepository(db, new NullCacheService()).GetSitemapAsync();
        var comps = entries.Where(e => e.Path.StartsWith("/competitions/")).ToList();

        Assert.Equal(2, comps.Count);
        Assert.Contains(comps, e => e.Path == $"/competitions/{single.Id}");
        var evEntry = Assert.Single(comps, e => e.Path == $"/competitions/{day1.Id}");
        Assert.Equal(new DateOnly(2024, 12, 25), evEntry.LastModified);
    }

    [Fact]
    public void CompetitionDays_OrderByDateThenDayNumberThenId_UnparsableLast()
    {
        var ids = CompetitionDays.OrderedIds([
            new(10, 3, "26/12/2024"),
            new(5, null, ""),
            new(30, 1, "24/12/2024"),
            new(20, 2, "24/12/2024"),
            new(1, 2, "25/12/2024"),
        ]);

        Assert.Equal([30, 20, 1, 10, 5], ids);
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

    /// <summary>
    /// Ссылки тела: те же пловцы, что в sitemap (дети, скрытые, пловцы групп в HTML не попадают),
    /// клубы — только живые.
    /// </summary>
    [Fact]
    public async Task Links_OnlyIndexableSwimmersAndLiveClubs()
    {
        await using var db = CreateDb(nameof(Links_OnlyIndexableSwimmersAndLiveClubs));
        var style = new Style { Name = "freestyle" };
        var live = new Club { Name = "מכבי", NameEn = "Maccabi" };
        var pseudo = new Club { Name = "MIX", NameEn = "MIX", IsPseudo = true };
        var comp = new Competition { Name = "ליגה", Date = "09/10/2026", PoolType = "25m" };
        var other = new Competition { Name = "אחר", Date = "01/10/2026", PoolType = "25m" };
        db.AddRange(style, live, pseudo, comp, other);
        await db.SaveChangesAsync();
        var adult = NewSwimmer("adult");
        var minor = NewSwimmer("minor", birthYear: DateTime.UtcNow.Year - 12);
        var hidden = NewSwimmer("hidden", hidden: true);
        var groupOnly = NewSwimmer("group", privateGroup: 1);
        foreach (var s in new[] { adult, minor, hidden, groupOnly }) s.ClubId = live.Id;
        db.AddRange(adult, minor, hidden, groupOnly);
        await db.SaveChangesAsync();
        ResultRecord R(Competition c, Swimmer s, Club club) { var r = Result(c.Id, s.Id, style.Id); r.ClubId = club.Id; return r; }
        db.Results.AddRange(R(comp, adult, live), R(comp, minor, live), R(comp, hidden, live),
            R(comp, groupOnly, live), R(comp, adult, pseudo), R(other, adult, live));
        await db.SaveChangesAsync();
        var repo = new SeoRepository(db, new NullCacheService());

        var compLinks = await repo.GetCompetitionLinksAsync(comp.Id);
        Assert.Equal([adult.Id], compLinks.Swimmers.Select(s => s.Id));
        Assert.Equal([live.Id], compLinks.Clubs.Select(c => c.Id));

        var clubLinks = await repo.GetClubLinksAsync(live.Id);
        Assert.Equal([adult.Id], clubLinks.Swimmers.Select(s => s.Id));
        Assert.Equal(new[] { comp.Id, other.Id }.Order(), clubLinks.Competitions.Select(c => c.Id).Order());

        var swimmerLinks = await repo.GetSwimmerLinksAsync(adult.Id);
        Assert.Equal(live.Id, swimmerLinks.Club?.Id);
        Assert.Equal(2, swimmerLinks.Competitions.Count);
    }

    [Fact]
    public async Task BuildPage_MinorSwimmer_NoIndexAndNoBody_AdultGetsBody()
    {
        await using var db = CreateDb(nameof(BuildPage_MinorSwimmer_NoIndexAndNoBody_AdultGetsBody));
        var style = new Style { Name = "freestyle" };
        var comp = new Competition { Name = "ליגה", Date = "09/10/2026", PoolType = "25m" };
        var adult = NewSwimmer("adult");
        var minor = NewSwimmer("minor", birthYear: DateTime.UtcNow.Year - 12);
        db.AddRange(style, comp, adult, minor);
        await db.SaveChangesAsync();
        db.Results.AddRange(Result(comp.Id, adult.Id, style.Id), Result(comp.Id, minor.Id, style.Id));
        await db.SaveChangesAsync();
        var repo = new SeoRepository(db, new NullCacheService());

        var minorPage = await SeoPages.BuildPageAsync(new(SeoPages.Kind.Swimmer, minor.Id), repo, default);
        Assert.True(minorPage.NoIndex);
        Assert.Null(minorPage.Body);

        var adultPage = await SeoPages.BuildPageAsync(new(SeoPages.Kind.Swimmer, adult.Id), repo, default);
        Assert.False(adultPage.NoIndex);
        Assert.Contains(adultPage.Body!.Sections, s => s.Links.Any(l => l.Path == $"/competitions/{comp.Id}"));

        // Соревнование индексируется, но имя ребёнка в его теле не появляется.
        var compPage = await SeoPages.BuildPageAsync(new(SeoPages.Kind.Competition, comp.Id), repo, default);
        var html = SeoPages.RenderBody(compPage.Body!);
        Assert.Contains($"/swimmers/{adult.Id}", html);
        Assert.DoesNotContain($"/swimmers/{minor.Id}\"", html);
        Assert.DoesNotContain("minor", html);
    }
}

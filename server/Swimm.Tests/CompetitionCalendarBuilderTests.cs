using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Календарь сезона главной /competitions (docs/plans/competitions-home-plan.md, Б1):
/// тип по названию (В3), буква группы и сезон (В6), статус многодневки (В8), заявки (В2),
/// площадка (В5); часть В — будущие старты из «Входящих» (В9), перенос и отмена (В4).
/// </summary>
public class CompetitionCalendarBuilderTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static readonly IReadOnlyDictionary<string, CalendarCategoryBand> Bands =
        new Dictionary<string, CalendarCategoryBand>
        {
            ["results-kids-team"] = new(8, 11),
            ["results-youth-team"] = new(11, 14),
            ["results-junior-results"] = new(14, 17),
            ["results-main"] = new(17, null),
            ["results-masters"] = new(null, null),
        };

    private static CalendarCompetitionRow Day(int id, string date, string name = "ליגה", int? eventId = null,
        bool results = true, int? orgCompId = null, string pool = "25m", bool champ = false,
        string[]? categories = null, string discipline = Disciplines.Swimming, string? overrideKind = null,
        string? eventName = null, int? eventOrgCompId = null, int[]? sources = null) => new()
    {
        Id = id,
        EventId = eventId,
        EventName = eventName,
        EventOrgCompId = eventOrgCompId,
        Name = name,
        Date = DateOnly.Parse(date),
        PoolType = pool,
        IsChampionship = champ,
        Discipline = discipline,
        StandingKindOverride = overrideKind,
        OrgCompId = orgCompId,
        HasResults = results,
        CategoryKeys = categories ?? [],
        SourceOrgCompIds = sources ?? [],
    };

    private static CompetitionCalendarDto Build(
        IEnumerable<CalendarCompetitionRow>? comps = null,
        IEnumerable<CalendarEntryRow>? entries = null,
        IEnumerable<CalendarDiscoveredRow>? discovered = null,
        IReadOnlyDictionary<int, bool>? meetInfo = null,
        int season = 2026) =>
        CompetitionCalendarBuilder.Build(season, Today, [2025, 2026], new CalendarInputs
        {
            Competitions = (comps ?? []).ToList(),
            Entries = (entries ?? []).ToList(),
            Discovered = (discovered ?? []).ToList(),
            MeetInfoChampionship = meetInfo ?? new Dictionary<int, bool>(),
            CategoryBands = Bands,
        });

    // ── Тип (В3) ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ליגה צעירים 1 בת ים", Disciplines.Swimming, null, CalendarMeetTypes.Pool)]
    [InlineData("אליפות ישראל במים פתוחים", Disciplines.Swimming, null, CalendarMeetTypes.OpenWater)]
    [InlineData("צליחת הכנרת 2026", Disciplines.Swimming, null, CalendarMeetTypes.OpenWater)]
    [InlineData("Israel Open Water Cup", Disciplines.Swimming, null, CalendarMeetTypes.OpenWater)]
    [InlineData("תחרות קיץ", Disciplines.Swimming, StandingKinds.OpenWater, CalendarMeetTypes.OpenWater)]
    [InlineData("אליפות ישראל", Disciplines.Artistic, null, CalendarMeetTypes.Artistic)]
    // Артистика — только по хранимому признаку: название без правки в админке не решает.
    [InlineData("שחייה אומנותית", Disciplines.Swimming, null, CalendarMeetTypes.Pool)]
    public void TypeFor_name_discipline_override(string name, string discipline, string? overrideKind, string expected) =>
        Assert.Equal(expected, CompetitionCalendarBuilder.TypeFor(name, discipline, overrideKind));

    // ── Группа и сезон (В6) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("results-kids-team", "K", "Kids", "8–11")]
    [InlineData("results-youth-team", "Y", "Young", "11–14")]
    [InlineData("results-junior-results", "J", "Juniors", "14–17")]
    [InlineData("results-main", "A", "Adults", "17+")]
    [InlineData("results-masters", "M", "Masters", "25+")]
    public void GroupFor_letter_and_ages_from_category(string key, string letter, string label, string ages)
    {
        var g = CompetitionCalendarBuilder.GroupFor(false, [key], "תחרות", false, "25m", null, Bands);
        Assert.NotNull(g);
        Assert.Equal(letter, g!.Letter);
        Assert.Equal(label, g.Label);
        Assert.Equal(ages, g.Ages);
    }

    [Fact]
    public void GroupFor_youngest_rung_wins_when_several_categories()
    {
        var g = CompetitionCalendarBuilder.GroupFor(false, ["results-main", "results-kids-team"], "x", false, "25m", null, Bands);
        Assert.Equal("K", g!.Letter);
    }

    [Fact]
    public void GroupFor_custom_category_only_gives_no_badge()
    {
        // Маккабиада: категория есть, но не каноническая — по названию не гадаем.
        Assert.Null(CompetitionCalendarBuilder.GroupFor(false, ["result-maccabiah"], "מכביה ילדים", false, "50m", null, Bands));
    }

    [Theory]
    [InlineData("אליפות ישראל לילדים 8-11 חורף", "K")]
    [InlineData("ליגה צעירים 1 בת ים", "Y")]
    [InlineData("אליפות ישראל לנוער", "J")]
    [InlineData("ליגת מאסטרס - תל אביב", "M")]
    [InlineData("אליפות ישראל בוגרים", "A")]
    public void GroupFor_without_categories_guesses_from_name(string name, string letter) =>
        Assert.Equal(letter, CompetitionCalendarBuilder.GroupFor(false, [], name, false, "", null, Bands)!.Letter);

    [Fact]
    public void GroupFor_without_categories_and_unknown_name_is_null() =>
        Assert.Null(CompetitionCalendarBuilder.GroupFor(false, [], "גביע השרון", false, "", null, Bands));

    [Theory]
    [InlineData("אליפות ישראל חורף", false, "50m", StandingKinds.Winter)]
    [InlineData("אליפות ישראל קיץ", false, "25m", StandingKinds.Summer)]
    [InlineData("אליפות ישראל", true, "25m", StandingKinds.Winter)]
    [InlineData("אליפות ישראל", true, "50m", StandingKinds.Summer)]
    [InlineData("ליגה", false, "25m", null)]
    public void SeasonMarkFor_name_then_pool(string name, bool champ, string pool, string? expected) =>
        Assert.Equal(expected, CompetitionCalendarBuilder.SeasonMarkFor(name, champ, pool, null));

    [Fact]
    public void SeasonMarkFor_open_water_override_is_not_a_season() =>
        Assert.Null(CompetitionCalendarBuilder.SeasonMarkFor("אליפות", true, "50m", StandingKinds.OpenWater));

    // ── Статус (В4, В8) ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-10-25", "2026-10-25", 0, 1, CalendarMeetStatuses.Upcoming)]
    [InlineData("2026-10-19", "2026-10-19", 1, 1, CalendarMeetStatuses.Live)]
    [InlineData("2026-10-18", "2026-10-20", 0, 3, CalendarMeetStatuses.Live)]
    [InlineData("2026-10-09", "2026-10-09", 1, 1, CalendarMeetStatuses.Done)]
    [InlineData("2026-10-16", "2026-10-18", 1, 3, CalendarMeetStatuses.InProgress)]
    [InlineData("2026-10-16", "2026-10-18", 3, 3, CalendarMeetStatuses.Complete)]
    // Протокол первого дня приехал, а остальные дни ещё впереди — тоже «в процессе».
    [InlineData("2026-10-19", "2026-10-21", 1, 3, CalendarMeetStatuses.InProgress)]
    public void StatusFor_rules(string start, string end, int loaded, int total, string expected) =>
        Assert.Equal(expected, CompetitionCalendarBuilder.StatusFor(
            DateOnly.Parse(start), DateOnly.Parse(end), Today, loaded, total));

    [Fact]
    public void Without_discovered_marks_never_cancelled_or_postponed()
    {
        var cal = Build(
            comps: [Day(1, "2026-10-09"), Day(2, "2026-10-16", eventId: 7), Day(3, "2026-10-17", eventId: 7, results: false)],
            entries: [new CalendarEntryRow { OrgCompId = 900, Date = new DateOnly(2026, 11, 1), CompName = "גביע" }]);
        Assert.DoesNotContain(cal.Meets, m => m.Status is CalendarMeetStatuses.Cancelled or CalendarMeetStatuses.Postponed);
        Assert.All(cal.Meets, m => Assert.Null(m.PostponedTo));
    }

    // ── Сборка ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Event_days_fold_into_one_meet_with_event_name()
    {
        var cal = Build(comps:
        [
            Day(11, "2026-10-17", name: "יום 2", eventId: 5, eventName: "אליפות ישראל לנוער — חורף", champ: true,
                categories: ["results-junior-results"]),
            Day(10, "2026-10-16", name: "יום 1", eventId: 5, eventName: "אליפות ישראל לנוער — חורף",
                categories: ["results-junior-results"]),
        ]);

        var m = Assert.Single(cal.Meets);
        Assert.Equal("event:5", m.Key);
        Assert.Equal(CalendarMeetKinds.Event, m.Kind);
        Assert.Equal("אליפות ישראל לנוער — חורף", m.Name);
        Assert.Equal("2026-10-16", m.Start);
        Assert.Equal("2026-10-17", m.End);
        Assert.True(m.IsChampionship);
        Assert.Equal(2, m.DaysTotal);
        Assert.Equal(CalendarMeetStatuses.Complete, m.Status);
        Assert.Equal("J", m.Group!.Letter);
        Assert.Equal(StandingKinds.Winter, m.Group.Season);
    }

    [Fact]
    public void Event_with_missing_days_from_discovered_span_is_in_progress()
    {
        // Затянут один день из трёх: остальные известны из «Входящих» (16–18 октября).
        var cal = Build(
            comps: [Day(10, "2026-10-16", eventId: 5, eventName: "אליפות", eventOrgCompId: 777)],
            discovered: [new CalendarDiscoveredRow { OrgCompId = 777, Venue = "Wingate", Start = new(2026, 10, 16), End = new(2026, 10, 18) }]);

        var m = Assert.Single(cal.Meets);
        Assert.Equal(3, m.DaysTotal);
        Assert.Equal(1, m.DaysLoaded);
        Assert.Equal([true, false, false], m.Days.Select(d => d.Loaded));
        Assert.Equal("2026-10-18", m.End);
        Assert.Equal(CalendarMeetStatuses.InProgress, m.Status);
        Assert.Equal("Wingate", m.Venue);
    }

    [Fact]
    public void Single_competition_is_not_stretched_by_discovered_span()
    {
        // Однодневный в справочнике = протокол целиком у нас; растянуть его — значит вечно «в процессе».
        var cal = Build(
            comps: [Day(1, "2026-10-09", orgCompId: 16845)],
            discovered: [new CalendarDiscoveredRow { OrgCompId = 16845, Venue = " Netanya ", Start = new(2026, 10, 9), End = new(2026, 10, 10) }]);

        var m = Assert.Single(cal.Meets);
        Assert.Equal(1, m.DaysTotal);
        Assert.Equal(CalendarMeetStatuses.Done, m.Status);
        Assert.Equal("Netanya", m.Venue);
    }

    [Fact]
    public void Too_long_discovered_span_is_ignored()
    {
        var cal = Build(
            comps: [Day(10, "2026-10-16", eventId: 5, eventName: "סדרה", eventOrgCompId: 1)],
            discovered: [new CalendarDiscoveredRow { OrgCompId = 1, Start = new(2026, 10, 1), End = new(2026, 10, 31) }]);
        Assert.Equal(1, Assert.Single(cal.Meets).DaysTotal);
    }

    [Fact]
    public void Entries_of_imported_competition_mark_start_list_and_do_not_duplicate()
    {
        var cal = Build(
            comps: [Day(1, "2026-10-09", orgCompId: 100), Day(2, "2026-10-10", sources: [200])],
            entries:
            [
                new CalendarEntryRow { OrgCompId = 100, Date = new(2026, 10, 9), CompName = "x" },        // по compID дня
                new CalendarEntryRow { OrgCompId = 555, CompetitionId = 2, Date = new(2026, 10, 10), CompName = "y" }, // бэкфилл
                new CalendarEntryRow { OrgCompId = 200, Date = new(2026, 10, 10), CompName = "y" },       // источник протокола
            ]);

        Assert.Equal(2, cal.Meets.Count);
        Assert.All(cal.Meets, m => Assert.True(m.HasStartList));
        Assert.DoesNotContain(cal.Meets, m => m.Kind == CalendarMeetKinds.Upcoming);
    }

    [Fact]
    public void Unclaimed_entries_become_upcoming_meet_per_compId()
    {
        var cal = Build(
            entries:
            [
                new CalendarEntryRow { OrgCompId = 16900, Date = new(2026, 10, 25), CompName = "אליפות ישראל לילדים 8-11 חורף" },
                new CalendarEntryRow { OrgCompId = 16900, Date = new(2026, 10, 24), CompName = "אליפות ישראל לילדים 8-11 חורף" },
                new CalendarEntryRow { OrgCompId = 16901, Date = new(2026, 10, 24), CompName = "גביע עמק יזרעאל" },
            ],
            discovered: [new CalendarDiscoveredRow { OrgCompId = 16900, Venue = "Netanya", Start = new(2026, 10, 24), End = new(2026, 10, 25) }],
            meetInfo: new Dictionary<int, bool> { [16900] = true });

        Assert.Equal(["upcoming:16900", "upcoming:16901"], cal.Meets.Select(m => m.Key));
        var champ = cal.Meets[0];
        Assert.Equal(16900, champ.Id);
        Assert.Equal("2026-10-24", champ.Start);
        Assert.Equal("2026-10-25", champ.End);
        Assert.True(champ.IsChampionship);
        Assert.True(champ.HasStartList);
        Assert.Equal(CalendarMeetStatuses.Upcoming, champ.Status);
        Assert.Equal(0, champ.DaysLoaded);
        Assert.Equal("Netanya", champ.Venue);
        Assert.Equal("", champ.Pool);
        Assert.Equal("K", champ.Group!.Letter);
        Assert.Equal(StandingKinds.Winter, champ.Group.Season);
        Assert.False(cal.Meets[1].IsChampionship);
        Assert.Null(cal.Meets[1].Venue);
    }

    [Fact]
    public void Open_water_meet_has_no_pool_length()
    {
        // В справочнике у моря стоит 25m (живой случай: «מים פתוחים- אילת 2026»).
        var cal = Build(comps: [Day(1, "2026-10-09", name: "אליפות ישראל- מים פתוחים- אילת", pool: "25m")]);
        var m = Assert.Single(cal.Meets);
        Assert.Equal(CalendarMeetTypes.OpenWater, m.Type);
        Assert.Equal("", m.Pool);
    }

    [Fact]
    public void Upcoming_artistic_by_discovered_discipline()
    {
        var cal = Build(
            entries: [new CalendarEntryRow { OrgCompId = 1, Date = new(2026, 11, 1), CompName = "תחרות" }],
            discovered: [new CalendarDiscoveredRow { OrgCompId = 1, Start = new(2026, 11, 1), End = new(2026, 11, 1), Discipline = Disciplines.Artistic }]);
        Assert.Equal(CalendarMeetTypes.Artistic, Assert.Single(cal.Meets).Type);
    }

    [Fact]
    public void Meets_outside_season_are_dropped_and_rest_sorted_by_start_then_name()
    {
        var cal = Build(comps:
        [
            Day(1, "2026-08-31", name: "קודם"),      // сезон 2025
            Day(2, "2026-10-09", name: "ב"),
            Day(3, "2026-09-01", name: "ראשון"),
            Day(4, "2026-10-09", name: "א"),
            Day(5, "2027-09-01", name: "אחרי"),      // сезон 2027
        ]);

        Assert.Equal(["ראשון", "א", "ב"], cal.Meets.Select(m => m.Name));
        Assert.Equal(2026, cal.Season);
        Assert.Equal("2026/27", cal.SeasonLabel);
        Assert.Equal("2026-10-19", cal.Today);
        Assert.Equal([2025, 2026], cal.Seasons);
    }

    [Fact]
    public void Venue_prefers_meet_own_compId()
    {
        var cal = Build(
            comps: [Day(1, "2026-10-09", orgCompId: 50, sources: [10])],
            discovered:
            [
                new CalendarDiscoveredRow { OrgCompId = 10, Venue = "Haifa", Start = new(2026, 10, 9), End = new(2026, 10, 9) },
                new CalendarDiscoveredRow { OrgCompId = 50, Venue = "Netanya", Start = new(2026, 10, 9), End = new(2026, 10, 9) },
            ]);
        Assert.Equal("Netanya", Assert.Single(cal.Meets).Venue);
    }

    [Theory]
    [InlineData(8, 11, "8–11")]
    [InlineData(17, null, "17+")]
    [InlineData(null, 10, "≤10")]
    [InlineData(null, null, null)]
    public void AgesLabel_formats(int? min, int? max, string? expected) =>
        Assert.Equal(expected, CompetitionCalendarBuilder.AgesLabel(new CalendarCategoryBand(min, max)));

    // ── «Входящие» в календаре (часть В: В9 — будущие старты, В4 — перенос и отмена) ──────────

    private static CalendarDiscoveredRow Disc(int id, string name, string start, string? end = null,
        string? venue = null, string? prevStart = null, string? prevEnd = null, bool missing = false,
        string status = "new", string discipline = Disciplines.Swimming) => new()
    {
        OrgCompId = id,
        Name = name,
        Venue = venue,
        Start = DateOnly.Parse(start),
        End = DateOnly.Parse(end ?? start),
        PreviousStart = prevStart is null ? null : DateOnly.Parse(prevStart),
        PreviousEnd = prevEnd is null ? null : DateOnly.Parse(prevEnd),
        Missing = missing,
        Status = status,
        Discipline = discipline,
    };

    [Fact]
    public void Discovered_row_without_imported_or_entries_becomes_upcoming_with_source_url()
    {
        var cal = Build(discovered: [Disc(16950, "ליגה מס 2 צעירים", "2026-11-14", "2026-11-15", venue: "בריכת וינגייט")]);

        var m = Assert.Single(cal.Meets);
        Assert.Equal("upcoming:16950", m.Key);
        Assert.Equal(CalendarMeetKinds.Upcoming, m.Kind);
        Assert.Equal(16950, m.Id);
        Assert.Equal("ליגה מס 2 צעירים", m.Name);
        Assert.Equal(["2026-11-14", "2026-11-15"], m.Days.Select(d => d.Date));
        Assert.All(m.Days, d => Assert.False(d.Loaded));
        Assert.Equal(CalendarMeetStatuses.Upcoming, m.Status);
        Assert.Equal("", m.Pool);
        Assert.Equal("בריכת וינגייט", m.Venue);
        Assert.Equal(CalendarMeetTypes.Pool, m.Type);
        Assert.False(m.HasStartList);
        Assert.Equal("Y", m.Group!.Letter);
        Assert.Equal("https://isr.org.il/comp.asp?compID=16950", m.SourceUrl);
    }

    [Fact]
    public void Discovered_long_span_gives_only_start_and_end()
    {
        var m = Assert.Single(Build(discovered: [Disc(1, "סדרת גביע", "2026-11-01", "2026-11-30")]).Meets);
        Assert.Equal(["2026-11-01", "2026-11-30"], m.Days.Select(d => d.Date));
        Assert.Equal("2026-11-30", m.End);
    }

    [Fact]
    public void Discovered_row_claimed_by_compId_is_not_duplicated()
    {
        var cal = Build(
            comps: [Day(1, "2026-10-09", name: "ליגה מס 1", orgCompId: 16845)],
            discovered: [Disc(16845, "ליגה מס 1 - מחוז מרכז", "2026-10-09", venue: "Netanya")]);

        var m = Assert.Single(cal.Meets);
        Assert.Equal("competition:1", m.Key);
        Assert.Null(m.SourceUrl); // затянутому ссылка наружу не нужна
    }

    [Fact]
    public void Discovered_row_matched_by_name_and_date_is_not_duplicated_and_lends_venue()
    {
        // Протокол затянут PDF-импортом без compID: шов — имя+дата, как в /Admin/Competitions.
        var cal = Build(
            comps: [Day(1, "2026-10-09", name: "אליפות \\\"ארנה\\\" ילדים")],
            discovered: [Disc(16846, "אליפות \"ארנה\" ילדים - מחוז צפון", "2026-10-09", venue: "Haifa")]);

        var m = Assert.Single(cal.Meets);
        Assert.Equal("competition:1", m.Key);
        Assert.Equal("Haifa", m.Venue);
    }

    [Fact]
    public void Same_name_other_date_is_not_a_match()
    {
        var cal = Build(
            comps: [Day(1, "2026-10-09", name: "ליגה מס 1")],
            discovered: [Disc(16847, "ליגה מס 1", "2026-12-09")]);
        Assert.Equal(["competition:1", "upcoming:16847"], cal.Meets.Select(m => m.Key));
    }

    [Fact]
    public void Discovered_row_claimed_by_entries_stays_one_meet_with_source_url()
    {
        var cal = Build(
            entries: [new CalendarEntryRow { OrgCompId = 16900, Date = new(2026, 10, 24), CompName = "גביע" }],
            discovered: [Disc(16900, "גביע", "2026-10-24")]);
        var m = Assert.Single(cal.Meets);
        Assert.Equal("upcoming:16900", m.Key);
        Assert.True(m.HasStartList);
        Assert.Equal("https://isr.org.il/comp.asp?compID=16900", m.SourceUrl);
    }

    [Fact]
    public void Entries_upcoming_without_discovered_row_has_no_source_url() =>
        Assert.Null(Assert.Single(Build(
            entries: [new CalendarEntryRow { OrgCompId = 1, Date = new(2026, 11, 1), CompName = "x" }]).Meets).SourceUrl);

    [Fact]
    public void Ignored_discovered_row_is_not_shown()
    {
        var cal = Build(discovered:
        [
            Disc(1, "מסיבת סיום", "2026-11-01", status: "ignored"),
            Disc(2, "פוסטפון", "2026-11-20", prevStart: "2026-11-05", status: "ignored"),
        ]);
        Assert.Empty(cal.Meets);
    }

    [Fact]
    public void Discovered_outside_season_is_dropped() =>
        Assert.Empty(Build(discovered: [Disc(1, "קיץ", "2027-09-05")]).Meets);

    [Fact]
    public void Discovered_championship_by_name_unless_meet_info_says_otherwise()
    {
        var cal = Build(
            discovered:
            [
                Disc(1, "אליפות \"ארנה\" ישראל לילדים 8-11 חורף", "2026-12-10"),
                Disc(2, "אליפות ישראל לנוער", "2026-12-20"),
                Disc(3, "אליפות מכבי חדרה", "2026-12-30"),
            ],
            meetInfo: new Dictionary<int, bool> { [2] = false }); // ручная правка «не чемпионат» сильнее имени

        Assert.Equal([true, false, false], cal.Meets.Select(m => m.IsChampionship));
        Assert.Equal(StandingKinds.Winter, cal.Meets[0].Group!.Season);
    }

    [Fact]
    public void Discovered_artistic_and_open_water_types()
    {
        var cal = Build(discovered:
        [
            Disc(1, "תחרות", "2026-11-01", discipline: Disciplines.Artistic),
            Disc(2, "צליחת הכנרת", "2026-11-02"),
        ]);
        Assert.Equal([CalendarMeetTypes.Artistic, CalendarMeetTypes.OpenWater], cal.Meets.Select(m => m.Type));
    }

    [Fact]
    public void Postponed_row_adds_ghost_on_previous_dates()
    {
        var cal = Build(discovered:
        [
            Disc(16960, "גביע הנוער", "2026-12-12", "2026-12-13", venue: "בריכת עמידר",
                prevStart: "2026-11-14", prevEnd: "2026-11-15"),
        ]);

        Assert.Equal(["postponed:16960", "upcoming:16960"], cal.Meets.Select(m => m.Key));
        var ghost = cal.Meets[0];
        Assert.Equal(CalendarMeetKinds.Upcoming, ghost.Kind);
        Assert.Equal(16960, ghost.Id);
        Assert.Equal(CalendarMeetStatuses.Postponed, ghost.Status);
        Assert.Equal("2026-12-12", ghost.PostponedTo);
        Assert.Equal("2026-11-14", ghost.Start);
        Assert.Equal("2026-11-15", ghost.End);
        Assert.Equal(2, ghost.DaysTotal);
        Assert.Equal("גביע הנוער", ghost.Name);
        Assert.Equal("בריכת עמידר", ghost.Venue);
        Assert.Equal("J", ghost.Group!.Letter);
        Assert.Equal("https://isr.org.il/comp.asp?compID=16960", ghost.SourceUrl);

        var meet = cal.Meets[1];
        Assert.Equal(CalendarMeetStatuses.Upcoming, meet.Status);
        Assert.Null(meet.PostponedTo);
        Assert.Equal("2026-12-12", meet.Start);
    }

    [Fact]
    public void Postponed_ghost_stays_in_its_season_when_start_moved_out()
    {
        // Перенесли с августа на сентябрь: призрак — в сезоне 2026/27, сам старт — в следующем.
        var cal = Build(discovered: [Disc(5, "גביע קיץ", "2027-09-05", prevStart: "2027-08-20")]);
        var ghost = Assert.Single(cal.Meets);
        Assert.Equal("postponed:5", ghost.Key);
        Assert.Equal("2027-09-05", ghost.PostponedTo);
    }

    [Fact]
    public void Postponed_imported_meet_ghost_copies_meet_fields()
    {
        var cal = Build(
            comps: [Day(1, "2026-10-30", name: "ליגה מס 3", orgCompId: 70, pool: "50m", categories: ["results-main"])],
            discovered: [Disc(70, "ליגה מס 3", "2026-10-30", prevStart: "2026-10-16")]);

        Assert.Equal(["postponed:70", "competition:1"], cal.Meets.Select(m => m.Key));
        var ghost = cal.Meets[0];
        Assert.Equal("50m", ghost.Pool);
        Assert.Equal("A", ghost.Group!.Letter);
        Assert.Equal(CalendarMeetStatuses.Upcoming, cal.Meets[1].Status); // сам старт — по датам, не postponed
    }

    [Fact]
    public void Missing_discovered_row_is_cancelled()
    {
        var cal = Build(
            entries: [new CalendarEntryRow { OrgCompId = 2, Date = new(2026, 11, 8), CompName = "ליגה" }],
            discovered:
            [
                Disc(1, "ליגה אזורית", "2026-11-07", missing: true),
                Disc(2, "ליגה", "2026-11-08", missing: true),     // по заявкам, но с сайта пропал
                Disc(3, "גביע", "2026-11-09"),
            ]);
        Assert.Equal(
            [CalendarMeetStatuses.Cancelled, CalendarMeetStatuses.Cancelled, CalendarMeetStatuses.Upcoming],
            cal.Meets.Select(m => m.Status));
    }

    [Fact]
    public void Imported_meet_is_never_cancelled()
    {
        var cal = Build(
            comps: [Day(1, "2026-10-09", orgCompId: 9)],
            discovered: [Disc(9, "ליגה", "2026-10-09", missing: true)]);
        Assert.Equal(CalendarMeetStatuses.Done, Assert.Single(cal.Meets).Status);
    }

    // ── Матч «уже затянуто» по имени+дате (DiscoveryNameMatch) ─────────────────

    [Theory]
    [InlineData(" ליגה  מס 4 ", "ליגה מס 4", true)]                       // пробелы
    [InlineData("ליגה מס 4 - מחוז צפון", "ליגה מס 4", true)]             // суффикс района
    [InlineData("אליפות \"ארנה\" 2026", "אליפות \\\"ארנה\\\" 2026", true)] // кавычки и бэкслеши
    [InlineData("ליגה מס 4", "ליגה מס 4 - מחוז צפון", false)]            // наоборот — нет
    [InlineData("ליגה", "", false)]                                       // пустое имя дня
    public void NameMatch_rules(string discovered, string competition, bool expected) =>
        Assert.Equal(expected, DiscoveryNameMatch.Matches(discovered, new(2026, 7, 3), new(2026, 7, 4), competition, new(2026, 7, 4)));

    [Fact]
    public void NameMatch_needs_date_inside_span() =>
        Assert.False(DiscoveryNameMatch.Matches("ליגה", new(2026, 7, 3), new(2026, 7, 4), "ליגה", new(2026, 7, 5)));
}

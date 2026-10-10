using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Карточка «Top clubs» (Overview): клубный зачёт, бейдж сверки, заметка о расхождении,
/// шкала «Points system». Разбивка ♂/♀ — в ResultRepositoryTests, чип «Unofficial» — в
/// OverviewClubPointsUnofficialTests, сами очки клуба — в тестах GetClubSummaryAsync.
/// Правила — docs/competition-overview-cards.md, «Top clubs».
/// </summary>
public class OverviewTopClubsCardTests
{
    private const int EventId = 7;

    private static PointRuleClubs ClubRule(string version, bool manualOnly = false, params (int Place, int Points)[] scale) => new()
    {
        Version = version, Scope = "all", EffectiveFrom = new DateOnly(2020, 1, 1), DefaultPoints = 0,
        ManualOnly = manualOnly,
        Entries = scale.Select(s => new PointRuleClubsEntry { Place = s.Place, Points = s.Points }).ToList()
    };

    /// <summary>День события со своей строкой результата — у обзора по eventId в выборке все дни.</summary>
    private static async Task<Competition> AddDayAsync(OverviewTestKit kit, int day, Action<Competition>? setup = null)
    {
        var comp = await kit.AddCompetitionAsync(c =>
        {
            c.EventId = EventId;
            c.DayNumber = day;
            setup?.Invoke(c);
        });
        var s = await kit.AddSwimmerAsync($"Day{day}");
        await kit.AddRowsAsync(kit.Row(comp, s, position: 1));
        return comp;
    }

    /// <summary>
    /// Бейдж «★ Differs from official — ours verified» — только если итог сверки одинаков у
    /// ВСЕХ дней выборки: «сверено» на одном дне и «расхождение» на другом в один бейдж не
    /// складываются.
    /// </summary>
    [Theory]
    [InlineData(PointsVerifiedKinds.Mismatch, PointsVerifiedKinds.Mismatch, PointsVerifiedKinds.Mismatch)]
    [InlineData(PointsVerifiedKinds.Mismatch, PointsVerifiedKinds.Official, null)]
    [InlineData(PointsVerifiedKinds.Official, null, null)]
    [InlineData(PointsVerifiedKinds.Accepted, PointsVerifiedKinds.Accepted, PointsVerifiedKinds.Accepted)]
    public async Task Verified_ShownOnlyWhenSameOnAllDays(string? day1, string? day2, string? expected)
    {
        await using var kit = await OverviewTestKit.CreateAsync($"{nameof(Verified_ShownOnlyWhenSameOnAllDays)}-{day1}-{day2}");
        await AddDayAsync(kit, 1, c => c.ClubPointsVerifiedKind = day1);
        await AddDayAsync(kit, 2, c => c.ClubPointsVerifiedKind = day2);

        var overview = await kit.OverviewAsync(new ResultFilter { EventId = EventId });

        Assert.Equal(expected, overview.ClubPointsVerified);
    }

    private static CompetitionNote MismatchNote(int competitionId, string en, string? url = null) => new()
    {
        CompetitionId = competitionId,
        Kind = CompetitionNoteKinds.ClubPointsMismatch,
        ScaleDiffJson = """[{"place":21,"expected":5,"actual":6,"swimmer":"Alpha"}]""",
        ScaleDiffCaption = "50 m backstroke · girls 17-18",
        SourceUrl = url,
        Texts = [new CompetitionNoteText { Lang = "en", Body = en }]
    };

    /// <summary>
    /// Заметка «чем официальные очки неверны» — к бейджу расхождения. У многодневки она
    /// записана каждому дню одинаковой — это одна заметка, отдаём её.
    /// </summary>
    [Fact]
    public async Task MismatchNote_SameOnAllDays_IsReturned()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(MismatchNote_SameOnAllDays_IsReturned));
        var d1 = await AddDayAsync(kit, 1, c => c.ClubPointsVerifiedKind = PointsVerifiedKinds.Mismatch);
        var d2 = await AddDayAsync(kit, 2, c => c.ClubPointsVerifiedKind = PointsVerifiedKinds.Mismatch);
        kit.Db.AddRange(
            MismatchNote(d1.Id, "Place 21 paid 6 instead of 5.", "https://loglig.com/rules.pdf"),
            MismatchNote(d2.Id, "Place 21 paid 6 instead of 5.", "https://loglig.com/rules.pdf"));
        await kit.Db.SaveChangesAsync();

        var note = (await kit.OverviewAsync(new ResultFilter { EventId = EventId })).ClubPointsMismatchNote;

        Assert.NotNull(note);
        Assert.Equal("Place 21 paid 6 instead of 5.", note!.Texts["en"]);
        Assert.Equal("50 m backstroke · girls 17-18", note.ScaleDiffCaption);
        var diff = Assert.Single(note.ScaleDiff);
        Assert.Equal(21, diff.Place);
        Assert.Equal("https://loglig.com/rules.pdf", note.SourceUrl);
    }

    /// <summary>Два разных объяснения в одном попапе противоречили бы друг другу — не отдаём ни одно.</summary>
    [Fact]
    public async Task MismatchNote_DiffersBetweenDays_IsNull()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(MismatchNote_DiffersBetweenDays_IsNull));
        var d1 = await AddDayAsync(kit, 1, c => c.ClubPointsVerifiedKind = PointsVerifiedKinds.Mismatch);
        var d2 = await AddDayAsync(kit, 2, c => c.ClubPointsVerifiedKind = PointsVerifiedKinds.Mismatch);
        kit.Db.AddRange(MismatchNote(d1.Id, "One story."), MismatchNote(d2.Id, "Another story."));
        await kit.Db.SaveChangesAsync();

        var overview = await kit.OverviewAsync(new ResultFilter { EventId = EventId });

        Assert.Equal(PointsVerifiedKinds.Mismatch, overview.ClubPointsVerified);
        Assert.Null(overview.ClubPointsMismatchNote);
    }

    /// <summary>
    /// Заметка без бейджа расхождения не показывается: объясняет расхождение, которого витрина
    /// не заявляет. А ссылка не-http(s) отбрасывается и на выводе — она идёт в href (XSS).
    /// </summary>
    [Fact]
    public async Task MismatchNote_OnlyWithMismatch_UnsafeUrlDropped()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(MismatchNote_OnlyWithMismatch_UnsafeUrlDropped));
        var official = await kit.AddCompetitionAsync(c => c.ClubPointsVerifiedKind = PointsVerifiedKinds.Official);
        var mismatch = await kit.AddCompetitionAsync(c => c.ClubPointsVerifiedKind = PointsVerifiedKinds.Mismatch);
        var s = await kit.AddSwimmerAsync("S");
        await kit.AddRowsAsync(kit.Row(official, s, position: 1), kit.Row(mismatch, s, position: 1));
        kit.Db.AddRange(
            MismatchNote(official.Id, "Stale note."),
            MismatchNote(mismatch.Id, "Real note.", "javascript:alert(1)"));
        await kit.Db.SaveChangesAsync();

        Assert.Null((await kit.OverviewAsync(official)).ClubPointsMismatchNote);
        var note = (await kit.OverviewAsync(mismatch)).ClubPointsMismatchNote;
        Assert.Equal("Real note.", note!.Texts["en"]);
        Assert.Null(note.SourceUrl);
    }

    /// <summary>
    /// «Points system» показывает правила, по которым зачёт РЕАЛЬНО посчитан, — тем же
    /// выбором, что и расчёт: привязка дня важнее подбора по дате, ManualOnly в подборе не
    /// участвует. Одинаковые правила дней схлопываются, порядок — по версии, шкала — по месту.
    /// </summary>
    [Fact]
    public async Task PointsRules_ResolvedLikeTheCalculation_DistinctAndOrdered()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(PointsRules_ResolvedLikeTheCalculation_DistinctAndOrdered));
        var pinned = ClubRule("b-pinned", manualOnly: true, (2, 15), (1, 20));
        var auto = ClubRule("a-auto", manualOnly: false, (1, 9));
        var unused = ClubRule("z-manual", manualOnly: true, (1, 1));
        kit.Db.AddRange(pinned, auto, unused);
        await kit.Db.SaveChangesAsync();

        await AddDayAsync(kit, 1, c => c.PointRuleClubsId = pinned.Id);
        await AddDayAsync(kit, 2);                                    // без привязки → подбор по дате
        await AddDayAsync(kit, 3, c => c.PointRuleClubsId = pinned.Id);

        var rules = (await kit.OverviewAsync(new ResultFilter { EventId = EventId })).ClubPointsRules;

        Assert.Equal(new[] { "a-auto", "b-pinned" }, rules.Select(r => r.Version));
        Assert.Equal(new[] { 1, 2 }, rules[1].PointsByPlace.Select(e => e.Place));
        Assert.Equal(2, rules[1].RelayMultiplier);
    }

    /// <summary>Общий зачёт — топ-10, по полу — топ-3 (полный список — таб Clubs).</summary>
    [Fact]
    public async Task TopClubs_Top10_GenderPanelsTop3()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(TopClubs_Top10_GenderPanelsTop3));
        var rule = ClubRule("scale", manualOnly: false, Enumerable.Range(1, 12).Select(p => (p, 100 - p)).ToArray());
        kit.Db.Add(rule);
        await kit.Db.SaveChangesAsync();
        var comp = await kit.AddCompetitionAsync(c => c.PointRuleClubsId = rule.Id);

        var rows = new List<ResultRecord>();
        Club? first = null;
        for (var place = 1; place <= 12; place++)
        {
            var club = await kit.AddClubAsync($"Club{place:00}");
            first ??= club;
            var s = await kit.AddSwimmerAsync($"S{place}");
            rows.Add(kit.Row(comp, s, position: place, club: club));
        }
        await kit.AddRowsAsync(rows.ToArray());

        var overview = await kit.OverviewAsync(comp);

        Assert.Equal(10, overview.TopClubs.Count);
        Assert.Equal(3, overview.TopClubsMen.Count);
        Assert.Empty(overview.TopClubsWomen);
        Assert.Equal(first!.Id, overview.TopClubs[0].ClubId);   // «Champion club» = top_clubs[0]
        Assert.Equal(99, overview.TopClubs[0].Points);
    }

    /// <summary>
    /// Тоггл «Combine All Results» обязан доехать и до зачёта по полу: иначе панель ♂ на том
    /// же экране показывала бы протокольного лидера рядом с объединённым в общем зачёте.
    /// </summary>
    [Fact]
    public async Task Combined_ReachesGenderPanelsToo()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Combined_ReachesGenderPanelsToo));
        var rule = ClubRule("scale", manualOnly: false, (1, 10), (2, 5));
        kit.Db.Add(rule);
        await kit.Db.SaveChangesAsync();
        var comp = await kit.AddCompetitionAsync(c =>
        {
            c.PointRuleClubsId = rule.Id;
            c.ShowCombineAllResults = true;
        });
        var clubA = await kit.AddClubAsync("A");
        var clubB = await kit.AddClubAsync("B");
        var a = await kit.AddSwimmerAsync("A1");
        var b = await kit.AddSwimmerAsync("B1");
        var rowA = kit.Row(comp, a, position: 1, club: clubA);
        rowA.CombinedPlace = 2;
        var rowB = kit.Row(comp, b, position: 2, club: clubB);
        rowB.CombinedPlace = 1;
        await kit.AddRowsAsync(rowA, rowB);

        var protocol = await kit.OverviewAsync(comp);
        var combined = await kit.OverviewAsync(comp, combined: true);

        Assert.Equal("A", protocol.TopClubs[0].Club);
        Assert.Equal("A", protocol.TopClubsMen[0].Club);
        Assert.Equal("B", combined.TopClubs[0].Club);
        Assert.Equal("B", combined.TopClubsMen[0].Club);
    }
}

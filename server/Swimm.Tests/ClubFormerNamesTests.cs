using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Прежние названия клуба и цепочки склеек — чистые правила (docs/plans/swimmer-club-change-plan.md
/// §5): размотка цепочек (Н4), индекс старых названий для импорта (Н5), «Formerly: …» в шапке и
/// SEO (Н6), «then: …» в окне истории клубов пловца (Н8).
/// </summary>
public class ClubFormerNamesTests
{
    // ── Н4: цепочки ───────────────────────────────────────────────────────────

    private static readonly Dictionary<int, int?> ProdChains = new()
    {
        [1454] = null,          // Aluma — живой
        [1372] = 1454,          // Hapoel → Aluma (после склейки-переименования)
        [605] = 1372, [1329] = 605,
        [447] = 1372, [1361] = 447, [1408] = 447,
        [438] = null,
    };

    [Fact]
    public void FinalOf_WalksChainToLiveClub()
    {
        Assert.Equal(1454, ClubMergeChains.FinalOf(1329, ProdChains));
        Assert.Equal(1454, ClubMergeChains.FinalOf(1408, ProdChains));
        Assert.Equal(1454, ClubMergeChains.FinalOf(1454, ProdChains));
        Assert.Null(ClubMergeChains.FinalOf(9999, ProdChains));
    }

    [Fact]
    public void FinalOf_Loop_IsNull()
    {
        var loop = new Dictionary<int, int?> { [1] = 2, [2] = 1 };
        Assert.Null(ClubMergeChains.FinalOf(1, loop));
    }

    [Fact]
    public void Plan_MovesOnlyTombstonesNotPointingAtLiveClub()
    {
        var plan = ClubMergeChains.Plan(ProdChains);

        Assert.Equal(
            [new ClubChainMove(447, 1372, 1454), new ClubChainMove(605, 1372, 1454),
             new ClubChainMove(1329, 605, 1454), new ClubChainMove(1361, 447, 1454),
             new ClubChainMove(1408, 447, 1454)],
            plan.Moves);
        Assert.Empty(plan.Broken);
    }

    [Fact]
    public void Plan_LoopIsBroken_NotMoved()
    {
        var plan = ClubMergeChains.Plan(new Dictionary<int, int?> { [1] = 2, [2] = 1, [3] = null, [4] = 3 });
        Assert.Empty(plan.Moves);
        Assert.Equal([1, 2], plan.Broken);
    }

    // ── Н5: индекс старых названий ────────────────────────────────────────────

    private static ClubAliasIndex HodHasharon() => new(
        [
            new ClubAliasClub(1454, "אלומה עירוני הוד השרון", "", null, false),
            new ClubAliasClub(1372, "הפועל עירוני הוד השרון", "Hapoel Hod Hasharon", 1454, false),
            new ClubAliasClub(605, "הפועל הוד השרון", "", 1372, false),
            new ClubAliasClub(900, "Israel", "Israel", null, true),
            new ClubAliasClub(901, "ישראל", "", 900, true),
        ],
        [new ClubAliasName(7, 1454, "הפועל עירוני הוד השרון", "Hapoel Hod Hasharon", ClubNameKinds.Former)]);

    [Fact]
    public void Alias_FormerName_GivesCanonicalAndClubNameId()
    {
        Assert.Equal(new ClubAliasHit(1454, 7), HodHasharon().Find("הפועל עירוני הוד השרון", null));
        // Только английское в протоколе — по нему.
        Assert.Equal(new ClubAliasHit(1454, 7), HodHasharon().Find("", "hapoel hod hasharon"));
    }

    [Fact]
    public void Alias_TombstoneName_ThroughChain_GivesFinalCanonical_WithoutClubNameId()
    {
        Assert.Equal(new ClubAliasHit(1454, null), HodHasharon().Find(" הפועל הוד השרון ", null));
    }

    [Fact]
    public void Alias_UnknownOrPseudo_IsNull()
    {
        Assert.Null(HodHasharon().Find("מכבי חיפה", null));
        Assert.Null(HodHasharon().Find("ישראל", null)); // конец цепочки — псевдоклуб
        Assert.Null(HodHasharon().Find("", ""));
    }

    [Fact]
    public void Alias_SpellingKind_GivesClubWithoutClubNameId()
    {
        var index = new ClubAliasIndex(
            [new ClubAliasClub(1, "SWIM TLV", "", null, false)],
            [new ClubAliasName(3, 1, "TLV SWIM", "", ClubNameKinds.Spelling)]);
        Assert.Equal(new ClubAliasHit(1, null), index.Find("TLV SWIM", null));
    }

    // ── Н6: «Formerly: …» ────────────────────────────────────────────────────

    private static readonly ClubFormerNameRow HapoelRow = new(
        7, "הפועל עירוני הוד השרון", "Hapoel Hod Hasharon", new DateTime(2024, 10, 25), new DateTime(2026, 2, 16), ClubNameKinds.Former);

    [Fact]
    public void ToDtos_OnlyFormer_NewestFirst_YearsFromDates()
    {
        var dtos = ClubFormerNames.ToDtos(
        [
            new ClubFormerNameRow(1, "ישן", "", new DateTime(2019, 1, 1), new DateTime(2020, 5, 1), ClubNameKinds.Former),
            HapoelRow,
            new ClubFormerNameRow(9, "TLV SWIM", "", null, null, ClubNameKinds.Spelling),
        ]);

        Assert.Equal(["הפועל עירוני הוד השרון", "ישן"], dtos.Select(d => d.Name));
        Assert.Equal((2024, 2026), (dtos[0].FromYear!.Value, dtos[0].ToYear!.Value));
        Assert.Equal("Hapoel Hod Hasharon", dtos[0].NameEn);
        Assert.Null(dtos[1].NameEn);
    }

    [Theory]
    [InlineData(2024, 2026, "2024–2026")]
    [InlineData(2025, 2025, "2025")]
    [InlineData(2024, null, "2024")]
    [InlineData(null, null, null)]
    public void YearsLabel(int? from, int? to, string? expected) =>
        Assert.Equal(expected, ClubFormerNames.YearsLabel(from, to));

    [Fact]
    public void SeoDescription_MentionsFormerName_BeforeGenericTail()
    {
        var head = SeoText.Club(new SeoClubFacts(1454, "אלומה עירוני הוד השרון", "", false, null, 120,
            ClubFormerNames.ToDtos([HapoelRow])));

        Assert.StartsWith("אלומה עירוני הוד השרון: 120 swimmers. Formerly: הפועל עירוני הוד השרון (2024–2026).",
            head.Description);

        var plain = SeoText.Club(new SeoClubFacts(7, "מכבי חיפה", "Maccabi Haifa", false, null, 120));
        Assert.DoesNotContain("Formerly", plain.Description);
    }

    // ── Н8: «then: …» в истории клубов пловца ─────────────────────────────────

    private static SeasonSwimRow Swim(long id, string date, int clubId, int? clubNameId) => new(
        id, 1, 1, DateTime.Parse(date), 1, "100", "female", "25m", null, 60000, false, null, false)
    {
        ClubId = clubId,
        ClubNameId = clubNameId,
    };

    private static readonly IReadOnlyDictionary<int, SwimmerClubRef> AlumaClubs = new Dictionary<int, SwimmerClubRef>
    {
        [1454] = new(1454, "אלומה עירוני הוד השרון", "", false) { FormerNames = [HapoelRow] },
    };

    [Fact]
    public void History_SeasonUnderFormerName_ShowsThenName_OneClubNoBadge()
    {
        var history = SwimmerClubHistory.Build(
        [
            Swim(1, "2024-11-10", 1454, 7),
            Swim(2, "2025-03-10", 1454, 7),
            Swim(3, "2026-11-05", 1454, null),
        ], AlumaClubs);

        Assert.Equal(1, SwimmerClubHistory.ClubCount(history)); // Hapoel и Aluma — один клуб, значка ⇄ нет
        var old = history.Single(h => h.Season == 2024);
        Assert.Equal("אלומה עירוני הוד השרון", old.Name);
        Assert.Equal("הפועל עירוני הוד השרון", old.ThenName);
        Assert.Equal("Hapoel Hod Hasharon", old.ThenNameEn);
        Assert.Null(history.Single(h => h.Season == 2026).ThenName);
    }

    [Fact]
    public void History_MixedSeason_ThenNameOnlyWhenFormerPrevails()
    {
        // 2025/26: Aluma с 24.10.2025, Hapoel — до 16.02.2026, внахлёст.
        var mostlyNew = SwimmerClubHistory.Build(
            [Swim(1, "2025-10-24", 1454, null), Swim(2, "2025-12-01", 1454, null), Swim(3, "2026-02-16", 1454, 7)],
            AlumaClubs);
        Assert.Null(mostlyNew.Single().ThenName);

        var tie = SwimmerClubHistory.Build(
            [Swim(1, "2025-10-24", 1454, null), Swim(2, "2026-02-16", 1454, 7)], AlumaClubs);
        Assert.Equal("הפועל עירוני הוד השרון", tie.Single().ThenName);
    }
}

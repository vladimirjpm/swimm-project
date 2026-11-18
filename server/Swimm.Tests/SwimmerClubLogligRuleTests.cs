using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка кандидата на смену клуба с loglig (Р98) — все строки таблицы §6.2
/// docs/plans/swimmer-club-change-plan.md плюс сравнение названий через Normalize, прежние
/// названия (ClubNames) и склеенные клубы.
/// </summary>
public class SwimmerClubLogligRuleTests
{
    private const int Dolphin = 438, Raanana = 449, Shoham = 446, SwimTlv = 456, TlvSwimDup = 1500,
        HapoelHod = 1372, AlumaHod = 1454, Israel = 900;

    private static ClubNameMatcher Matcher(bool tlvMerged = true) => new(
        [
            new(Dolphin, "הפועל דולפין נתניה", "Hapoel Dolphin Netanya", null, false),
            new(Raanana, "ע.ל.ה עצמה רעננה", "", null, false),
            new(Shoham, "מכבי שוהם", "Maccabi Shoham", null, false),
            new(SwimTlv, "SWIM TLV", "SWIM TLV", null, false),
            new(TlvSwimDup, "TLV SWIM", "TLV SWIM", tlvMerged ? SwimTlv : null, false),
            new(HapoelHod, "הפועל עירוני הוד השרון", "", AlumaHod, false),
            new(AlumaHod, "אלומה עירוני הוד השרון", "Aluma Hod Hasharon", null, false),
            new(Israel, "ישראל", "Israel", null, true),
        ],
        [new(1, AlumaHod, "הפועל עירוני הוד השרון", "", ClubNameKinds.Former)]);

    private static string Decide(string? logligClub, int? from = Dolphin, int to = Raanana, int? logligId = 123,
        bool fetched = true, bool tlvMerged = true) =>
        SwimmerClubLogligRule.Decide(logligId, new LogligClubAnswer(fetched, logligClub), from, to, Matcher(tlvMerged));

    // ── Таблица §6.2 ─────────────────────────────────────────────────────────

    [Fact]
    public void LogligShowsNewClub_Confirmed() =>
        Assert.Equal(SwimmerClubLogligVerdicts.Confirmed, Decide("ע.ל.ה עצמה רעננה"));

    [Fact]
    public void LogligShowsOldClub_Waits() =>
        Assert.Equal(SwimmerClubLogligVerdicts.StillOld, Decide("הפועל דולפין נתניה"));

    [Fact]
    public void LogligArchive_Waits_ArchiveList() =>
        Assert.Equal(SwimmerClubLogligVerdicts.Archive, Decide(SwimmerClubLogligRule.ArchiveName));

    [Fact]
    public void LogligThirdClub_Waits_OtherList() =>
        Assert.Equal(SwimmerClubLogligVerdicts.OtherClub, Decide("מכבי שוהם"));

    [Fact]
    public void LogligUnknownName_IsOtherClub() =>
        Assert.Equal(SwimmerClubLogligVerdicts.OtherClub, Decide("מועדון שאין אצלנו"));

    [Fact]
    public void NoLogligId_Waits() =>
        Assert.Equal(SwimmerClubLogligVerdicts.NoLogligId, Decide("ע.ל.ה עצמה רעננה", logligId: null));

    [Fact]
    public void CardDidNotOpen_Waits() =>
        Assert.Equal(SwimmerClubLogligVerdicts.Unavailable, Decide(null, fetched: false));

    [Fact]
    public void NetworkError_NoAnswer_Waits() =>
        Assert.Equal(SwimmerClubLogligVerdicts.Unavailable,
            SwimmerClubLogligRule.Decide(123, null, Dolphin, Raanana, Matcher()));

    [Fact]
    public void CardWithoutClub_Waits() =>
        Assert.Equal(SwimmerClubLogligVerdicts.Unavailable, Decide("  "));

    [Theory]
    [InlineData(SwimmerClubLogligVerdicts.Confirmed, true)]
    [InlineData(SwimmerClubLogligVerdicts.StillOld, false)]
    [InlineData(SwimmerClubLogligVerdicts.Archive, false)]
    [InlineData(SwimmerClubLogligVerdicts.OtherClub, false)]
    [InlineData(SwimmerClubLogligVerdicts.NoLogligId, false)]
    [InlineData(SwimmerClubLogligVerdicts.Unavailable, false)]
    [InlineData(null, false)]
    public void OnlyConfirmed_Applies(string? verdict, bool applies) =>
        Assert.Equal(applies, SwimmerClubLogligRule.Applies(verdict));

    // ── Сравнение названий ───────────────────────────────────────────────────

    [Fact]
    public void TlvSwim_MatchesSwimTlv_AfterMerge() =>
        Assert.Equal(SwimmerClubLogligVerdicts.Confirmed, Decide("TLV SWIM", from: Shoham, to: SwimTlv));

    [Fact]
    public void TlvSwim_WithoutMerge_IsAnotherClub() =>
        Assert.Equal(SwimmerClubLogligVerdicts.OtherClub, Decide("TLV SWIM", from: Shoham, to: SwimTlv, tlvMerged: false));

    [Fact]
    public void Normalize_IgnoresCaseSpacesFinalLettersAndGeresh()
    {
        Assert.Equal(SwimmerClubLogligVerdicts.Confirmed, Decide("  swim   tlv ", from: Shoham, to: SwimTlv));
        // Финальная буква и гереш: «מכבי שוהמ» = «מכבי שוהם».
        Assert.Equal(SwimmerClubLogligVerdicts.Confirmed, Decide("מכבי שוהמ", from: Dolphin, to: Shoham));
    }

    [Fact]
    public void EnglishName_AlsoMatches() =>
        Assert.Equal(SwimmerClubLogligVerdicts.StillOld, Decide("Hapoel Dolphin Netanya"));

    [Fact]
    public void FormerName_MatchesRenamedClub()
    {
        // loglig ещё пишет старое название Hapoel, а у нас клуб уже Aluma — это тот же клуб.
        Assert.Equal(SwimmerClubLogligVerdicts.Confirmed, Decide("הפועל עירוני הוד השרון", from: Dolphin, to: AlumaHod));
        // Карточка указывает на надгробие 1372 — сводится к канону 1454, loglig = «старый» = тот же.
        Assert.Equal(SwimmerClubLogligVerdicts.StillOld, Decide("אלומה עירוני הוד השרון", from: HapoelHod, to: Raanana));
    }

    [Fact]
    public void CardOnMergedClub_WhoseCanonIsTheNewClub_IsNotAChange()
    {
        // Карточка на надгробии 1372, правило даёт его канон 1454 — loglig не нужен вовсе.
        Assert.Equal(SwimmerClubLogligVerdicts.Confirmed, Decide(null, from: HapoelHod, to: AlumaHod, logligId: null));
        Assert.Equal(SwimmerClubLogligVerdicts.Confirmed, Decide(SwimmerClubLogligRule.ArchiveName, from: HapoelHod, to: AlumaHod));
    }

    [Fact]
    public void PseudoClub_IsNotInIndex() =>
        Assert.Empty(Matcher().Find("Israel"));

    [Fact]
    public void NameMatchingBothOldAndNew_IsNotConfirmation()
    {
        var matcher = new ClubNameMatcher(
            [new(1, "Same", "", null, false), new(2, "Same", "", null, false)], []);
        Assert.Equal(SwimmerClubLogligVerdicts.StillOld,
            SwimmerClubLogligRule.Decide(5, new LogligClubAnswer(true, "Same"), 1, 2, matcher));
    }

    [Fact]
    public void NoClubInCard_NewClubOnLoglig_Confirmed() =>
        Assert.Equal(SwimmerClubLogligVerdicts.Confirmed, Decide("ע.ל.ה עצמה רעננה", from: null));

    // ── Кэш на сутки ─────────────────────────────────────────────────────────

    [Fact]
    public void Cache_ValidForADay_SameLogligId_WithClub()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(SwimmerClubLogligRule.CacheValid(now, now.AddHours(-23), 5, 5, "X"));
        Assert.False(SwimmerClubLogligRule.CacheValid(now, now.AddHours(-25), 5, 5, "X"));
        Assert.False(SwimmerClubLogligRule.CacheValid(now, now.AddHours(-1), 5, 6, "X"));
        Assert.False(SwimmerClubLogligRule.CacheValid(now, now.AddHours(-1), 5, 5, null));
        Assert.False(SwimmerClubLogligRule.CacheValid(now, null, 5, 5, "X"));
    }
}

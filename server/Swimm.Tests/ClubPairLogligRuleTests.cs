using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Что знает loglig про общих пловцов пары клубов-кандидатов в дубли (Р112): вердикт по пловцу,
/// сравнение названий (Normalize, прежние названия ClubNames, надгробия склеенных клубов), сводка
/// и свежесть ответа для кнопки «Проверить в loglig».
/// </summary>
public class ClubPairLogligRuleTests
{
    private const int HapoelJlm = 509, HapoelJlmArt = 1455, MaccabiJlm = 514, Raanana = 449,
        OldHod = 1372, AlumaHod = 1454, TlvSwimDup = 1500, SwimTlv = 456;

    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    private static ClubNameMatcher Matcher(bool sameNames = false) => new(
        [
            new(HapoelJlm, "הפועל ירושלים", "Hapoel Jerusalem", null, false),
            new(HapoelJlmArt, sameNames ? "הפועל ירושלים" : "הפועל ירושלים אומנותית", "", null, false),
            new(MaccabiJlm, "מכבי ירושלים", "", null, false),
            new(Raanana, "ע.ל.ה עצמה רעננה", "", null, false),
            new(OldHod, "הפועל עירוני הוד השרון", "", AlumaHod, false),
            new(AlumaHod, "אלומה עירוני הוד השרון", "", null, false),
            new(SwimTlv, "SWIM TLV", "", null, false),
            new(TlvSwimDup, "TLV SWIM", "", SwimTlv, false),
        ],
        [new(1, MaccabiJlm, "מכבי י-ם", "", ClubNameKinds.Former)]);

    private static string Classify(string? logligClub, int a = HapoelJlm, int b = HapoelJlmArt,
        int? logligId = 77, int storedLogligId = 77, bool stored = true, bool sameNames = false) =>
        ClubPairLogligRule.Classify(logligId,
            stored ? new StoredLogligClub(storedLogligId, logligClub, Now.AddDays(-3)) : null,
            a, b, Matcher(sameNames));

    // ── Вердикт по пловцу ────────────────────────────────────────────────────

    [Fact]
    public void LogligShowsClubA() => Assert.Equal(ClubPairLogligVerdicts.ClubA, Classify("הפועל ירושלים"));

    [Fact]
    public void LogligShowsClubB() => Assert.Equal(ClubPairLogligVerdicts.ClubB, Classify("הפועל ירושלים אומנותית"));

    [Fact]
    public void LogligShowsThirdClub_Other() => Assert.Equal(ClubPairLogligVerdicts.Other, Classify("ע.ל.ה עצמה רעננה"));

    [Fact]
    public void UnknownName_Other() => Assert.Equal(ClubPairLogligVerdicts.Other, Classify("מועדון שלא קיים"));

    [Fact]
    public void Archive() => Assert.Equal(ClubPairLogligVerdicts.Archive, Classify("ארכיון איגוד"));

    [Fact]
    public void NoLogligId_EvenWithStoredAnswer() =>
        Assert.Equal(ClubPairLogligVerdicts.NoLogligId, Classify("הפועל ירושלים", logligId: null));

    [Fact]
    public void NoStoredAnswer_Unchecked() =>
        Assert.Equal(ClubPairLogligVerdicts.Unchecked, Classify(null, stored: false));

    [Fact]
    public void AnswerForAnotherLogligId_Unchecked() =>
        Assert.Equal(ClubPairLogligVerdicts.Unchecked, Classify("הפועל ירושלים", storedLogligId: 78));

    [Fact]
    public void CardWithoutClub_Unchecked() => Assert.Equal(ClubPairLogligVerdicts.Unchecked, Classify("  "));

    [Fact]
    public void StaleAnswerStillCounts() =>
        // Ответ трёхдневной давности — тоже ответ: свежесть решает только, идти ли снова в сеть.
        Assert.Equal(ClubPairLogligVerdicts.ClubA, Classify("הפועל ירושלים"));

    [Fact]
    public void SameNameForBothClubs_Both() =>
        Assert.Equal(ClubPairLogligVerdicts.Both, Classify("הפועל ירושלים", sameNames: true));

    // ── Сравнение названий: Normalize, ClubNames, склеенные ───────────────────

    [Fact]
    public void Normalize_FinalLettersSpacesCase() =>
        // «ם» в конце vs «מ», лишние пробелы и регистр — одно название.
        Assert.Equal(ClubPairLogligVerdicts.ClubA, Classify("  הפועל   ירושלימ "));

    [Fact]
    public void Normalize_LatinNameEnCaseInsensitive() =>
        Assert.Equal(ClubPairLogligVerdicts.ClubA, Classify("HAPOEL JERUSALEM"));

    [Fact]
    public void FormerName_FromClubNames() =>
        Assert.Equal(ClubPairLogligVerdicts.ClubB, Classify("מכבי י-ם", a: HapoelJlm, b: MaccabiJlm));

    [Fact]
    public void MergedTombstoneName_CountsAsCanon() =>
        // loglig пишет старое название, а клуб у нас уже склеен в Aluma — это клуб A пары (Aluma).
        Assert.Equal(ClubPairLogligVerdicts.ClubA, Classify("הפועל עירוני הוד השרון", a: AlumaHod, b: Raanana));

    [Fact]
    public void PairClubIsTombstone_ResolvedToCanon() =>
        // Пара указывает на надгробие (TLV SWIM → SWIM TLV): название канона подходит.
        Assert.Equal(ClubPairLogligVerdicts.ClubB, Classify("SWIM TLV", a: Raanana, b: TlvSwimDup));

    // ── Сводка ───────────────────────────────────────────────────────────────

    [Fact]
    public void Summarize_CountsEachVerdict()
    {
        var s = ClubPairLogligRule.Summarize(
        [
            ClubPairLogligVerdicts.ClubA, ClubPairLogligVerdicts.ClubA, ClubPairLogligVerdicts.ClubB,
            ClubPairLogligVerdicts.Both, ClubPairLogligVerdicts.Other, ClubPairLogligVerdicts.Archive,
            ClubPairLogligVerdicts.Archive, ClubPairLogligVerdicts.NoLogligId, ClubPairLogligVerdicts.Unchecked,
        ]);
        Assert.Equal((2, 1, 1, 1, 2, 1, 1), (s.ClubA, s.ClubB, s.Both, s.Other, s.Archive, s.NoLogligId, s.Unchecked));
        Assert.Equal(9, s.Total);
    }

    [Fact]
    public void Summarize_Empty_AllZero() => Assert.Equal(0, ClubPairLogligRule.Summarize([]).Total);

    // ── Свежесть ответа (кнопка «Проверить в loglig») ─────────────────────────

    [Fact]
    public void NeedsFetch_NoAnswer() => Assert.True(ClubPairLogligRule.NeedsFetch(Now, 77, null));

    [Fact]
    public void NeedsFetch_FreshAnswer_No() =>
        Assert.False(ClubPairLogligRule.NeedsFetch(Now, 77, new StoredLogligClub(77, "הפועל ירושלים", Now.AddHours(-23))));

    [Fact]
    public void NeedsFetch_OlderThanDay() =>
        Assert.True(ClubPairLogligRule.NeedsFetch(Now, 77, new StoredLogligClub(77, "הפועל ירושלים", Now.AddHours(-25))));

    [Fact]
    public void NeedsFetch_OtherLogligId() =>
        Assert.True(ClubPairLogligRule.NeedsFetch(Now, 77, new StoredLogligClub(78, "הפועל ירושלים", Now.AddHours(-1))));

    [Fact]
    public void NeedsFetch_CardWithoutClub_Retry() =>
        Assert.True(ClubPairLogligRule.NeedsFetch(Now, 77, new StoredLogligClub(77, null, Now.AddHours(-1))));

    [Fact]
    public void NeedsFetch_NoLogligId_Never() => Assert.False(ClubPairLogligRule.NeedsFetch(Now, null, null));
}

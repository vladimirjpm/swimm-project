using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р125: правило «пол пловца с карточки loglig». Пишем, только если (а) пол пуст, (б) loglig-id
/// подтверждён, (в) имя и год карточки совпали, (г) карточка дала однозначный пол.
/// </summary>
public class LogligCardGenderRuleTests
{
    private static CardGenderSwimmer Swimmer(string? gender = null, int? logligId = 129763,
        string? status = "Verified", string name = "סמואל מונייר", int year = 1998) =>
        new(gender, logligId, status, name.Split(' '), year);

    private static CardGenderAnswer Card(string? gender = "M", string name = "סמואל מונייר", int? year = 1998) =>
        new(name.Split(' ', StringSplitOptions.RemoveEmptyEntries), year, gender);

    [Theory]
    [InlineData("M", "male")]
    [InlineData("F", "female")]
    [InlineData("male", "male")]
    [InlineData("female", "female")]
    public void AllConditionsMet_Writes(string cardGender, string expected)
    {
        var (verdict, gender) = LogligCardGenderRule.Decide(Swimmer(), Card(cardGender));

        Assert.Equal(CardGenderVerdict.Write, verdict);
        Assert.Equal(expected, gender);
    }

    // (а) Проставленный пол не перезаписывается — и когда loglig согласен, и когда нет.
    [Theory]
    [InlineData("female")]
    [InlineData("male")]
    [InlineData("F")]
    public void GenderAlreadySet_NeverOverwritten(string ours)
    {
        var (verdict, gender) = LogligCardGenderRule.Decide(Swimmer(gender: ours), Card("M"));

        Assert.Equal(CardGenderVerdict.AlreadyKnown, verdict);
        Assert.Null(gender);
    }

    // (б) Предложенный, отклонённый или отсутствующий loglig-id — карточке не верим.
    [Theory]
    [InlineData(129763, "Suggested")]
    [InlineData(129763, "Rejected")]
    [InlineData(129763, null)]
    [InlineData(null, "Verified")]
    public void LogligIdNotVerified_Refuses(int? logligId, string? status)
    {
        var (verdict, _) = LogligCardGenderRule.Decide(Swimmer(logligId: logligId, status: status), Card());

        Assert.Equal(CardGenderVerdict.NotVerified, verdict);
    }

    [Fact]
    public void CardUnavailable_IsItsOwnVerdict()
    {
        Assert.Equal(CardGenderVerdict.CardUnavailable, LogligCardGenderRule.Decide(Swimmer(), null).Verdict);
    }

    // (в) Год обязателен: другой год или карточка без года — не тот человек.
    [Theory]
    [InlineData(1999)]
    [InlineData(null)]
    public void BirthYearDiffers_Refuses(int? cardYear)
    {
        var (verdict, gender) = LogligCardGenderRule.Decide(Swimmer(), Card(year: cardYear));

        Assert.Equal(CardGenderVerdict.YearMismatch, verdict);
        Assert.Null(gender);
    }

    [Fact]
    public void NameDiffers_Refuses()
    {
        var (verdict, gender) = LogligCardGenderRule.Decide(Swimmer(), Card(name: "דניאל כהן"));

        Assert.Equal(CardGenderVerdict.NameMismatch, verdict);
        Assert.Null(gender);
    }

    // То же сопоставление, что у привязки по протоколу: порядок токенов не важен, одно имя может
    // быть полнее другого («טימופיי גלושקוב צים» на loglig против «טימופיי גלושקוב» у нас).
    [Theory]
    [InlineData("מונייר סמואל")]
    [InlineData("סמואל דוד מונייר")]
    public void NameReorderedOrFuller_StillSamePerson(string cardName)
    {
        Assert.Equal(CardGenderVerdict.Write, LogligCardGenderRule.Decide(Swimmer(), Card(name: cardName)).Verdict);
    }

    [Fact]
    public void EmptyCardName_Refuses()
    {
        Assert.Equal(CardGenderVerdict.NameMismatch, LogligCardGenderRule.Decide(Swimmer(), Card(name: "")).Verdict);
    }

    // (г) Чипа «מגדר» нет или в нём не «זכר»/«נקבה» — не гадаем.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mixed")]
    [InlineData("אחר")]
    public void CardWithoutClearGender_Refuses(string? cardGender)
    {
        var (verdict, gender) = LogligCardGenderRule.Decide(Swimmer(), Card(cardGender));

        Assert.Equal(CardGenderVerdict.CardHasNoGender, verdict);
        Assert.Null(gender);
    }

    // Суточный ответ годится, только если записан с именем (после Р125), тем же id и свежий.
    [Fact]
    public void CacheValid_NeedsNameSameIdAndFreshness()
    {
        var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
        var fresh = new StoredLogligClub(7, "клуб", now.AddHours(-3), "имя", 2012, "male");

        Assert.True(LogligCardGenderRule.CacheValid(now, fresh, 7));
        Assert.False(LogligCardGenderRule.CacheValid(now, fresh, 8));
        Assert.False(LogligCardGenderRule.CacheValid(now, fresh with { CheckedAt = now.AddHours(-25) }, 7));
        Assert.False(LogligCardGenderRule.CacheValid(now, fresh with { CardName = null }, 7));
        Assert.False(LogligCardGenderRule.CacheValid(now, null, 7));
        // Карточка без пола — тоже ответ: второй раз за сутки не идём.
        Assert.True(LogligCardGenderRule.CacheValid(now, fresh with { Gender = null }, 7));
    }

    [Fact]
    public void Summary_SaysWhoWasSetAndWhoWasNot()
    {
        var text = LogligCardGenderRule.Summary(candidates: 12, set: 9, rows: 10, cardWithoutGender: 1,
            failed: 1, mismatch: 1, notVerified: 0, overLimit: 0, requests: 11, fromCache: 1);

        Assert.Contains("проставлен 9 (строк результата 10)", text);
        Assert.Contains("на карточке пола нет — 1", text);
        Assert.Contains("карточка не открылась — 1", text);
        Assert.Contains("имя или год карточки не совпали — 1", text);
        Assert.DoesNotContain("потолка", text);
        Assert.Contains("Запросов к loglig 11", text);
    }

    // ── Р126: вторые ворота — личность по заплыву и серьёзные последствия ────────────────

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IdentityConfirmedBySwim_AndNoConsequences_NoReview(bool onCard, bool rowPlayer)
    {
        Assert.Empty(LogligCardGenderRule.ReviewReasons(onCard, rowPlayer, GenderImpact.None));
    }

    [Fact]
    public void NameAndYearOnly_IsNotEnough_GoesToReview()
    {
        var reason = Assert.Single(LogligCardGenderRule.ReviewReasons(false, false, GenderImpact.None));
        Assert.Equal(GenderImpactRules.IdentityUnconfirmed, reason.Code);
        Assert.Contains("не смогли подтвердить, что это тот же человек", reason.Text);
    }

    [Fact]
    public void SeriousConsequences_GoToReview_EvenWithConfirmedIdentity()
    {
        var impact = new GenderImpact([new GenderImpactItem(GenderImpactRules.Record, "появится рекорд")]);
        Assert.Equal([GenderImpactRules.Record],
            LogligCardGenderRule.ReviewReasons(true, true, impact).Select(r => r.Code));
    }

    // Ответ, записанный сверкой клуба (про заплывы неизвестно), для решения о поле не годится —
    // если только строка пловца не несёт этого же игрока loglig.
    [Fact]
    public void CacheDecides_OnlyWhenIdentityEvidenceIsKnown()
    {
        var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
        var unknown = new StoredLogligClub(7, "клуб", now.AddHours(-3), "имя", 2012, "male");

        Assert.False(LogligCardGenderRule.CacheDecides(now, unknown, 7, rowPlayerMatched: false));
        Assert.True(LogligCardGenderRule.CacheDecides(now, unknown, 7, rowPlayerMatched: true));
        Assert.True(LogligCardGenderRule.CacheDecides(now, unknown with { ResultsMatched = false }, 7, false));
        Assert.True(LogligCardGenderRule.CacheDecides(now, unknown with { ResultsMatched = true }, 7, false));
        Assert.False(LogligCardGenderRule.CacheDecides(now, unknown with { ResultsMatched = true }, 8, true));
    }

    [Fact]
    public void Summary_ReportsReview_AndHonestPartialRun()
    {
        var text = LogligCardGenderRule.Summary(12, 5, 6, 0, 0, 0, 0, 0, 7, 0, review: 3, dbFailed: 1, reached: 9);

        Assert.Contains("время вышло, дошли до 9 из 12 — нажмите ещё раз", text);
        Assert.Contains("проставлен 5", text);
        Assert.Contains("ждут ручной проверки — 3", text);
        Assert.Contains("сбой записи — 1", text);
    }

    // Влад 25.11.2026 (comp 1896): «ждут ручной проверки — 12» без причины непонятно — пишем, почему.
    [Fact]
    public void Summary_SaysWhyReview_IdentityOnly()
    {
        var text = LogligCardGenderRule.Summary(12, 0, 0, 0, 0, 0, 0, 0, 0, 12, review: 12, reviewIdentity: 12);

        Assert.Contains("ждут ручной проверки — 12, потому что личность не подтверждена: "
                        + "на карточке loglig нет ни одного заплыва пловца", text);
    }

    [Fact]
    public void Summary_SaysWhyReview_ImpactOnly_AndBothReasons()
    {
        var impact = LogligCardGenderRule.Summary(5, 0, 0, 0, 0, 0, 0, 0, 5, 0, review: 2, reviewImpact: 2);
        Assert.Contains("ждут ручной проверки — 2, потому что изменилось бы серьёзное", impact);

        // 4 на проверке: у 3 личность, у 2 последствия — у одного обе причины.
        var both = LogligCardGenderRule.Summary(5, 1, 1, 0, 0, 0, 0, 0, 5, 0, review: 4, reviewIdentity: 3, reviewImpact: 2);
        Assert.Contains("ждут ручной проверки — 4 (личность не подтверждена: на карточке loglig нет ни одного заплыва пловца — 3, "
                        + "изменилось бы серьёзное (рекорд, место, медаль, очки или топ) — 2; у части — обе причины)", both);
    }
}

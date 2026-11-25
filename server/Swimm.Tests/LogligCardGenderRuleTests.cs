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
}

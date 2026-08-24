using Swimm.Application.Validation;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Подбор категорий соревнования по его названию (правило Влада 2026-08-23): «есть в названии
/// слово из /Admin/Categories — применяй; если там не только эти возраста — добавляй 8-99».
///
/// Названия в тестах — живые, из базы. Слова берутся из самой таблицы категорий (Name +
/// NameHe), поэтому здесь они заданы так же, как заведены в БД.
/// </summary>
public class CategoryNameMatcherTests
{
    private static readonly List<CategoryWord> Categories =
    [
        new("results-kids-team", ["Kids", "ילדים"], MinAge: 8, MaxAge: 11),
        new("results-youth-team", ["Young", "צעירים"], MinAge: 11, MaxAge: 14),
        new("results-junior-results", ["Juniors", "נוער"], MinAge: 14, MaxAge: 17),
        new("results-main", ["Adults", "בוגרים"], MinAge: 17),
        new("results-masters", ["Masters", "מסטרס"]),
        new("result-maccabiah", ["Maccabiah", "מכביה"]),
        new("results-8-99", ["Age 8-99", "רב גילאי"]),
    ];

    private static IReadOnlyList<string> Suggest(string name, bool isMasters = false) =>
        CategoryNameMatcher.Suggest(name, Categories, isMasters);

    [Fact]
    public void PicksCategoryByHebrewWord()
    {
        // «אליפות ישראל "ארנה" לגילאים צעירים חורף 2026» — Young.
        var keys = Suggest("אליפות ישראל \"ארנה\" לגילאים צעירים חורף 2026");

        Assert.Contains("results-youth-team", keys);
        Assert.DoesNotContain("results-main", keys);
    }

    [Fact]
    public void PicksSeveralWhenNameNamesSeveral()
    {
        // «נוער ובוגרים» — юниоры И взрослые, обе категории.
        var keys = Suggest("אליפות ישראל \"ארנה\" נוער ובוגרים חורף 2025");

        Assert.Contains("results-junior-results", keys);
        Assert.Contains("results-main", keys);
    }

    [Fact]
    public void PrefixesAndConjunctionsDoNotHideTheWord()
    {
        // В иврите слово идёт с приставкой-предлогом: «לצעירים» (для юниоров), «ובוגרים» (и
        // взрослые). Поиск по подстроке это переживает, а вот «צעיריםX» — уже другое слово.
        Assert.Contains("results-youth-team", Suggest("אליפות ישראל \"ארנה\" לצעירים בבריכה"));
        Assert.Contains("results-main", Suggest("אליפות נוער ובוגרים"));
    }

    [Fact]
    public void AddsAllAges_WhenNameSaysNothingAboutAge()
    {
        // «ליגה מס 1 הפועל ירושלים» — про возраст ни слова: старт ничем не ограничен.
        var keys = Suggest("ליגה מס 1 הפועל ירושלים");

        Assert.Equal(["results-8-99"], keys);
    }

    [Fact]
    public void NumericRangeIsMappedToBand()
    {
        // «לגילאי 8-11» — возраст назван цифрами, слова категории нет. По полосам из
        // /Admin/Categories это Kids (8–11) — ровно так Влад и размечал такие старты вручную
        // (9 соревнований в базе). Раньше правило давало им «все возраста».
        var keys = Suggest("אליפות ישראל \"ארנה\" לגילאי 8-11 חורף 2026");

        Assert.Equal(["results-kids-team"], keys);
    }

    [Theory]
    [InlineData("אליפות חורף ארנה גילאי 11-10", "results-kids-team")]   // границы пишут наоборот
    [InlineData("אליפות ישראל \"ארנה\" קיץ 2025 לגילאי 9-11", "results-kids-team")]
    [InlineData("תחרות גילאי 12-13", "results-youth-team")]
    [InlineData("תחרות גילאי 15-16", "results-junior-results")]
    [InlineData("תחרות גילאי 18-20", "results-main")]
    public void NumericRangesFollowTheLadderFromDb(string name, string expected) =>
        Assert.Equal([expected], Suggest(name));

    [Fact]
    public void AdjacentBandShareOneYear_AndThatIsNotEnough()
    {
        // Полосы смыкаются: Kids 8–11 и Young 11–14 делят одиннадцатилетку. Один общий год
        // не повод отмечать обе — иначе у каждого детского старта висела бы лишняя категория.
        Assert.Equal(["results-kids-team"], Suggest("גילאי 8-11"));
        Assert.Equal(["results-youth-team"], Suggest("גילאי 11-14"));
    }

    [Fact]
    public void WideRangeCoveringWholeLadder_MeansAllAges()
    {
        // «8-99» перекрывает все полосы — это не «Kids и Young и Juniors», а «все возраста».
        Assert.Equal(["results-8-99"], Suggest("תחרות פתוחה גילאי 8-99"));
    }

    [Fact]
    public void OpenTopRangeGoesToAdults()
    {
        // «17+» — открытая сверху полоса Adults.
        Assert.Contains("results-main", Suggest("אליפות 17+"));
    }

    [Fact]
    public void YearInNameIsNotAnAgeRange()
    {
        // «חורף 2026» — год, а не возраст: лишней категории он давать не должен.
        var keys = Suggest("אליפות מכבי בשחייה אביב 2026- צעירים");

        Assert.Equal(["results-youth-team"], keys);
    }

    [Fact]
    public void MastersComesFromFile_WhenNameIsSilent()
    {
        // «ליגת ותיקים» — мастерс, но словом «מסטרס» это не названо; признак есть у заплывов.
        var keys = Suggest("ליגת ותיקים תל אביב", isMasters: true);

        Assert.Contains("results-masters", keys);
    }

    [Fact]
    public void NonAgeCategoryStillGetsAllAges()
    {
        // Маккабиада — кастомная категория, а не возрастная полоса. Возраст в названии не
        // назван ничем, а плывут там все — значит по правилу добавляется и «Age 8-99».
        var keys = Suggest("מכביה 2025");

        Assert.Equal(["result-maccabiah", "results-8-99"], keys);
    }

    [Fact]
    public void AllAgesIsNeverPickedByItsOwnName()
    {
        // «Age 8-99» ставится ПРАВИЛОМ, а не по слову в названии — иначе оно попадало бы
        // в подбор от любого «רב גילאי» и путало причину.
        var keys = Suggest("תחרות רב גילאי");

        Assert.Equal(["results-8-99"], keys);
    }
}

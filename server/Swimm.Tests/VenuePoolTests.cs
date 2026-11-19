using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Application.Validation;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Справочник «место → длина бассейна» (П4): нормализация написаний, ответ справочника,
/// выбор длины в превью и пачке, проверка формы. Без базы — Postgres-часть в VenuePoolPgTests.
/// Написания взяты из настоящих строк «Входящих» прода (20.11.2026).
/// </summary>
public class VenuePoolTests
{
    // ── Нормализация ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("בריכת ויצ\"ו נהלל", "בריכת ויצו נהלל")]                       // кавычки
    [InlineData("בריכת ויצ״ו נהלל", "בריכת ויצו נהלל")]                        // гершаим
    [InlineData("בריכת וייסגל", "בריכת וויסגל")]                               // וו / יי
    [InlineData("מרכז הספורט תלפ\"ז- ארמון הנציב", "מרכז הספורט תלפ\"ז ארמון הנציב")] // дефис
    [InlineData("בריכת מכבי חיפה", "מכבי חיפה")]                               // «בריכת» в начале
    [InlineData("הבריכה הלימודית רמת אביב", "הלימודית רמת אביב")]
    [InlineData("  בריכת   אפק ", "בריכת אפק")]                                // пробелы
    [InlineData("בְּרֵיכַת אֲפֵק", "בריכת אפק")]                                    // огласовки
    [InlineData("Wingate Institute", "wingate   INSTITUTE")]                  // латиница, регистр
    [InlineData("בריכת שיכון מזרחי, ראשון לציון", "בריכת שיכון מזרחי ראשון לציון")] // запятая
    public void Normalize_SameVenue_SameKey(string a, string b) =>
        Assert.Equal(VenueNameNormalizer.Normalize(a), VenueNameNormalizer.Normalize(b));

    [Theory]
    [InlineData("בריכת וייסגל", "בריכת וייסגל רחובות")]          // город — синоним, а не нормализация
    [InlineData("חוות הנוער הציוני", "בריכת חוות הנוער בציוני")]  // опечатку не угадываем
    [InlineData("שער הנגב", "הידרו שער הנגב")]
    [InlineData("קאנטרי בית שמש", "בית שמש")]
    public void Normalize_DifferentSpelling_DifferentKey(string a, string b) =>
        Assert.NotEqual(VenueNameNormalizer.Normalize(a), VenueNameNormalizer.Normalize(b));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" - , ")]
    [InlineData("בריכה")]
    [InlineData("בריכת")]
    public void Normalize_NoName_IsNull(string? venue) => Assert.Null(VenueNameNormalizer.Normalize(venue));

    [Fact]
    public void Normalize_KeepsTheWordPool_InTheMiddle() =>
        Assert.Equal("עירונית שיכון המזרח ראשלצ", VenueNameNormalizer.Normalize("בריכה עירונית שיכון המזרח ראש\"לצ"));

    // ── Ответ справочника ──────────────────────────────────────────────────────

    private static readonly VenuePoolEntry Weizgal =
        new(1, "בריכת וייסגל", "25m", false, null, "Vlad", new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
    private static readonly VenuePoolEntry Wingate =
        new(2, "מכון וינגייט", null, true, "25 и 50", "Vlad", new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));

    private static VenuePoolDirectory Directory() => new(new[]
    {
        KeyValuePair.Create(VenueNameNormalizer.Normalize("בריכת וייסגל")!, Weizgal),
        KeyValuePair.Create(VenueNameNormalizer.Normalize("בריכת וייסגל רחובות")!, Weizgal),
        KeyValuePair.Create(VenueNameNormalizer.Normalize("מכון וינגייט")!, Wingate),
    });

    [Theory]
    [InlineData("בריכת וייסגל")]
    [InlineData("בריכת וויסגל רחובות")]   // другое написание синонима — тот же ключ
    [InlineData(" וייסגל ")]             // без «בריכת»
    public void Resolve_KnownSpelling_Found(string venue)
    {
        var r = Directory().Resolve(venue);
        Assert.Equal(VenuePoolLookupStatus.Found, r.Status);
        Assert.Equal("25m", r.PoolType);
        Assert.Equal(1, r.Entry!.Id);
        Assert.Contains("справочник бассейнов", r.Describe());
        Assert.Contains("Vlad", r.Describe());
        Assert.Contains("20.11.2026", r.Describe());
    }

    [Fact]
    public void Resolve_Ambiguous_GivesNoLength()
    {
        var r = Directory().Resolve("מכון וינגייט");
        Assert.Equal(VenuePoolLookupStatus.Ambiguous, r.Status);
        Assert.Null(r.PoolType);
        Assert.False(r.IsFound);
        Assert.Contains("двусмысленное", r.Describe());
    }

    [Fact]
    public void Resolve_UnknownSpelling_NotInDirectory()
    {
        var r = Directory().Resolve("בריכת מרום נווה רמת גן");
        Assert.Equal(VenuePoolLookupStatus.NotInDirectory, r.Status);
        Assert.Null(r.PoolType);
        Assert.Contains("בריכת מרום נווה רמת גן", r.Describe());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("בריכה")]
    public void Resolve_NoVenue(string? venue) =>
        Assert.Equal(VenuePoolLookupStatus.NoVenue, Directory().Resolve(venue).Status);

    [Fact]
    public void Resolve_BrokenLengthOnUnambiguousEntry_IsNotGuessed()
    {
        var broken = Weizgal with { PoolType = "33m" };
        var dir = new VenuePoolDirectory([KeyValuePair.Create("x", broken)]);
        Assert.Equal(VenuePoolLookupStatus.Ambiguous, dir.Resolve("x").Status);
    }

    // ── Выбор длины: регламент → справочник → как было ─────────────────────────

    [Fact]
    public void Choose_RegulationBeatsDirectory()
    {
        var c = PoolTypeChooser.Choose("50m", "регламент: «50 מ׳»", Directory().Resolve("בריכת וייסגל"));
        Assert.Equal("50m", c.PoolType);
        Assert.Equal(PoolTypeSources.Regulation, c.Source);
        Assert.Equal("регламент: «50 מ׳»", c.Reason);
    }

    [Fact]
    public void Choose_DirectoryWhenNoRegulation()
    {
        var c = PoolTypeChooser.Choose(null, null, Directory().Resolve("בריכת וייסגל"));
        Assert.Equal("25m", c.PoolType);
        Assert.Equal(PoolTypeSources.Venue, c.Source);
        Assert.Contains("справочник бассейнов", c.Reason);
    }

    [Theory]
    [InlineData("מכון וינגייט", "двусмысленное")]
    [InlineData("בריכת מרום נווה רמת גן", "нет в справочнике")]
    [InlineData(null, "не указано")]
    public void Choose_NoAnswer_AsksHuman_AndSaysWhy(string? venue, string why)
    {
        var c = PoolTypeChooser.Choose(null, null, Directory().Resolve(venue));
        Assert.Null(c.PoolType);
        Assert.Null(c.Source);
        Assert.Contains(why, c.Reason);
        Assert.Contains("выбери вручную", c.Reason);
    }

    [Fact]
    public void Bulk_League_UnknownVenue_KeepsLeagueDefault()
    {
        var c = PoolTypeChooser.ChooseForBulk(null, null, Directory().Resolve("בריכת מרום נווה רמת גן"), isChampionship: false);
        Assert.Equal("25m", c.PoolType);
        Assert.Equal(PoolTypeSources.Default, c.Source);
    }

    [Fact]
    public void Bulk_Ambiguous_NoDefault()
    {
        // Вингейт: 25 по умолчанию молча положил бы 50-метровый старт в короткую воду (Р-3).
        var c = PoolTypeChooser.ChooseForBulk(null, null, Directory().Resolve("מכון וינגייט"), isChampionship: false);
        Assert.Null(c.PoolType);
    }

    [Fact]
    public void Bulk_Championship_DirectoryAnswers_NoDefaultOtherwise()
    {
        Assert.Equal("25m", PoolTypeChooser.ChooseForBulk(null, null, Directory().Resolve("בריכת וייסגל"), true).PoolType);
        Assert.Null(PoolTypeChooser.ChooseForBulk(null, null, Directory().Resolve(null), true).PoolType);
    }

    // ── Пакетный классификатор ─────────────────────────────────────────────────

    private static DiscoveryPreviewResult PreviewWith(VenuePoolLookup venue) =>
        new(Guid.NewGuid(),
            new ParsedCompetition
            {
                Format = "IsrOrg", ResultsJson = "[]", ResultCount = 10,
                Competitions = [new ParsedCompetitionSummary("День 1", "01/11/2025", 10)],
                Warnings = [],
            },
            ["he"], null, [], new ImportRecordPreviewDto { Count = 0 }, null,
            new CompetitionFlagSuggestion(false, false, false, false, venue.PoolType, null,
                new Dictionary<string, string>(), [], venue.IsFound ? PoolTypeSources.Venue : null, venue));

    private static RegulationFetchDto Regulation(bool championship = false, string? pool = null) =>
        new(true, "https://loglig.com:2053/LeagueTable/ShowLeagueDoc/1",
            new RegulationAnalysisDto(true, false, championship, [], PoolType: pool));

    [Fact]
    public void Classifier_AmbiguousVenue_NeedsReview()
    {
        var (verdict, reasons) = BulkPullClassifier.Classify(
            PreviewWith(Directory().Resolve("מכון וינגייט")), Regulation(), isChampionshipByName: false);
        Assert.Equal(BulkPullVerdict.NeedsReview, verdict);
        Assert.Contains(reasons, r => r.Contains("двусмысленное"));
    }

    [Fact]
    public void Classifier_AmbiguousVenue_RegulationAnswers_Clean()
    {
        var (verdict, _) = BulkPullClassifier.Classify(
            PreviewWith(Directory().Resolve("מכון וינגייט")), Regulation(pool: "50m"), isChampionshipByName: false);
        Assert.Equal(BulkPullVerdict.Clean, verdict);
    }

    [Fact]
    public void Classifier_Championship_DirectoryAnswers_NoPoolQuestion()
    {
        var (_, reasons) = BulkPullClassifier.Classify(
            PreviewWith(Directory().Resolve("בריכת וייסגל")), Regulation(championship: true), isChampionshipByName: true);
        Assert.DoesNotContain(reasons, r => r.Contains("25/50"));
    }

    // ── Форма ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NameBecomesFirstAlias_DuplicatesByKeyDropped()
    {
        var (v, error) = VenuePoolInputValidator.Validate(new VenuePoolInputDto(
            " בריכת וייסגל ", "25m", false, null, "בריכת וויסגל\r\nבריכת וייסגל רחובות\n\n  "));
        Assert.Null(error);
        Assert.Equal("בריכת וייסגל", v!.Name);
        Assert.Equal(["בריכת וייסגל", "בריכת וייסגל רחובות"], v.Aliases.Select(a => a.Alias).ToList());
    }

    [Fact]
    public void Validate_Ambiguous_DropsLength()
    {
        var (v, error) = VenuePoolInputValidator.Validate(new VenuePoolInputDto("מכון וינגייט", "50m", true, null, null));
        Assert.Null(error);
        Assert.Null(v!.PoolType);
        Assert.True(v.IsAmbiguous);
    }

    [Theory]
    [InlineData("", "25m", false)]
    [InlineData("בריכה", "25m", false)]
    [InlineData("בריכת אפק", null, false)]
    [InlineData("בריכת אפק", "33m", false)]
    public void Validate_Rejects(string name, string? pool, bool ambiguous)
    {
        var (v, error) = VenuePoolInputValidator.Validate(new VenuePoolInputDto(name, pool, ambiguous, null, null));
        Assert.Null(v);
        Assert.NotNull(error);
    }
}

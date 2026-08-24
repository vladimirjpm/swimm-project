using Swimm.Parsing.Parsers.Regulation;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разбор регламента соревнования (תקנון) ради галочек в панели затягивания.
///
/// Строки в тестах — НАСТОЯЩИЕ, из регламентов «אליפות מכבי בשחייה אביב 26» и
/// «אליפות ישראל וטריילז ארנה 2026», и лежат они здесь ЗАДОМ НАПЕРЁД: именно так текст
/// выходит из ивритских PDF федерации. Анализатор обязан находить слова в обоих
/// направлениях, иначе на живых файлах не сработает ничего.
/// </summary>
public class RegulationAnalyzerTests
{
    private static string Reversed(string readable)
    {
        var chars = readable.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    [Fact]
    public void Medals_FoundInReversedText()
    {
        var text = Reversed("● הענקת מדליות ע\"פ גיל.");

        var finding = Assert.Single(RegulationAnalyzer.Find(text));

        Assert.Equal(RegulationFlags.Medals, finding.Flag);
        // Цитату разворачиваем обратно — админ должен прочитать её глазами.
        Assert.Contains("מדליות", finding.Quote);
    }

    [Fact]
    public void Medals_FoundEvenWhenPdfSplitsTheWord()
    {
        // «מדליו ת» — не опечатка регламента, а разрыв слова при извлечении PDF.
        var text = Reversed("פרסים: מדליו ת למקומות 3-1 למנצחים/ות בכל משחה וקבוצת גיל.");

        var finding = Assert.Single(RegulationAnalyzer.Find(text));
        Assert.Equal(RegulationFlags.Medals, finding.Flag);
    }

    [Theory]
    [InlineData("ניקוד קבוצתי (למשחי השליחים ניקוד כפול):")]
    [InlineData("01. טבלת הניקוד הקבוצתי (שליחים ניקוד כפול)")]
    public void ClubStanding_FoundWithAndWithoutArticle(string readable)
    {
        // В регламентах клубный зачёт называется КОМАНДНЫМ (ניקוד קבוצתי), а не «דירוג
        // מועדונים», как на loglig, и слова идут с артиклями — потому маркеры регулярками.
        var finding = Assert.Single(RegulationAnalyzer.Find(Reversed(readable)));

        Assert.Equal(RegulationFlags.ClubStanding, finding.Flag);
    }

    [Fact]
    public void Championship_FoundByName()
    {
        var text = Reversed("תקנון אליפות ישראל וטריילז ארנה 2026");

        Assert.Contains(RegulationAnalyzer.Find(text),
            f => f.Flag == RegulationFlags.Championship);
    }

    /// <summary>
    /// «Миллениум 2025» (compID 16739): единственное упоминание чемпионата во всём регламенте —
    /// во вступительном слове, и оно говорит РОВНО ОБРАТНОЕ: соревнование даёт «подготовиться
    /// к чемпионату Израиля». Галочка «Чемпионат Израиля» уходила в БД и меняла вид клубного
    /// зачёта; теперь такие упоминания отклоняются по словам подготовки рядом.
    /// </summary>
    [Fact]
    public void Championship_PreparationForIt_IsNotAChampionship()
    {
        var text = Reversed(
            "תחרות המילניום, אשר תתקיים בבריכה מהירה ומקצועית בת 8 מסלולים, תהווה "
            + "להתחדד ולהתכונן באופן מיטבי לאליפות ישראל.");

        Assert.DoesNotContain(RegulationAnalyzer.Find(text),
            f => f.Flag == RegulationFlags.Championship);
    }

    [Theory]
    [InlineData("תחרות הכנה לאליפות ישראל לגילאים צעירים")]
    [InlineData("התחרות מתקיימת לקראת אליפות ישראל החורף")]
    public void Championship_OtherPreparationWordings_AlsoRejected(string readable)
        => Assert.DoesNotContain(RegulationAnalyzer.Find(Reversed(readable)),
            f => f.Flag == RegulationFlags.Championship);

    /// <summary>
    /// Вторая живая формула — ССЫЛКА на чемпионат как на образец. «ליגה מס 1 צעירים»
    /// (compID 16752) пишет: возрастные группы в лиге такие же, как на чемпионате Израиля.
    /// Лига от этого чемпионатом не становится.
    /// </summary>
    [Theory]
    [InlineData("קבוצות הגיל בליגה זהות לקבוצות הגיל באליפות ישראל לצעירים")]
    [InlineData("המשחים יתקיימו בהתאם לתקנון אליפות ישראל")]
    public void Championship_ReferenceAsATemplate_IsRejected(string readable)
        => Assert.DoesNotContain(RegulationAnalyzer.Find(Reversed(readable)),
            f => f.Flag == RegulationFlags.Championship);

    [Fact]
    public void Championship_PreparationMentionDoesNotHideARealOne()
    {
        // Вето отклоняет одно вхождение, но не должно прятать настоящее дальше по тексту:
        // PdfPig отдаёт страницу ОДНОЙ строкой, так что оба упоминания придут вместе.
        var readable =
            "התחרות תהווה הזדמנות להתכונן לאליפות ישראל. " + new string('־', 200)
            + " תקנון אליפות ישראל וטריילז ארנה 2026";

        Assert.Contains(RegulationAnalyzer.Find(Reversed(readable)),
            f => f.Flag == RegulationFlags.Championship);
    }

    /// <summary>Предлог ל־ сам по себе не улика: «регламент ДЛЯ чемпионата» — это чемпионат.</summary>
    [Fact]
    public void Championship_DativeWithoutPreparationWords_StaysAChampionship()
    {
        var text = Reversed("התקנון לאליפות ישראל וטריילז ארנה 2026");

        Assert.Contains(RegulationAnalyzer.Find(text),
            f => f.Flag == RegulationFlags.Championship);
    }

    [Fact]
    public void PlainRegulation_WithoutMarkers_FindsNothing()
    {
        // Регламент оплаты — ни медалей, ни зачёта: галочки трогать нельзя.
        var text = Reversed("21. עלות ההרשמה ע\"פ תקנון התשלומים של איגוד השחייה.");

        Assert.Empty(RegulationAnalyzer.Find(text));
    }

    /// <summary>
    /// Живая проверка на настоящих файлах регламентов (в Downloads у Влада). В обычном
    /// прогоне пропускается; включается SWIMM_LOCAL_PDF=1 и путями через переменные.
    /// </summary>
    [Fact]
    public void Live_RealRegulationPdfs()
    {
        if (Environment.GetEnvironmentVariable("SWIMM_LOCAL_PDF") != "1") return;

        foreach (var path in (Environment.GetEnvironmentVariable("SWIMM_REG_PDFS") ?? "")
                     .Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            using var stream = File.OpenRead(path);
            var result = new RegulationAnalyzer().Analyze(stream, Path.GetFileName(path));

            // Пишем в файл, а не в консоль: PowerShell рвёт иврит, а цитаты надо прочитать.
            var outPath = Environment.GetEnvironmentVariable("SWIMM_REG_OUT");
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                var lines = new List<string>
                {
                    $"{Path.GetFileName(path)}: medals={result.HasMedals} " +
                    $"club={result.HasClubStanding} champ={result.IsChampionship} error={result.Error}"
                };
                lines.AddRange(result.Findings.Select(f => $"   {f.Flag} | {f.Matched} | {f.Quote}"));
                File.AppendAllLines(outPath, lines, System.Text.Encoding.UTF8);
            }

            Assert.Null(result.Error);
        }
    }

    [Fact]
    public void QuotesPerFlag_AreCapped()
    {
        // Медали в регламенте упоминаются десятками строк — админу хватает пары цитат.
        var lines = Enumerable.Repeat(0, 10)
            .Select((_, i) => Reversed($"● הענקת מדליות לפי קבוצת גיל {i}."));

        var findings = RegulationAnalyzer.Find(string.Join('\n', lines));

        Assert.Equal(2, findings.Count(f => f.Flag == RegulationFlags.Medals));
    }
}

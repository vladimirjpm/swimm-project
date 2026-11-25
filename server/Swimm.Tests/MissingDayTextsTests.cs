using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Простые тексты блока «чего не хватает» (просьба Влада 25.11.2026): подстановка дат, дня недели в
/// нужном падеже, числа дней и строк. Живой случай — 6611: пятница 06.12.2024 в базе, суббота 07.12 найдена.
/// </summary>
public class MissingDayTextsTests
{
    private static readonly DateOnly Fri = new(2024, 12, 6), Sat = new(2024, 12, 7), Sun = new(2024, 12, 8);

    [Fact]
    public void Found_TwoDays_ByWeekday()
    {
        Assert.Equal("Второй день есть у федерации, но у нас его нет", MissingDayTexts.FoundHeadline([Fri], Sat));
        Assert.Equal("Федерация выложила пятницу и субботу как два отдельных соревнования. У нас в базе только пятница 06.12. "
            + "Суббота 07.12 нашлась отдельно.", MissingDayTexts.FoundText([Fri], Sat));
        Assert.Equal("Затянуть субботу 07.12", MissingDayTexts.PullButton([Fri], Sat));
        Assert.Equal("Мы скачаем результаты субботы и добавим их вторым днём. Получится одно соревнование с двумя днями: "
            + "06.12 и 07.12. Перед записью вы увидите, сколько строк и какой бассейн.", MissingDayTexts.PullNote([Fri], Sat));
        Assert.Equal("Не нажимайте, пока не затянули субботу: отметка исчезнет, а результатов субботы на сайте так и не будет.",
            MissingDayTexts.AcceptWarning([Fri], Sat));
    }

    [Fact]
    public void Found_MissingFirstDay_FirstAndMasculineWeekday()
    {
        // 23.11.2026 — понедельник, 24.11 — вторник: не хватает понедельника.
        var mon = new DateOnly(2026, 11, 23);
        var tue = new DateOnly(2026, 11, 24);
        Assert.Equal("Первый день есть у федерации, но у нас его нет", MissingDayTexts.FoundHeadline([tue], mon));
        Assert.EndsWith("Понедельник 23.11 нашёлся отдельно.", MissingDayTexts.FoundText([tue], mon));
        Assert.Contains("добавим их первым днём", MissingDayTexts.PullNote([tue], mon));
        Assert.Equal("Затянуть понедельник 23.11", MissingDayTexts.PullButton([tue], mon));
    }

    [Fact]
    public void Found_ThreeDays_ByDate()
    {
        Assert.Equal("Третий день есть у федерации, но у нас его нет", MissingDayTexts.FoundHeadline([Fri, Sat], Sun));
        Assert.Equal("Затянуть 08.12", MissingDayTexts.PullButton([Fri, Sat], Sun));
        Assert.Contains("одно соревнование с тремя днями: 06.12, 07.12 и 08.12", MissingDayTexts.PullNote([Fri, Sat], Sun));
        Assert.Contains("результаты за 08.12", MissingDayTexts.PullNote([Fri, Sat], Sun));
        Assert.Contains("У нас в базе только 06.12 и 07.12. День 08.12 нашёлся отдельно.", MissingDayTexts.FoundText([Fri, Sat], Sun));
    }

    [Fact]
    public void Sunday_IsNeuter()
    {
        Assert.EndsWith("Воскресенье 08.12 нашлось отдельно.", MissingDayTexts.FoundText([Sat], Sun));
        Assert.Equal("Затянуть воскресенье 08.12", MissingDayTexts.PullButton([Sat], Sun));
    }

    [Fact]
    public void ImportSummary_TemplateAndRows()
    {
        Assert.Equal("Будет добавлен второй день: суббота 07.12.2024, 1784 строки, бассейн {pool}. "
            + "Соревнование станет двухдневным и будет называться «{name}» (название можно поправить).",
            MissingDayTexts.ImportSummaryTemplate([Fri], Sat, 1784, nameEditable: true));
        Assert.Contains("1 строка", MissingDayTexts.ImportSummaryTemplate([Fri], Sat, 1, true));
        Assert.Contains("трёхдневным и будет называться «{name}».", MissingDayTexts.ImportSummaryTemplate([Fri, Sat], Sun, 5, false));
    }

    [Fact]
    public void Done_ListsDaysWithRows() =>
        Assert.Equal("Готово. Теперь у соревнования два дня: 06.12 (1555 строк) и 07.12 (1784 строки).",
            MissingDayTexts.Done([(Sat, 1784), (Fri, 1555)]));

    [Fact]
    public void Collapsed_And_NothingToPull()
    {
        Assert.Equal("Оба дня у нас есть, но записаны одной датой.", MissingDayTexts.CollapsedHeadline(2));
        Assert.Equal("Нажмите «Разделить по дням» — заплывы 28.03 и 29.03 разойдутся по своим дням, ничего не скачивается заново.",
            MissingDayTexts.CollapsedHint([new DateOnly(2025, 3, 29), new DateOnly(2025, 3, 28)]));
        Assert.StartsWith("У федерации в программе плавания только те дни, что у нас уже есть.", MissingDayTexts.NothingToPull);
    }

    [Fact]
    public void Overlap_RefusalNamesTheDay() =>
        Assert.StartsWith("эти заплывы уже есть в пятнице 06.12 — похоже, это тот же протокол, а не другой день",
            SiblingDayRules.OverlapRefusal(
                Enumerable.Range(1, 3).Select(i => new SiblingSwimKey("p" + i, 2010, "freestyle", "50", 1, i)).ToList(), 3, Fri));

    [Fact]
    public void Pool_Words() =>
        Assert.Equal(("25 м", "не выбран"), (MissingDayTexts.Pool("25m"), MissingDayTexts.Pool(null)));
}

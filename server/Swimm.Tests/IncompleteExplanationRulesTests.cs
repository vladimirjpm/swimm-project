using System;
using System.Linq;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Неполный — чего не хватает» в карточке /Admin/Competitions (просьба Влада 24.11.2026):
/// Р115 — даты по сайту и какие дни в базе; Р114 — дисциплины без времён из текста отметки.
/// </summary>
public class IncompleteExplanationRulesTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void R115_OneDayOfTwo()
    {
        var e = IncompleteExplanationRules.MissingDays(D(2023, 12, 22), D(2023, 12, 23), [new PulledDay(D(2023, 12, 22), 1337)])!;
        Assert.Equal("По сайту федерации старт идёт 22–23.12.2023 (2 дня). В базе есть: 22.12 (1337 строк). Нет: 23.12.", e.Headline);
        Assert.Equal(2, e.Hints.Count);
        Assert.Contains("Перезатянуть", e.Hints[1]);
        Assert.Contains("Принять как есть", e.Hints[1]);
    }

    [Fact]
    public void R115_TwoDaysOfThree()
    {
        var e = IncompleteExplanationRules.MissingDays(D(2026, 11, 5), D(2026, 11, 7),
            [new PulledDay(D(2026, 11, 5), 895), new PulledDay(D(2026, 11, 6), 850)])!;
        Assert.Equal("По сайту федерации старт идёт 05–07.11.2026 (3 дня). В базе есть: 05.11 (895 строк), 06.11 (850 строк). Нет: 07.11.", e.Headline);
    }

    [Fact]
    public void R115_AcrossMonth_AndAcrossYear()
    {
        var month = IncompleteExplanationRules.MissingDays(D(2025, 11, 30), D(2025, 12, 1), [new PulledDay(D(2025, 11, 30), 21)])!;
        Assert.Equal("По сайту федерации старт идёт 30.11–01.12.2025 (2 дня). В базе есть: 30.11 (21 строка). Нет: 01.12.", month.Headline);

        var year = IncompleteExplanationRules.MissingDays(D(2024, 12, 31), D(2025, 1, 2), [new PulledDay(D(2025, 1, 1), 3)])!;
        Assert.Equal("По сайту федерации старт идёт 31.12.2024–02.01.2025 (3 дня). В базе есть: 01.01.2025 (3 строки). "
            + "Нет: 31.12.2024, 02.01.2025.", year.Headline);
    }

    [Fact]
    public void R115_NothingMissing_Null() =>
        Assert.Null(IncompleteExplanationRules.MissingDays(D(2026, 1, 1), D(2026, 1, 1), [new PulledDay(D(2026, 1, 1), 5)]));

    [Fact]
    public void R114_OneDistance()
    {
        var e = IncompleteExplanationRules.PartialProtocol("ждут 1 из 16 событий; 4X50m Freestyle Relay - GIRLS 8-15: ещё не плыли (0 из 9 со временем)");
        Assert.Equal("В протоколе 1 из 16 дисциплин без результатов (в последнем заплыве нет времён):", e.Headline);
        Assert.Equal(["4X50m Freestyle Relay - GIRLS 8-15 — ещё не плыли (0 из 9 со временем)"], e.Items);
        Assert.Single(e.Hints);
    }

    [Fact]
    public void R114_ManyDistances_WithMoreTail()
    {
        var e = IncompleteExplanationRules.PartialProtocol(
            "ждут 5 из 21 событий; 100m Free: ещё не плыли (0 из 8 со временем); 200m Back: идёт — в последнем заплыве времён нет (6 из 10 со временем); "
            + "4X50m Medley Relay - BOYS: ещё не плыли (0 из 4 со временем); и ещё 2");
        Assert.Equal("В протоколе 5 из 21 дисциплины без результатов (в последнем заплыве нет времён):", e.Headline);
        Assert.Equal(4, e.Items.Count);
        Assert.Equal("200m Back — идёт — в последнем заплыве времён нет (6 из 10 со временем)", e.Items[1]);
        Assert.Equal("и ещё 2", e.Items[^1]);
    }

    [Fact]
    public void R114_UnknownText_ShownAsIs() =>
        Assert.Equal("протокол обрезан вручную", IncompleteExplanationRules.PartialProtocol("протокол обрезан вручную").Headline);

    [Fact]
    public void For_Row_R115AndPerDayR114_WithDayLabels()
    {
        var u = new UnifiedCompetitionRowDto
        {
            Stage = CompetitionStage.Imported,
            Site = new UnifiedSiteInfo { DateStart = new DateTime(2026, 11, 5), DateEnd = new DateTime(2026, 11, 7) },
            Db = new CompetitionRowDto
            {
                EventId = 62, DayCount = 2,
                Days =
                [
                    new CompetitionListItemDto { Id = 1, Name = "d1", Date = "05/11/2026", DayNumber = 1, ResultCount = 895 },
                    new CompetitionListItemDto { Id = 2, Name = "d2", Date = "06/11/2026", DayNumber = 2, ResultCount = 850,
                        PartialResults = "ждут 1 из 10 событий; 50m Fly: ещё не плыли (0 из 6 со временем)" },
                ],
            },
            Status = new CompetitionRowStatus(CompetitionRowState.WaitingProtocol, WaitingProtocolReason.Incomplete) with { PulledDays = 2, SiteDays = 3 },
        };
        var list = IncompleteExplanationRules.For(u);
        Assert.Equal(2, list.Count);
        Assert.EndsWith("Нет: 07.11.", list[0].Headline);
        Assert.StartsWith("День 2 · 06.11: В протоколе 1 из 10", list[1].Headline);
        Assert.Equal("нет 07.11", IncompleteExplanationRules.MissingDatesShort(u));
    }

    [Theory]
    [InlineData(2026, 3, 5, 2026, 3, 5, "05.03.2026")]
    [InlineData(2026, 3, 5, 2026, 3, 7, "05–07.03.2026")]
    [InlineData(2026, 3, 30, 2026, 4, 1, "30.03–01.04.2026")]
    [InlineData(2025, 12, 31, 2026, 1, 1, "31.12.2025–01.01.2026")]
    public void Range_Formats(int y1, int m1, int d1, int y2, int m2, int d2, string expected) =>
        Assert.Equal(expected, IncompleteExplanationRules.Range(D(y1, m1, d1), D(y2, m2, d2)));
}

using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регрессия по реальным PDF рекордов Израиля (длинный и короткий бассейн).
///
/// Метка блока ("100 מ׳ גב") — объединённая ячейка, центрированная по вертикали.
/// PdfPig иногда кладёт её токены в Y-группу СТРОКИ ДАННЫХ: в длинном бассейне
/// "100 מ׳" приклеилось к строке 01:16.07, а "גב" осталось отдельной строкой.
/// Детект дистанции работал только для строк БЕЗ времени — маркер 100 терялся,
/// и весь блок «100 м на спине» разбирали соседние блоки: мужской уходил в
/// «50 спина», женский в «200 спина» (ложный RECORD BREAKER на детских
/// полтинниках при сравнении с ISR 53.60). Так же слипались 800/1500 вольным.
///
/// Инвариант: у каждой индивидуальной дистанции ровно по одному рекорду
/// на (пол × возрастную категорию), и блок 100 спина существует.
/// </summary>
public class IsrOrgAgeRecordsBlockLabelTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", name);

    [Theory]
    [InlineData("isr-age-records-50m.pdf", "50m")]
    [InlineData("isr-age-records-25m.pdf", "25m")]
    public void BlockLabelGluedToDataRow_StillStartsNewDistanceBlock(string fixture, string poolType)
    {
        var path = Fixture(fixture);
        Assert.True(File.Exists(path), $"Фикстура не найдена: {path}");

        using var fs = File.OpenRead(path);
        var parser = new IsrOrgAgeRecordsParser();
        var results = parser
            .Parse(new ParseRequest(fs, Path.GetFileName(path), PoolType: poolType))
            .ToList();

        var individual = results.Where(r => r.IsRelay != true).ToList();

        // Блок «100 на спине» должен существовать и быть полным (10 возрастов × 2 пола).
        var back100 = individual
            .Where(r => r.EventStyleName == "backstroke" && r.EventStyleLen == "100")
            .ToList();
        Assert.Equal(20, back100.Count);
        Assert.Equal(10, back100.Count(r => r.EventStyleGender == "male"));
        Assert.Equal(10, back100.Count(r => r.EventStyleGender == "female"));

        // Соседние блоки не должны быть раздуты чужими строками.
        Assert.Equal(20, individual.Count(r => r.EventStyleName == "backstroke" && r.EventStyleLen == "50"));
        Assert.Equal(20, individual.Count(r => r.EventStyleName == "backstroke" && r.EventStyleLen == "200"));

        // И в целом: одна дистанция+стиль+пол+возраст = максимум одна запись.
        var dupes = individual
            .GroupBy(r => (r.EventStyleLen, r.EventStyleName, r.EventStyleGender, r.EventStyleAge))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();
        Assert.True(dupes.Count == 0, "Дубли блоков: " + string.Join(", ", dupes));
    }
}

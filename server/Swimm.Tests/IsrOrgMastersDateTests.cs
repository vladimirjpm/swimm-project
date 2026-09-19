using Swimm.Parsing.Parsers.IsrOrgMastersRecords;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разбор дат источника isrorg-masters (docs/data-integrity.md, И-27): файл федерации пишет
/// даты МЕСЯЦ/ДЕНЬ/ГОД без ведущих нулей, а не ДЕНЬ/МЕСЯЦ, как остальные источники. Правило:
/// первая часть &gt; 12 → это точно день (Д/М, свежие ручные строки), иначе — месяц (М/Д,
/// формат файла). Невалидная дата возвращается как есть.
/// </summary>
public class IsrOrgMastersDateTests
{
    [Theory]
    [InlineData("7/20/2019", '/', "20/07/2019")]   // однозначно М/Д (вторая часть > 12)
    [InlineData("3/12/2022", '/', "12/03/2022")]   // неоднозначно → М/Д
    [InlineData("7/9/2010", '/', "09/07/2010")]    // неоднозначно → М/Д
    [InlineData("18/7/2023", '/', "18/07/2023")]   // первая часть > 12 → Д/М
    [InlineData("13/12/2025", '/', "13/12/2025")]  // первая часть > 12 → Д/М
    [InlineData("12/12/2020", '/', "12/12/2020")]  // обе равны — формат не важен
    // Точка — дата обновления в шапке файла, она ДЕНЬ.МЕСЯЦ: файлы source-2025-04 и
    // source-2026-01 обновлены 6 апреля 2025 и 10 января 2026. По правилу косой черты
    // вышло бы 4 июня и 1 октября 2026 — дата в будущем.
    [InlineData("6.4.2025", '.', "06/04/2025")]
    [InlineData("10.1.2026", '.', "10/01/2026")]
    [InlineData("2/30/2020", '/', "2/30/2020")]    // невалидная дата → как есть
    public void ParseSourceDate_AppliesMonthFirstRule(string input, char separator, string expected)
    {
        var result = IsrOrgMastersRecordsParser.ParseSourceDate(input, separator);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Инвариант решения 4 (цена принята осознанно): свежая строка Д/М, у которой обе части
    /// ≤ 12, неотличима от М/Д и прочитается как М/Д — даже если в источнике имелось в виду
    /// 1 мая, а не 5 января. Таких строк в справочнике единицы (5 из ~917), альтернатива
    /// (читать всё как Д/М) портит сотни строк формата файла.
    /// </summary>
    [Fact]
    public void ParseSourceDate_AmbiguousBothLowNumbers_PrefersMonthFirst()
    {
        var result = IsrOrgMastersRecordsParser.ParseSourceDate("1/5/2025", '/');

        Assert.Equal("05/01/2025", result);
    }

    /// <summary>
    /// Дата выпуска файла решает неоднозначную строку (И-27, 19.09.2026): выпуск 10.1.2026
    /// содержит `9/1/2026` и `10/1/2026` — старт 9–10 января, записанный Д/М. М/Д дал бы
    /// 1 сентября и 1 октября, позже выпуска, — значит Д/М. В двух выпусках таких 50 строк.
    /// </summary>
    [Theory]
    [InlineData("9/1/2026", "2026-01-10", "09/01/2026")]
    [InlineData("10/1/2026", "2026-01-10", "10/01/2026")]
    [InlineData("4/5/2025", "2025-04-06", "05/04/2025")]  // выпуск 6.4.2025
    [InlineData("3/12/2022", "2026-01-10", "12/03/2022")] // обе даты до выпуска → М/Д, как раньше
    [InlineData("7/20/2019", "2026-01-10", "20/07/2019")] // однозначная М/Д не трогается
    public void ParseSourceDate_DateAfterFileRelease_FlipsToDayFirst(string input, string released, string expected)
    {
        var result = IsrOrgMastersRecordsParser.ParseSourceDate(input, '/', DateTime.Parse(released));

        Assert.Equal(expected, result);
    }
}

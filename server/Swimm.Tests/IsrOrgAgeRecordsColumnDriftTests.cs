using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разбор PDF возрастных рекордов не должен ЗАВИСЕТЬ ОТ ВЁРСТКИ выпуска (И-26).
///
/// Болезнь нашли так: федерация перевыпустила справочник 17.09.2026, и дифф показал 83
/// изменившиеся строки при том же времени — якобы федерация переписала даты и составы. На
/// деле в самом PDF всё стояло на месте: разбор терял разное в разных выпусках.
///
/// Три причины, три инварианта здесь:
/// <list type="number">
/// <item>строки собирались бинированием по Y, и дата, напечатанная на пункт выше базовой
/// линии, уезжала в собственную «строку» — в файле 17.08 так потерялось 37 дат, в 17.09 — 28,
/// и у РАЗНЫХ строк;</item>
/// <item>слова одной ячейки склеивались через пробел, хотя PdfPig режет слово на куски:
/// «אמילי גולוס» печаталось как «אמיל י גולו ס»;</item>
/// <item>колонка выбиралась по «кто ближе по X», и у длинного состава эстафеты крайние слова
/// утекали к соседям: начало состава — в дату, хвост — в клуб.</item>
/// </list>
///
/// ⚠ Тест намеренно проверяет ФАЙЛ, а не конкретные имена: список рекордов живой, и
/// прибивать его гвоздями значит переписывать тест после каждого выпуска.
/// </summary>
public class IsrOrgAgeRecordsColumnDriftTests
{
    private static readonly Regex LoneLetter = new(@"(?:^|\s)\p{L}(?:\s|$)");

    private static Swimm.Parsing.Models.Result[] Parse(string fixture, string poolType)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", fixture);
        Assert.True(File.Exists(path), $"Фикстура не найдена: {path}");

        using var fs = File.OpenRead(path);
        return new IsrOrgAgeRecordsParser()
            .Parse(new ParseRequest(fs, Path.GetFileName(path), PoolType: poolType))
            .ToArray();
    }

    /// <summary>
    /// Дата есть почти у каждой строки. Порог не нулевой сознательно: у пары строк
    /// справочника даты нет и в самом файле (проверено по тексту PDF) — это не наш дефект.
    /// До правки без даты оставалось 37 строк из 496.
    /// </summary>
    [Theory]
    [InlineData("isr-age-records-25m.pdf", "25m")]
    [InlineData("isr-age-records-50m.pdf", "50m")]
    public void RecordDate_IsNotLostToRowBinning(string fixture, string poolType)
    {
        var rows = Parse(fixture, poolType);
        var noDate = rows.Where(r => string.IsNullOrWhiteSpace(r.Date)).ToArray();

        Assert.True(noDate.Length <= 3,
            $"Без даты {noDate.Length} строк из {rows.Length} — похоже, строки снова режутся по Y. " +
            string.Join("; ", noDate.Take(5).Select(r =>
                $"{r.EventStyleName} {r.EventStyleLen} {r.EventStyleGender} {r.EventStyleAge} {r.Time}")));
    }

    /// <summary>
    /// Имя не разорвано на куски. Одиночная БУКВА в имени — верный признак того, что слово
    /// собрали через пробел («אמיל י גולו ס»); одиночная запятая в составе эстафеты законна,
    /// поэтому проверяем именно буквы.
    /// </summary>
    [Theory]
    [InlineData("isr-age-records-25m.pdf", "25m")]
    [InlineData("isr-age-records-50m.pdf", "50m")]
    public void HolderName_IsNotSplitIntoLetters(string fixture, string poolType)
    {
        var rows = Parse(fixture, poolType);
        var broken = rows
            .Select(r => (r, full: $"{r.FirstName} {r.LastName}".Trim()))
            .Where(x => x.full.Length > 0 && LoneLetter.IsMatch(x.full))
            .ToArray();

        Assert.True(broken.Length == 0,
            "Имена собраны по буквам: " + string.Join("; ", broken.Take(5).Select(x => $"'{x.full}'")));
    }

    /// <summary>
    /// Состав эстафеты доезжает целиком: четыре ноги — три запятые. Проверяем только строки,
    /// где состав вообще есть (у части эстафет справочник имён не приводит).
    /// </summary>
    [Theory]
    [InlineData("isr-age-records-25m.pdf", "25m")]
    [InlineData("isr-age-records-50m.pdf", "50m")]
    public void RelayRoster_KeepsAllFourLegs(string fixture, string poolType)
    {
        var rows = Parse(fixture, poolType);
        var relays = rows
            .Where(r => r.IsRelay == true)
            .Select(r => $"{r.FirstName} {r.LastName}".Trim())
            .Where(n => n.Contains(','))
            .ToArray();

        Assert.NotEmpty(relays);

        // Хотя бы у девяти из десяти составов все четыре ноги на месте. Остаток — строки, где
        // сам справочник пишет неполный состав.
        var full = relays.Count(n => n.Count(c => c == ',') >= 3);
        Assert.True(full >= relays.Length * 0.9,
            $"Составов с четырьмя ногами {full} из {relays.Length} — крайние слова снова утекают в соседние колонки.");
    }
}

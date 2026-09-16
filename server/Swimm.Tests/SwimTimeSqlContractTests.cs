using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Два разбора времени должны давать один результат: <see cref="SwimTime.ParseToMs"/> в C# и
/// функция <c>swim_time_ms</c> в Postgres, из которой считается вычисляемая колонка
/// <c>Records.TimeMs</c> (миграция AddRecordTimeMs, этап 11.2.1).
///
/// Почему разборов два. Колонка обязана быть вычисляемой: рекорды пишут четыре разных места
/// плюс psql и восстановление дампа, и ручная синхронизация двух колонок разъезжается молча.
/// Значит разбор нужен в SQL. А C#-разбор нужен приложению независимо — им живут сторож
/// правдоподобия и сверка с протоколами. Убрать одну из копий нельзя, поэтому их держит тест.
///
/// Фикстура — НЕ придуманные примеры, а все различающиеся значения из живого справочника
/// (2615 строк на 16.09.2026) вместе с тем, что посчитала БД. То есть проверяется не «как
/// я думаю, должно быть», а фактическое поведение Postgres на реальных данных.
///
/// Пересобрать после большой заливки:
/// <code>
/// docker exec swimm-postgres psql -U swimm -d swimm -t -A -F',' \
///   -c 'SELECT DISTINCT "Time","TimeMs" FROM "Records" ORDER BY "Time";' \
///   > server/Swimm.Tests/Fixtures/record-time-ms.csv
/// </code>
/// Упал тест — разбор разъехался: чинить надо ТОТ, который неправ, а не подгонять фикстуру.
/// </summary>
public class SwimTimeSqlContractTests
{
    private static string FixturePath()
    {
        // Файл копируется в выходной каталог (см. Swimm.Tests.csproj).
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "record-time-ms.csv");
        Assert.True(File.Exists(path), $"Нет фикстуры {path} — не скопировалась в bin?");
        return path;
    }

    [Fact]
    public void CSharpParser_AgreesWithDatabase_OnEveryRealRecordTime()
    {
        var mismatches = new List<string>();
        var checkedCount = 0;

        foreach (var line in File.ReadLines(FixturePath()))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Время содержит «:» и «.», но не запятую — разделитель однозначен.
            var comma = line.LastIndexOf(',');
            var time = line[..comma];
            var fromDb = line[(comma + 1)..].Trim();

            checkedCount++;
            var fromCode = SwimTime.ParseToMs(time);
            var expected = string.IsNullOrEmpty(fromDb) ? (int?)null : int.Parse(fromDb);

            if (fromCode != expected)
                mismatches.Add($"'{time}': БД {expected?.ToString() ?? "NULL"}, C# {fromCode?.ToString() ?? "NULL"}");
        }

        Assert.True(checkedCount > 2000, $"Фикстура подозрительно мала: {checkedCount} строк.");
        Assert.Empty(mismatches);
    }

    /// <summary>
    /// Отдельно — три формы записи и края дробной части: фикстура покрывает то, что реально
    /// встретилось, а эти случаи должны работать независимо от того, попались ли они в данных.
    /// </summary>
    [Theory]
    [InlineData("25.37", 25_370)]          // сс.дд
    [InlineData("59.9", 59_900)]           // один знак дроби дополняется нулями
    [InlineData("48.725", 48_725)]         // три знака — уже миллисекунды
    [InlineData("01:02.36", 62_360)]       // мм:сс.дд
    [InlineData("16:34.89", 994_890)]
    [InlineData("01:14:08.7", 4_448_700)]  // ч:мм:сс.д — 1500 в/с у столетних
    [InlineData("", null)]
    [InlineData("DSQ", null)]              // в справочнике встречаются пометки вместо времени
    [InlineData("1:02", null)]             // без дробной части формат не наш
    public void ParseToMs_KnownForms(string time, int? expected)
        => Assert.Equal(expected, SwimTime.ParseToMs(time));
}

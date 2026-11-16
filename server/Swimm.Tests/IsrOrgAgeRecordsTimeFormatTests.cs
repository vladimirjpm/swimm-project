using Swimm.Parsing.Parsers.IsrOrgAgeRecords;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Время в PDF федерации бывает без ведущего нуля в минутах (16.11.2026: «2:09.33» у свежего
/// национального рекорда Лии Полонски, 200 баттерфляй 25 м, файл 07_11_2026). Строгое
/// «две цифры минут» молча выкидывало строку, и на её месте оставался рекорд World Aquatics.
/// Живой прогон обоих файлов: 984 → 985 строк, добавилась ровно эта.
/// </summary>
public class IsrOrgAgeRecordsTimeFormatTests
{
    [Theory]
    [InlineData("2:09.33", "02:09.33")]
    [InlineData("02:09.33", "02:09.33")]
    [InlineData("00:24.46", "00:24.46")]
    [InlineData("16:31.53", "16:31.53")]
    [InlineData("", "")]
    public void Minutes_are_padded_to_two_digits(string raw, string expected) =>
        Assert.Equal(expected, IsrOrgAgeRecordsParser.NormalizeTime(raw));
}

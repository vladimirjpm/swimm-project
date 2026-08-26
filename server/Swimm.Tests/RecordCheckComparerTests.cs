using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка подозрительного заплыва превью с карточкой пловца на loglig.
///
/// Смысл проверки: протокол мы качаем С loglig, значит его время обязано быть и в карточке.
/// Совпало — рекорд настоящий; в карточке время ХУЖЕ — разбор поехал (эталон Маккабиады:
/// 00:32.59 на 100 баттерфляем).
/// </summary>
public class RecordCheckComparerTests
{
    private static LogligResultRow Row(
        string style, string distance, string time, int ms, int pool = 25, bool relay = false) =>
        new($"{distance} {style}", distance, style, relay, pool, time, ms,
            new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc), "Meet");

    [Fact]
    public void Confirms_WhenCardHasTheSameTime()
    {
        var (verdict, message) = RecordCheckComparer.Compare(
            [Row("backstroke", "50", "00:25.62", 25_620)],
            "backstroke", "50", "25m", "00:25.62");

        Assert.Equal(RecordCheckVerdict.Confirms, verdict);
        Assert.Contains("то же время", message);
    }

    [Fact]
    public void Confirms_WhenRoundingDiffersWithinTolerance()
    {
        // Федерация печатает сотые, loglig хранит тысячные — 20 мс это то же самое время.
        var (verdict, _) = RecordCheckComparer.Compare(
            [Row("freestyle", "100", "00:55.04", 55_040)],
            "freestyle", "100", "25m", "00:55.06");

        Assert.Equal(RecordCheckVerdict.Confirms, verdict);
    }

    [Fact]
    public void Contradicts_WhenCardTimeIsMuchWorse()
    {
        // Ровно тот случай, ради которого проверка и делалась: в файле нереальное время.
        var (verdict, message) = RecordCheckComparer.Compare(
            [Row("butterfly", "100", "01:05.10", 65_100)],
            "butterfly", "100", "25m", "00:32.59");

        Assert.Equal(RecordCheckVerdict.Contradicts, verdict);
        Assert.Contains("пометить заплыв сомнительным", message);
        Assert.Contains("32.51", message);   // расхождение в секундах
    }

    [Fact]
    public void Confirms_WhenCardHasEvenFasterTime()
    {
        // Пловец уже плыл быстрее — значит время из файла ему по силам, разбор правдоподобен.
        var (verdict, _) = RecordCheckComparer.Compare(
            [Row("freestyle", "50", "00:24.90", 24_900)],
            "freestyle", "50", "25m", "00:25.70");

        Assert.Equal(RecordCheckVerdict.Confirms, verdict);
    }

    [Fact]
    public void NoData_WhenCardHasNoSuchEvent()
    {
        var (verdict, message) = RecordCheckComparer.Compare(
            [Row("freestyle", "50", "00:24.90", 24_900)],
            "breaststroke", "200", "25m", "02:30.00");

        Assert.Equal(RecordCheckVerdict.NoData, verdict);
        Assert.Contains("сверить не с чем", message);
    }

    [Fact]
    public void PoolLengthMatters()
    {
        // 50-метровый бассейн — другая дисциплина; сравнивать с 25 м нельзя.
        var (verdict, _) = RecordCheckComparer.Compare(
            [Row("backstroke", "50", "00:25.62", 25_620, pool: 25)],
            "backstroke", "50", "50m", "00:28.00");

        Assert.Equal(RecordCheckVerdict.NoData, verdict);
    }

    [Fact]
    public void RelayRowsAreIgnored()
    {
        // Этап эстафеты плывётся с хода и всегда быстрее — как эталон он не годится.
        var (verdict, _) = RecordCheckComparer.Compare(
            [Row("freestyle", "50", "00:24.10", 24_100, relay: true)],
            "freestyle", "50", "25m", "00:25.70");

        Assert.Equal(RecordCheckVerdict.NoData, verdict);
    }

    [Fact]
    public void NotChecked_WhenFileTimeIsUnparsable()
    {
        var (verdict, _) = RecordCheckComparer.Compare(
            [Row("freestyle", "50", "00:24.90", 24_900)],
            "freestyle", "50", "25m", "DQ");

        Assert.Equal(RecordCheckVerdict.NotChecked, verdict);
    }

    [Fact]
    public void Verdict_IsSerializedAsString()
    {
        // Панель различает вердикты по имени — глобального конвертера enum'ов у API нет.
        Assert.Equal("\"Contradicts\"",
            System.Text.Json.JsonSerializer.Serialize(RecordCheckVerdict.Contradicts));
        Assert.Equal("\"Many\"",
            System.Text.Json.JsonSerializer.Serialize(PreviewSwimmerMatch.Many));
    }
}

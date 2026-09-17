using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Формат архива источников: выгрузка разобранных строк в CSV и её запись на диск
/// (`!records-sources/README.md`, правило 11 в docs/pre-push-rules.md).
///
/// Почему это вообще под тестом. Ценность архива — в том, что выгрузки РАЗНЫХ дней
/// сравниваются построчно и сверяются контрольной суммой. Сломать это можно молча и насовсем:
/// поменять порядок сортировки, отдать CRLF вместо LF, дописать BOM. Файл при этом выглядит
/// нормально, а `git diff` показывает «изменилось всё» и `sha256sum -c` не сходится на другой
/// машине. Заметить это можно только через месяц — и как раз тогда, когда архив понадобился.
/// </summary>
public class RecordCsvDumpTests
{
    private static ParsedRecordDto Row(
        string regionCode, string style, string distance, string time, string? holder = "Some One") =>
        new(
            RegionType: "country", RegionCode: regionCode, Category: "open", AgeKey: "",
            Gender: "female", PoolType: "25m", Style: style, Distance: distance,
            Time: time, HolderName: holder, Club: null, HolderCountry: regionCode,
            RecordDate: "01/01/2020");

    [Fact]
    public void Build_StartsWithHeader_AndEndsWithNewline()
    {
        var csv = RecordCsvDump.Build([Row("AGU", "freestyle", "50m", "24.11")]);

        Assert.StartsWith(RecordCsvDump.Header + "\n", csv);
        Assert.EndsWith("\n", csv);
    }

    /// <summary>
    /// LF и только LF. CRLF разъехал бы контрольную сумму между Windows и Linux-раннером —
    /// та же беда, что ловили с бандлом админки.
    /// </summary>
    [Fact]
    public void Build_UsesLineFeedOnly()
    {
        var csv = RecordCsvDump.Build([
            Row("AGU", "freestyle", "50m", "24.11"),
            Row("JAM", "butterfly", "100m", "57.02"),
        ]);

        Assert.DoesNotContain("\r", csv);
    }

    /// <summary>
    /// Порядок строк не зависит от того, в каком порядке их принёс прогон: иначе diff двух
    /// выгрузок показывал бы перестановки вместо изменений.
    /// </summary>
    [Fact]
    public void Build_SortsRows_RegardlessOfInputOrder()
    {
        var a = RecordCsvDump.Build([
            Row("JAM", "freestyle", "50m", "24.11"),
            Row("AGU", "butterfly", "100m", "57.02"),
        ]);
        var b = RecordCsvDump.Build([
            Row("AGU", "butterfly", "100m", "57.02"),
            Row("JAM", "freestyle", "50m", "24.11"),
        ]);

        Assert.Equal(a, b);
        Assert.True(a.IndexOf("AGU", StringComparison.Ordinal) < a.IndexOf("JAM", StringComparison.Ordinal));
    }

    /// <summary>
    /// Состав эстафеты идёт через запятую — то есть через разделитель CSV. Без кавычек такая
    /// строка сдвинула бы все колонки вправо и сделала бы выгрузку нечитаемой.
    /// </summary>
    [Fact]
    public void Build_QuotesFieldsWithCommas()
    {
        var csv = RecordCsvDump.Build([
            Row("JAM", "freestyle", "4X50m", "1:35.21", "Anna Santamans, Beryl Gastaldello"),
        ]);

        Assert.Contains("\"Anna Santamans, Beryl Gastaldello\"", csv);
    }

    [Fact]
    public void Build_EscapesQuotes()
    {
        var csv = RecordCsvDump.Build([Row("JAM", "freestyle", "50m", "24.11", "The \"Flash\"")]);

        Assert.Contains("\"The \"\"Flash\"\"\"", csv);
    }

    /// <summary>Держателя у 132 иностранных строк нет вовсе — пустая клетка, а не пропуск колонки.</summary>
    [Fact]
    public void Build_KeepsColumnCount_WhenHolderIsMissing()
    {
        var csv = RecordCsvDump.Build([Row("AGU", "freestyle", "50m", "24.11", null)]);

        var columns = csv.Split('\n')[1].Split(',').Length;
        Assert.Equal(RecordCsvDump.Header.Split(',').Length, columns);
    }

    // ── запись на диск ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Имя по договору README архива: источник, охват и дата скачивания. Дата — та, когда
    /// качали мы, поэтому часы подставляются снаружи.
    /// </summary>
    [Fact]
    public void FileName_CarriesSourceScopeAndFetchDate()
        => Assert.Equal(
            "worldrecords-countries__fetched-2026-09-17.csv",
            FileRecordRunArchive.FileName("worldrecords", new DateTime(2026, 9, 17)));

    [Fact]
    public async Task Save_WritesFileWithoutBom()
    {
        var dir = Path.Combine(Path.GetTempPath(), "swimm-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archive = new FileRecordRunArchive(
                dir, NullLogger<FileRecordRunArchive>.Instance, () => new DateTime(2026, 9, 17));

            var result = await archive.SaveCountryRunAsync("worldrecords", null, "a,b\n1,2\n");

            Assert.Null(result.Error);
            Assert.NotNull(result.Path);

            var bytes = await File.ReadAllBytesAsync(result.Path!);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "BOM сдвинет контрольную сумму файла, который коммитится и сверяется");
            Assert.Equal("a,b\n1,2\n", await File.ReadAllTextAsync(result.Path!));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Отладочный прогон подмножеством на диск не попадает — и папку не создаёт.</summary>
    [Fact]
    public async Task Save_SkipsSubsetRuns()
    {
        var dir = Path.Combine(Path.GetTempPath(), "swimm-archive-" + Guid.NewGuid().ToString("N"));
        var archive = new FileRecordRunArchive(dir, NullLogger<FileRecordRunArchive>.Instance);

        var result = await archive.SaveCountryRunAsync("worldrecords", ["BER"], "a,b\n");

        Assert.Null(result.Path);
        Assert.Null(result.Error);
        Assert.False(Directory.Exists(dir));
    }

    /// <summary>Пустой путь в конфиге = архив выключен, а не падение прогона.</summary>
    [Fact]
    public async Task Save_IsOff_WhenDirectoryNotConfigured()
    {
        var archive = new FileRecordRunArchive(null, NullLogger<FileRecordRunArchive>.Instance);

        var result = await archive.SaveCountryRunAsync("worldrecords", null, "a,b\n");

        Assert.Null(result.Path);
        Assert.Null(result.Error);
    }

    /// <summary>
    /// Беда записи возвращается, а не бросается: прогон по 215 странам идёт час-два, и терять
    /// его результат из-за файла нельзя.
    /// </summary>
    [Fact]
    public async Task Save_ReturnsError_InsteadOfThrowing()
    {
        // Файл вместо папки — Directory.CreateDirectory на него ругается.
        var occupied = Path.Combine(Path.GetTempPath(), "swimm-archive-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(occupied, "");
        try
        {
            var archive = new FileRecordRunArchive(occupied, NullLogger<FileRecordRunArchive>.Instance);

            var result = await archive.SaveCountryRunAsync("worldrecords", null, "a,b\n");

            Assert.Null(result.Path);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }
        finally
        {
            File.Delete(occupied);
        }
    }
}

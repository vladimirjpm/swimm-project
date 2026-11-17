using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>Файловое сохранённое прогона: круг записи-чтения, срок годности, чужой источник.</summary>
public class FileRecordCountryRunProgressTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "swimm-tests-progress-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);

    private FileRecordCountryRunProgress Progress() =>
        new(_dir, NullLogger<FileRecordCountryRunProgress>.Instance, () => _now);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task RoundTrip_KeepsRowsMismatchesAndWorld()
    {
        var p = Progress();
        await p.BeginAsync("worldrecords");
        await p.SaveWorldAsync([Row("world", "")]);
        await p.SaveCountryAsync(new RecordCountryFetchResult("RUS", [Row("country", "RUS", team: true)],
            [new RecordCountryMismatchDto("RUS", "", "25m", "butterfly", "50m", "male", "21.28")]));

        _now = _now.AddHours(2);
        var saved = await Progress().LoadAsync("worldrecords");

        Assert.NotNull(saved);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 20, 0, 0, TimeSpan.Zero), saved!.StartedAt);
        Assert.Single(saved.World!);
        var rus = saved.Countries["rus"];   // регистр кода не важен
        var row = Assert.Single(rus.Records);
        Assert.Equal(Row("country", "RUS", team: true), row);
        Assert.Single(rus.Mismatches);
    }

    [Fact]
    public async Task Load_IgnoresSavedOlderThanADay()
    {
        var p = Progress();
        await p.BeginAsync("worldrecords");
        await p.SaveCountryAsync(new RecordCountryFetchResult("AGU", [Row("country", "AGU")], []));

        _now = _now.Add(FileRecordCountryRunProgress.MaxAge).AddMinutes(1);

        Assert.Null(await Progress().LoadAsync("worldrecords"));
    }

    [Fact]
    public async Task Load_IgnoresOtherSource()
    {
        await Progress().BeginAsync("worldrecords");
        Assert.Null(await Progress().LoadAsync("wa-junior"));
    }

    [Fact]
    public async Task Begin_And_Clear_DropPreviousCountries()
    {
        var p = Progress();
        await p.BeginAsync("worldrecords");
        await p.SaveCountryAsync(new RecordCountryFetchResult("AGU", [Row("country", "AGU")], []));

        await p.BeginAsync("worldrecords");
        Assert.Empty((await p.LoadAsync("worldrecords"))!.Countries);

        await p.ClearAsync();
        Assert.Null(await p.LoadAsync("worldrecords"));
    }

    /// <summary>Испорченный файл одной страны — её скачаем заново, остальные целы.</summary>
    [Fact]
    public async Task Load_SkipsCorruptCountryFile()
    {
        var p = Progress();
        await p.BeginAsync("worldrecords");
        await p.SaveCountryAsync(new RecordCountryFetchResult("AGU", [Row("country", "AGU")], []));
        await File.WriteAllTextAsync(Path.Combine(_dir, "country-JAM.json"), "{ оборвано");

        var saved = await p.LoadAsync("worldrecords");

        Assert.Equal(["AGU"], saved!.Countries.Keys);
    }

    /// <summary>Код идёт в имя файла: ничего похожего на путь записать нельзя.</summary>
    [Fact]
    public async Task SaveCountry_RefusesCodeThatIsNotPlain()
    {
        var p = Progress();
        await p.BeginAsync("worldrecords");
        await p.SaveCountryAsync(new RecordCountryFetchResult("..\\X", [Row("country", "X")], []));

        Assert.Empty((await p.LoadAsync("worldrecords"))!.Countries);
        Assert.Equal<string>(["run.json"], Directory.GetFiles(_dir).Select(f => Path.GetFileName(f)).ToArray());
    }

    private static ParsedRecordDto Row(string regionType, string code, bool team = false) =>
        new(regionType, code, "open", "", "male", "25m", "butterfly", "50m", "21.28",
            "KORNEV Egor", null, code.Length > 0 ? code : "RUS", "15/11/2026", team);
}

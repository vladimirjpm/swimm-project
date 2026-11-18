using Swimm.Parsing.Models;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;
using Xunit;
using Xunit.Abstractions;

namespace Swimm.Tests;

/// <summary>
/// Эстафета без состава в PDF возрастных рекордов федерации печатается одним клубом
/// («מכבי קרית ביאליק»). Колонка имени у неё пустая, и пересплит имени/клуба по «самому
/// широкому зазору» резал название клуба: «מכבי» уезжало в держателя, «קרית ביאליק» — в клуб
/// (8 строк справочника, Р94 в docs/data-integrity.md, 18.11.2026).
///
/// Тест идёт по настоящим архивным PDF (`!records-sources`, коммитятся), а не по фикстуре:
/// сломалась геометрия именно этого файла.
/// </summary>
public class IsrOrgAgeRecordsRelayClubSplitTests
{
    private const string Pdf25 = "isrorg-age-25m__source-2026-09-17__fetched-2026-09-18.pdf";
    private const string Pdf50 = "isrorg-age-50m__source-2026-09-17__fetched-2026-09-18.pdf";

    private readonly ITestOutputHelper _out;
    public IsrOrgAgeRecordsRelayClubSplitTests(ITestOutputHelper output) => _out = output;

    private static string SourcePath(string file, [System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "!records-sources"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "!records-sources", file);
    }

    private static List<Result> Parse(string file, string pool)
    {
        using var fs = File.OpenRead(SourcePath(file));
        return new IsrOrgAgeRecordsParser()
            .Parse(new ParseRequest(fs, $"isrorg-age-{pool}.pdf", PoolType: pool))
            .ToList();
    }

    private static string Holder(Result r) => $"{r.FirstName} {r.LastName}".Trim();

    private static bool IsRelay(Result r) =>
        r.EventStyleLen.Contains('X', StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("02:20.20", "10", "מכבי קרית-ביאליק")]
    [InlineData("02:20.20", "11", "מכבי קרית-ביאליק")]
    [InlineData("02:33.30", "10", "מכבי קרית ביאליק")]
    [InlineData("02:33.30", "11", "מכבי קרית ביאליק")]
    public void RelayWithoutLineup_WholeClub_NoHolder(string time, string age, string club)
    {
        var row = Parse(Pdf25, "25m").Single(r => IsRelay(r) && r.Time.EndsWith(time) && r.EventStyleAge == age);

        Assert.Equal("", Holder(row));
        Assert.Equal(club, row.Club);
    }

    /// <summary>
    /// Сторож на оба файла: держатель эстафеты не бывает голой приставкой клуба. Так
    /// выглядел ровно этот дефект — первое слово названия клуба в поле имени.
    /// </summary>
    [Fact]
    public void NoRelayHolder_IsBareClubPrefix()
    {
        string[] prefixes = ["מכבי", "הפועל", "אליצור", "בית\"ר"];
        var relays = Parse(Pdf25, "25m").Concat(Parse(Pdf50, "50m")).Where(IsRelay).ToList();
        foreach (var r in relays.Where(r => !Holder(r).Contains(',')))
            _out.WriteLine($"{r.PoolType} {r.EventStyleLen} {r.EventStyleAge} {r.Time} holder='{Holder(r)}' club='{r.Club}'");

        Assert.NotEmpty(relays);
        Assert.DoesNotContain(relays, r => prefixes.Contains(Holder(r)));
    }

    /// <summary>Состав, который в PDF есть, пересплит по-прежнему отдаёт именами, а не клубом.</summary>
    [Fact]
    public void RelayWithLineup_KeepsNames()
    {
        var relays = Parse(Pdf25, "25m").Where(IsRelay).ToList();
        Assert.Contains(relays, r => Holder(r).Split(',').Length == 4);
    }
}

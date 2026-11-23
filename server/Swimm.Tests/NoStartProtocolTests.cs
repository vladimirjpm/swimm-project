using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Swimm.Parsing;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Протокол из одних неявок = «тянуть нечего». Живой случай — «ליגה בוגרים 2 -קיץ 2025»
/// (compID 14676, loglig 12637, 13.06.2025): 273 строки «0 NS», ни одного времени, на loglig
/// «סה"כ משתתפים» = 0 у всех дисциплин. До правила превью собиралось, пометка «∅ нет
/// протокола» снималась, а строка висела в «ждёт затягивания».
/// </summary>
public class NoStartProtocolTests
{
    private static string Rows(params object[] rows) => JsonSerializer.Serialize(rows);

    private static object Ns(string note = "NS") => new { time = (string?)null, time_fail = true, time_fail_note = note };
    private static object Swum(string time = "00:31.20") => new { time, time_fail = false, time_fail_note = (string?)null };

    [Fact]
    public void AllNs_IsNoStart()
    {
        Assert.True(NoStartProtocol.AllNoStart(Rows(Ns(), Ns("DNS"), Ns("ns "))));
    }

    [Fact]
    public void OneSwum_IsNotNoStart()
    {
        // Хоть одно время — протокол настоящий, неявки в нём — обычное дело.
        Assert.False(NoStartProtocol.AllNoStart(Rows(Ns(), Ns(), Swum())));
    }

    [Fact]
    public void DqAndDnf_AreStarts()
    {
        // DQ/DNF — пловец стартовал (StartStatus.IsNoStart), протокол не пустой.
        Assert.False(NoStartProtocol.AllNoStart(Rows(Ns(), Ns("DQ"))));
        Assert.False(NoStartProtocol.AllNoStart(Rows(Ns(), Ns("DNF"))));
    }

    [Fact]
    public void Empty_IsNotNoStart()
    {
        // Пустой разбор — свой случай («парсер не распознал ни одного результата»).
        Assert.False(NoStartProtocol.AllNoStart("[]"));
        Assert.False(NoStartProtocol.AllNoStart(""));
        Assert.False(NoStartProtocol.AllNoStart(null));
    }

    [Fact]
    public void StringTimeFail_IsUnderstood()
    {
        Assert.True(NoStartProtocol.AllNoStart("""[{"time_fail":"true","time_fail_note":"NS"}]"""));
    }

    [Fact]
    public void Message_IsClassifiedAsEmpty()
    {
        // Пачка и авто-забор должны прочитать это как «протокола нет», а не как сбой.
        var (verdict, _) = BulkPullClassifier.Classify(
            DiscoveryPreviewResult.Failed(NoStartProtocol.Message(273)),
            regulation: null, isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.Empty, verdict);
    }

    [Fact]
    public async Task RealPdf_14676_AllRowsNoStart()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing",
            "LeagueAdults2Summer2025_14676_all_ns_HE.pdf");
        await using var fs = File.OpenRead(path);

        var provider = new PdfResultSourceProvider([new IsrOrgParser()]);
        var parsed = await provider.ParseAsync(new ResultSourceRequest(
            fs, "isrorg-14676-loglig-12637-he.pdf", "IsrOrg", Language: "he"));

        Assert.True(parsed.ResultCount > 0);
        Assert.True(NoStartProtocol.AllNoStart(parsed.ResultsJson));
    }
}

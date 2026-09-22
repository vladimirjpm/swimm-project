using Swimm.Parsing.Models;
using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пол эстафет в PDF рекордов федерации (records-relays-plan Э3, 22.09.2026). Метка пола —
/// объединённая ячейка по центру группы; пол назначается БЛОКУ, а блок режется возрастной
/// лестницей и разрывом страницы. Три живых промаха выпуска 17.09.2026 (50 м), сверено с
/// картинкой страниц 7–8:
/// <list type="bullet">
/// <item>03:32.04 (4×100 компл., «ישראל») — МУЖСКАЯ, первая строка блока; уезжала в «מיקס»
/// из конца блока 4×50 — и сторож принял её за рекорд быстрее мирового.</item>
/// <item>09:55.22 (4×200 в/с, 13 и 12) — ЖЕНСКАЯ; «גיל 15 - גילאים / אגודות» подряд резали
/// блок надвое, хвост уходил к метке «מיקס».</item>
/// <item>07:32.96 (4×200 в/с, «ישראל») — СМЕШАННАЯ, последняя строка страницы; слипалась с
/// мужским блоком 4×50 компл. следующей страницы.</item>
/// </list>
/// </summary>
public class IsrOrgAgeRelayGenderTests
{
    private static readonly Lazy<Result[]> Rows = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "isr-age-records-50m-2026-09.pdf");
        using var fs = File.OpenRead(path);
        return new IsrOrgAgeRecordsParser()
            .Parse(new ParseRequest(fs, Path.GetFileName(path), PoolType: "50m"))
            .ToArray();
    });

    private static Result Relay(string time, string age) =>
        Assert.Single(Rows.Value, r => r.IsRelay == true && r.Time == time && r.EventStyleAge == age);

    [Fact]
    public void FirstRowOfBlock_KeepsItsOwnGender()
    {
        var r = Relay("03:32.04", Rows.Value.First(x => x.Time == "03:32.04").EventStyleAge);
        Assert.Equal(("male", "individual_medley", "4X100"), (r.EventStyleGender, r.EventStyleName, r.EventStyleLen));
    }

    [Fact]
    public void TwoRowsOfOneAge_DoNotSplitTheBlock()
    {
        Assert.All(Rows.Value.Where(r => r.IsRelay == true && r.Time == "09:55.22"),
            r => Assert.Equal("female", r.EventStyleGender));
    }

    [Fact]
    public void LastRowOfPage_IsItsOwnBlock()
    {
        var r = Assert.Single(Rows.Value, x => x.IsRelay == true && x.Time == "07:32.96");
        Assert.Equal(("mix", "freestyle", "4X200"), (r.EventStyleGender, r.EventStyleName, r.EventStyleLen));
    }
}

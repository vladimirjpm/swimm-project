using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Закреплённый в настройках адрес PDF федерации — в ссылках для админки (17.11.2026).
/// Ссылка «שיאי מאסטרס: בריכת 25 מטר» на сайте федерации с августа ведёт на чужой файл (И-15),
/// и мастерсы 25 м качаются по адресу из настроек. Админка показывала только «⚠ битая ссылка»,
/// и выглядело так, будто файл не грузится вовсе.
/// </summary>
public class IsrOrgRecordsPinnedUrlTests
{
    private const string Pinned = "https://isr.org.il/pics/masters-short-1_26.pdf";

    private static IsrOrgRecordsLink Link(bool isMasters, string pool, bool trusted = true) =>
        new($"https://isr.org.il/pics/{(isMasters ? "m" : "a")}-{pool}.pdf", "label", pool, isMasters, null, trusted);

    [Theory]
    [InlineData(true, "25m", "RecordsImport:IsrOrgMastersRecordsUrl25m")]
    [InlineData(true, "50m", "RecordsImport:IsrOrgMastersRecordsUrl50m")]
    [InlineData(false, "25m", "RecordsImport:IsrOrgAgeRecordsUrl25m")]
    [InlineData(false, "50m", "RecordsImport:IsrOrgAgeRecordsUrl50m")]
    public void PinnedUrlKey_MatchesAppsettingsKeys(bool isMasters, string pool, string expected) =>
        Assert.Equal(expected, IsrOrgRecordsPageResolver.PinnedUrlKey(isMasters, pool));

    [Fact]
    public void ToLinkDtos_PinnedOnlyOnItsOwnFile()
    {
        var settings = new Dictionary<string, string?>
        {
            ["RecordsImport:IsrOrgMastersRecordsUrl25m"] = " " + Pinned + " ",
            ["RecordsImport:IsrOrgAgeRecordsUrl25m"] = "",
        };

        var dtos = IsrOrgRecordsPageResolver.ToLinkDtos(
            [Link(true, "25m", trusted: false), Link(true, "50m"), Link(false, "25m"), Link(false, "50m")],
            key => settings.GetValueOrDefault(key));

        var masters25 = Assert.Single(dtos, d => d.IsMasters && d.PoolType == "25m");
        Assert.Equal(Pinned, masters25.PinnedUrl);
        Assert.False(masters25.Trusted);

        // Пустая строка в настройках — «не закреплено», как и у провайдеров.
        Assert.All(dtos.Where(d => d != masters25), d => Assert.Null(d.PinnedUrl));
    }
}

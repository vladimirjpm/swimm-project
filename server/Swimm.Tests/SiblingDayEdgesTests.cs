using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.API.Cli;
using Swimm.API.Controllers;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р124, края (ревью PR #284): превью соседа не трогает запись федерации, обычный импорт его превью не
/// берёт, сверка заплывов с запасным ключом, CLI <c>--join-days --name</c>.
/// </summary>
public class SiblingDayEdgesTests
{
    [Fact]
    public async Task PreviewSibling_FetchesSiblingNumber_DoesNotMarkFederationRow()
    {
        var row = BulkPullServiceTests.Row(5); // LogligId 12005
        var discovery = new Mock<ICompetitionDiscoveryService>();
        discovery.Setup(d => d.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([row]);
        var provider = new Mock<ICompetitionDiscoveryProvider>();
        provider.Setup(p => p.FetchResultsPdfAsync(12007, "he-IL", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        provider.Setup(p => p.FetchResultsPdfAsync(12007, "en-US", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("нет EN"));
        var source = new Mock<IResultSourceProvider>();
        // Пустой PDF: у обычного «Затянуть» запись получила бы пометку «нет протокола».
        source.Setup(s => s.ParseAsync(It.IsAny<ResultSourceRequest>())).ThrowsAsync(new InvalidOperationException("No competitions found in PDF"));
        var service = new DiscoveryPreviewService(discovery.Object, provider.Object, source.Object,
            Mock.Of<IImportService>(), Mock.Of<IImportRecordPreviewService>(), Mock.Of<IOfficialClubStandingService>(),
            Mock.Of<IRegulationFetchService>(), Mock.Of<ICategoryRepository>(), Mock.Of<IRelaySplitProvider>(),
            Mock.Of<ICollapsedDaysFixer>(), Mock.Of<IVenuePoolService>(), null!, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DiscoveryPreviewService>.Instance);

        var r = await service.PreviewSiblingAsync(row.Id, 12007);

        Assert.NotNull(r.Error);
        provider.Verify(p => p.FetchResultsPdfAsync(12005, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        discovery.Verify(d => d.SetEmptySourceAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        discovery.Verify(d => d.AddLanguagesAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        discovery.Verify(d => d.RefreshDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoveryImport_SiblingPreview_Rejected_NothingQueued()
    {
        var previewId = Guid.NewGuid();
        var previews = new Mock<IDiscoveryPreviewService>();
        previews.Setup(p => p.GetEntry(previewId)).Returns(new DiscoveryPreviewEntry(
            new ParsedCompetition { Format = "IsrOrg", ResultsJson = "[]" }, "f.pdf", 155, null, SiblingLogligId: 11590));
        var jobs = new Mock<IImportJobQueue>(MockBehavior.Strict);
        var controller = new DiscoveryAdminController(Mock.Of<ICompetitionDiscoveryService>(), previews.Object,
            Mock.Of<IResultSourceProvider>(), Mock.Of<ISwimmerNameSyncService>(), jobs.Object, Mock.Of<IImportService>(),
            Mock.Of<IImportRecordPreviewService>(), Mock.Of<IOfficialClubStandingService>(), Mock.Of<IPointRulesAdminRepository>(),
            Mock.Of<IRegulationFetchService>(), Mock.Of<IBulkPullService>(), Mock.Of<IPreviewRecordCheckService>(),
            Mock.Of<ILogligStampService>(), Mock.Of<IAdminAuditService>(), Mock.Of<IStartListPullService>(),
            Mock.Of<IMeetInfoAdminService>(), Mock.Of<IJobRunStore>(), NullLogger<DiscoveryAdminController>.Instance);

        var result = await controller.Import(new DiscoveryAdminController.DiscoveryImportRequest(previewId, null, null, null),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        previews.Verify(p => p.RemoveEntry(It.IsAny<Guid>()), Times.Never); // превью цело — его ещё можно импортировать кнопкой дня
        jobs.VerifyNoOtherCalls();
    }

    private static SiblingSwim S(string p, int heat, int lane, int? time, DateOnly? day = null) =>
        new(p, 2010, "freestyle", "50", heat, lane, time, day);

    [Fact]
    public void OverlapCheck_HeatLane_ByDefault()
    {
        var fri = new DateOnly(2024, 12, 6);
        var r = SiblingDayRules.OverlapCheck([S("a", 1, 1, 30000), S("b", 1, 2, 31000)], [S("a", 1, 1, 99999, fri)]);
        Assert.Equal(OverlapBy.HeatLane, r.By);
        Assert.Single(r.Matched);
        Assert.Equal(fri, r.InDay);
    }

    [Fact]
    public void OverlapCheck_NoHeats_FallsBackToTime()
    {
        var file = Enumerable.Range(1, 4).Select(i => S("p" + i, 0, 0, 30000 + i)).ToList();
        var db = file.Select(f => f with { Day = new DateOnly(2024, 12, 6) }).ToList();

        var r = SiblingDayRules.OverlapCheck(file, db);

        Assert.Equal(OverlapBy.Time, r.By);
        Assert.Equal(4, r.Matched.Count);
        Assert.Contains("тот же пловец, дисциплина и время", SiblingDayRules.OverlapRefusal(r));
    }

    [Fact]
    public void OverlapCheck_NoHeatsNoTimes_None_NotRefused()
    {
        var r = SiblingDayRules.OverlapCheck([S("a", 0, 0, null), S("b", 0, 0, null)], [S("a", 0, 0, null)]);
        Assert.Equal(OverlapBy.None, r.By);
        Assert.Null(SiblingDayRules.OverlapRefusal(r));
    }

    [Theory]
    [InlineData(new[] { "--join-days", "1858,2101", "--name", "תחרות חלופית", "--apply" }, "תחרות חלופית")]
    [InlineData(new[] { "--join-days", "1858,2101", "--apply" }, null)]
    [InlineData(new[] { "--join-days", "1858,2101", "--name", "--apply" }, null)]
    public void JoinDays_NameArgument(string[] args, string? expected) =>
        Assert.Equal(expected, MultidayCli.JoinName(args));
}

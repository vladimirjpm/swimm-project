using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Discovery;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Источник промежуточных для доклейки без переимпорта. Пустой PDF у ОДНОЙ дисциплины
/// (6595, loglig 11586: мужская 4X50 комплекс 58140, 07.10.2026) ронял доклейку всего
/// соревнования — 825 личных заплывов длиннее 50 м остались без промежуточных. Теперь такая
/// дисциплина пропускается и называется в отчёте; сеть по-прежнему роняет всё.
/// </summary>
public class LogligSplitSourceProviderTests
{
    private const int RelayOk = 1, RelayMissing = 2, SwimOk = 3, SwimMissing = 4;

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", name));

    private static int TeamsIn(string name)
    {
        using var ms = new MemoryStream(Fixture(name));
        return LogligRelaySplitParser.Parse(ms).Count;
    }

    private static int SwimsIn(string name)
    {
        using var ms = new MemoryStream(Fixture(name));
        return LogligIndividualSplitParser.Parse(ms).Count;
    }

    private const string RelayPdf = "loglig-split-relay-11586-58158-women-free.pdf";
    private const string SwimPdf = "loglig-split-indiv-13805-73034-women-200im.pdf";

    [Fact]
    public async Task DisciplineWithoutPdf_IsSkippedAndNamed_RestIsRead()
    {
        var provider = new LogligSplitSourceProvider(new FakeLoglig(), NullLogger<LogligSplitSourceProvider>.Instance);

        var source = await provider.FetchAsync(11586);

        Assert.Equal(TeamsIn(RelayPdf), source.Teams.Count);
        Assert.Equal(SwimsIn(SwimPdf), source.Swims.Count);
        Assert.Contains($"(пропущены): {RelayMissing}, {SwimMissing}", source.Message);
    }

    [Fact]
    public async Task NetworkFailureOnRelay_StillFailsEverything()
    {
        var provider = new LogligSplitSourceProvider(
            new FakeLoglig(networkFailure: RelayMissing), NullLogger<LogligSplitSourceProvider>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchAsync(11586));
    }

    private sealed class FakeLoglig(int? networkFailure = null) : ICompetitionDiscoveryProvider
    {
        public Task<IReadOnlyList<LogligDisciplineGridRowDto>> FetchDisciplineGridAsync(
            int logligId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LogligDisciplineGridRowDto>>(
            [
                Row(RelayOk, "freestyle", "4X50", relay: true),
                Row(RelayMissing, "medley", "4X50", relay: true),
                Row(SwimOk, "medley", "200", relay: false),
                Row(SwimMissing, "backstroke", "100", relay: false),
            ]);

        public Task<byte[]> FetchDisciplineSplitPdfAsync(int logligId, int disciplineId, CancellationToken ct = default)
        {
            if (disciplineId == networkFailure) throw new HttpRequestException("сеть отвалилась");
            return disciplineId switch
            {
                RelayOk => Task.FromResult(Fixture(RelayPdf)),
                SwimOk => Task.FromResult(Fixture(SwimPdf)),
                _ => throw new LogligSplitPdfMissingException(logligId, disciplineId),
            };
        }

        private static LogligDisciplineGridRowDto Row(int id, string style, string distance, bool relay) =>
            new(id, id, $"{distance} {style}", "מאסטרס נ 21-99", style, distance, "female", "21-99",
                relay, null, new DateTime(2025, 1, 3, 10, 0, 0), 0, 0);

        public Task<IReadOnlyList<DiscoveredListItem>> FetchListAsync(bool finished, int? year = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<DiscoveredDetails> FetchDetailsAsync(int orgCompId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<byte[]> FetchResultsPdfAsync(int logligId, string culture = "he-IL", CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<int>> FetchEventIdsAsync(int logligId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<LogligEventResultsDto> FetchEventResultsAsync(int eventId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<LogligStartListDto> FetchStartListAsync(int disciplineId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

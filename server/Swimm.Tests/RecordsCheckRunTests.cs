using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Прогон расписания records-check: упавшие источники проверяются ещё раз после паузы, удачные —
/// нет. Повод — первый боевой прогон 16.11.2026: три источника World Aquatics из пяти упали на
/// 504 и DNS, а через полчаса два из них прошли.
/// </summary>
public class RecordsCheckRunTests
{
    private sealed class FakeChecks(params string[] failFirstTime) : IRecordSourceCheckService
    {
        private readonly HashSet<string> _failing = new(failFirstTime);
        public List<string> Single { get; } = new();

        private static RecordSourceCheckResultDto Result(string source, string outcome) =>
            new(source, 1, outcome, DateTime.UtcNow, outcome == RecordSourceCheckOutcomes.Failed ? "504" : null, null);

        public Task<IReadOnlyList<RecordSourceCheckResultDto>> CheckAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecordSourceCheckResultDto>>(
                new[] { "worldrecords", "wa-masters", "isrorg-age" }
                    .Select(s => Result(s, _failing.Contains(s) ? RecordSourceCheckOutcomes.Failed : RecordSourceCheckOutcomes.Unchanged))
                    .ToList());

        public Task<RecordSourceCheckResultDto> CheckAsync(
            string source, RecordSourceRequest? request = null, CancellationToken ct = default)
        {
            Single.Add(source);
            // Второй раз источник отвечает — как на проде через полчаса.
            return Task.FromResult(Result(source, RecordSourceCheckOutcomes.Unchanged));
        }

        public Task<RecordDiffApplyResult> ApplyAsync(RecordDiffApplyRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("Джоб ничего не применяет");

        public Task LogRunAsync(string source, RecordDiffResult? diff, string? error, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RecordSourceFreshnessDto>> GetFreshnessAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecordSourceFreshnessDto>>([]);
    }

    [Fact]
    public async Task All_ok_means_no_retry()
    {
        var checks = new FakeChecks();

        var results = await RecordsCheckRun.RunAsync(checks, TimeSpan.Zero, CancellationToken.None);

        Assert.Empty(checks.Single);
        Assert.All(results, r => Assert.Equal(RecordSourceCheckOutcomes.Unchanged, r.Outcome));
    }

    [Fact]
    public async Task Only_failed_sources_are_retried_and_their_retry_result_wins()
    {
        var checks = new FakeChecks("worldrecords", "wa-masters");

        var results = await RecordsCheckRun.RunAsync(checks, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(["worldrecords", "wa-masters"], checks.Single);
        Assert.Equal(["worldrecords", "wa-masters", "isrorg-age"], results.Select(r => r.Source));
        Assert.All(results, r => Assert.Equal(RecordSourceCheckOutcomes.Unchanged, r.Outcome));
    }
}

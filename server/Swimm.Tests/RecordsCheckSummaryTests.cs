using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Итог прогона «проверка источников рекордов» (records-check) одной строкой — то, что видно в
/// истории /Admin/Jobs и после кнопки «Проверить все источники».
/// </summary>
public class RecordsCheckSummaryTests
{
    private static RecordDiffEntry Entry(string regionType, string category) =>
        new(regionType, regionType == "country" ? "ISR" : "", category, "open", "male", "50m",
            "freestyle", "50m", "00:21.00", null, null, "00:20.90", null, null);

    private static RecordSourceCheckResultDto Result(
        string source, string outcome, IReadOnlyList<RecordDiffEntry>? added = null,
        IReadOnlyList<RecordDiffEntry>? changed = null, string? error = null)
    {
        added ??= [];
        changed ??= [];
        var diff = outcome == RecordSourceCheckOutcomes.Failed
            ? null
            : new RecordDiffResult("d-" + source, source, added.Count, changed.Count, 0, 0, added, changed);
        return new RecordSourceCheckResultDto(source, 1, outcome, DateTime.UtcNow, error, diff);
    }

    [Fact]
    public void All_unchanged_says_so()
    {
        var summary = JobRunSummaries.RecordsCheck([
            Result("worldrecords", RecordSourceCheckOutcomes.Unchanged),
            Result("isrorg-age", RecordSourceCheckOutcomes.Unchanged),
        ]);

        Assert.Equal("источников 2: без изменений 2", summary);
    }

    [Fact]
    public void Changes_and_failures_are_named_by_source()
    {
        var summary = JobRunSummaries.RecordsCheck([
            Result("worldrecords", RecordSourceCheckOutcomes.Unchanged),
            Result("wa-masters", RecordSourceCheckOutcomes.ChangesFound,
                added: [Entry("world", "masters"), Entry("world", "masters")],
                changed: [Entry("world", "masters")]),
            Result("wa-junior", RecordSourceCheckOutcomes.Failed, error: "504"),
        ]);

        Assert.Equal(
            "источников 3: без изменений 1 · ждут Apply: wa-masters +2 ~1 · сбой: wa-junior",
            summary);
    }

    [Fact]
    public void Contested_country_open_cells_are_not_counted()
    {
        // country/*/open — два хозяина (И-13): журнал их не считает, итог тоже не должен.
        var summary = JobRunSummaries.RecordsCheck([
            Result("isrorg-age", RecordSourceCheckOutcomes.ChangesFound,
                added: [Entry("age", "open")],
                changed: [Entry("country", "open"), Entry("country", "open")]),
        ]);

        Assert.Equal("источников 1: без изменений 0 · ждут Apply: isrorg-age +1 ~0", summary);
    }
}

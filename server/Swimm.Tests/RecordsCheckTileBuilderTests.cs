using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>Плитка «Рекорды · источники» на /Admin/Jobs: давность проверки, ждёт Apply, сбои, тон.</summary>
public class RecordsCheckTileBuilderTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static RecordSourceFreshnessDto Source(
        string source, DateTime? checkedAt, string? outcome = "unchanged",
        int added = 0, int changed = 0, bool needsCheck = false) =>
        new(source, checkedAt, checkedAt, outcome, outcome == "failed" ? "504" : null,
            added, changed, null, null, needsCheck);

    [Fact]
    public void Fresh_and_clean_is_ok_with_time_ago()
    {
        var tile = RecordsCheckTileBuilder.Build([
            Source("worldrecords", Now.AddHours(-3)),
            Source("isrorg-age", Now.AddHours(-2)),
        ], Now);

        Assert.Equal("2 ч назад", tile.Value);
        Assert.Equal("изменений нет", tile.Sub);
        Assert.Equal("ok", tile.Tone);
        Assert.Equal(Now.AddHours(-2), tile.LastCheckedAt);
    }

    [Fact]
    public void Pending_changes_are_named_and_warn()
    {
        var tile = RecordsCheckTileBuilder.Build([
            Source("worldrecords", Now.AddHours(-1)),
            Source("wa-masters", Now.AddHours(-1), "changes-found", added: 2, changed: 1),
        ], Now);

        Assert.Equal("ждут Apply: wa-masters +2 ~1", tile.Sub);
        Assert.Equal("warning", tile.Tone);
    }

    [Fact]
    public void Failure_is_danger_and_listed_after_pending()
    {
        var tile = RecordsCheckTileBuilder.Build([
            Source("wa-masters", Now.AddHours(-1), "changes-found", added: 1),
            Source("wa-junior", Now.AddDays(-3), "failed"),
        ], Now);

        Assert.Equal("ждут Apply: wa-masters +1 ~0 · сбой: wa-junior", tile.Sub);
        Assert.Equal("danger", tile.Tone);
    }

    [Fact]
    public void Never_checked_says_so()
    {
        var tile = RecordsCheckTileBuilder.Build([
            Source("worldrecords", null, outcome: null, needsCheck: true),
        ], Now);

        Assert.Equal("не было", tile.Value);
        Assert.Equal("ни разу не проверяли", tile.Sub);
        Assert.Equal("warning", tile.Tone);
    }

    [Fact]
    public void Countries_run_does_not_affect_the_tile()
    {
        // Прогон по странам в проверку не входит — его давность кнопка не исправит.
        var tile = RecordsCheckTileBuilder.Build([
            Source("worldrecords", Now.AddHours(-1)),
            Source("worldrecords-countries", Now.AddDays(-60), "failed", needsCheck: true),
        ], Now);

        Assert.Equal("ok", tile.Tone);
        Assert.Equal("изменений нет", tile.Sub);
    }
}

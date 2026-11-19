using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>«Закончено ли соревнование» — перенос правил из server/db/day-json.mjs (план П5, §4.4).</summary>
public class CompetitionProgressTests
{
    private static ProgressRow Swam(string ev, int heat, bool relay = false) => new(ev, relay, heat, "1:02.34", false);
    private static ProgressRow Dns(string ev, int heat, bool relay = false) => new(ev, relay, heat, "", true);

    [Fact]
    public void All_events_swum_is_finished()
    {
        var s = CompetitionProgress.Evaluate([Swam("50 free", 1), Swam("100 back", 2), Dns("100 back", 2)]);
        Assert.True(s.IsFinished);
        Assert.Empty(s.Pending);
    }

    [Fact]
    public void Event_without_any_time_is_pending_and_named()
    {
        var s = CompetitionProgress.Evaluate([Swam("50 free", 1), Dns("200 free", 1), Dns("200 free", 1)]);
        Assert.False(s.IsFinished);
        Assert.Contains("200 free", Assert.Single(s.Pending));
    }

    [Fact]
    public void Relay_with_only_first_heat_swum_is_not_finished()
    {
        var s = CompetitionProgress.Evaluate([Swam("4x50 free", 1, true), Dns("4x50 free", 2, true), Dns("4x50 free", 2, true)]);
        Assert.False(s.IsFinished);
        Assert.Contains("эстафеты", Assert.Single(s.Pending));
    }

    [Fact]
    public void Partly_DNS_but_last_heat_has_time_is_swum()
    {
        var s = CompetitionProgress.Evaluate([Dns("50 free", 1), Dns("50 free", 2), Swam("50 free", 2)]);
        Assert.True(s.IsFinished);
    }

    [Fact]
    public void Empty_is_not_finished() =>
        Assert.False(CompetitionProgress.Evaluate([]).IsFinished);

    [Fact]
    public void Zero_time_text_counts_as_no_time() =>
        Assert.False(new ProgressRow("e", false, 1, "00:00.00", false).HasTime);

    [Fact]
    public void Json_projection_matches_rows()
    {
        const string json = """
            [{"event":"A","heat":1,"time":"0:30.10","time_fail":false,"is_relay":false},
             {"event":"B","heat":1,"time":"","time_fail":true,"is_relay":null}]
            """;
        var s = CompetitionProgress.EvaluateJson(json);
        Assert.False(s.IsFinished);
        Assert.Contains("B", Assert.Single(s.Pending));
    }
}

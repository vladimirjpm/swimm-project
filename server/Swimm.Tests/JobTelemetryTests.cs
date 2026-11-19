using System.Diagnostics;
using Swimm.API.Telemetry;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Метка фоновой задачи на исходящих спанах (prod-auto-pull-plan §4.2, П2): внутри прогона —
/// <c>swimm.source=job</c> и имя задачи, вне — ничего; запрос к сайту (Server) не трогаем.
/// </summary>
public class JobTelemetryTests
{
    private static Activity Started(ActivityKind kind)
    {
        // Kind у Activity без ActivitySource не задать — берём источник со слушателем.
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "utest-job-telemetry",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("utest-job-telemetry");
        var activity = source.StartActivity("span", kind)!;
        new JobTelemetryProcessor().OnStart(activity);
        activity.Stop();
        return activity;
    }

    [Fact]
    public void Outgoing_span_inside_job_is_tagged()
    {
        using (JobTelemetry.Begin("start-list"))
        {
            var span = Started(ActivityKind.Client);
            Assert.Equal("job", span.GetTagItem(RequestSource.Tag));
            Assert.Equal("start-list", span.GetTagItem(JobTelemetry.JobTag));
        }
    }

    [Fact]
    public void Span_outside_job_or_site_request_is_left_alone()
    {
        Assert.Null(Started(ActivityKind.Client).GetTagItem(RequestSource.Tag));
        using (JobTelemetry.Begin("discovery"))
            Assert.Null(Started(ActivityKind.Server).GetTagItem(RequestSource.Tag));
    }

    [Fact]
    public async Task Mark_flows_through_await_and_is_restored()
    {
        Assert.Null(JobTelemetry.Current);
        using (JobTelemetry.Begin("discovery"))
        {
            await Task.Yield();
            Assert.Equal("discovery", JobTelemetry.Current);
            using (JobTelemetry.Begin("start-list"))
                Assert.Equal("start-list", JobTelemetry.Current);
            Assert.Equal("discovery", JobTelemetry.Current);
        }
        Assert.Null(JobTelemetry.Current);
    }
}

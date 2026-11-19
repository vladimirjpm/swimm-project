using System.Diagnostics;
using OpenTelemetry;

namespace Swimm.API.Telemetry;

/// <summary>
/// Метка фоновой задачи на исходящих спанах (docs/plans/prod-auto-pull-plan.md §4.2): HTTP к
/// loglig/isr.org.il и медленные SQL, сделанные внутри прогона расписания, получают
/// <c>swimm.source=job</c> и <c>swimm.job=&lt;имя задачи&gt;</c>. Когда забор пойдёт с прода, на
/// /Admin/AppInsights фон не смешается с посетителями.
///
/// Как: <see cref="Begin"/> кладёт имя задачи в <see cref="AsyncLocal{T}"/> на время работы
/// (<c>ScheduledJob</c>), а <see cref="JobTelemetryProcessor"/> в момент старта спана читает его —
/// спан HttpClient/Npgsql создаётся в том же асинхронном потоке. Ни нового спана, ни заголовка
/// наружу (baggage ушёл бы к loglig в каждом запросе) — только теги на уже существующих.
/// Без телеметрии (разработка, тесты) процессора нет, и <see cref="Begin"/> ничего не стоит.
/// </summary>
internal static class JobTelemetry
{
    internal const string JobTag = "swimm.job";

    private static readonly AsyncLocal<string?> CurrentJob = new();

    /// <summary>Имя задачи, внутри прогона которой идёт код; null — не в фоне.</summary>
    internal static string? Current => CurrentJob.Value;

    /// <summary>Пометить всё, что сделает этот асинхронный поток до Dispose, как работу задачи.</summary>
    internal static IDisposable Begin(string jobName)
    {
        var previous = CurrentJob.Value;
        CurrentJob.Value = jobName;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => CurrentJob.Value = previous;
    }
}

/// <summary>
/// Ставит <see cref="RequestSource.Tag"/> = <see cref="RequestSource.Job"/> и имя задачи на спаны,
/// начатые внутри <see cref="JobTelemetry.Begin"/>. Спаны запросов к сайту (Server) не трогает —
/// их источник решает <see cref="RequestSource.Classify"/>.
/// </summary>
internal sealed class JobTelemetryProcessor : BaseProcessor<Activity>
{
    public override void OnStart(Activity activity)
    {
        if (activity.Kind == ActivityKind.Server || JobTelemetry.Current is not { } job) return;
        activity.SetTag(RequestSource.Tag, RequestSource.Job);
        activity.SetTag(JobTelemetry.JobTag, job);
    }
}

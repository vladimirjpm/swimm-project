using System.Diagnostics;
using Npgsql;
using Swimm.Infrastructure.Data;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Спан запроса Npgsql остаётся, а событие <c>received-first-response</c> — нет: экспортёр Azure
/// Monitor превращал его в лишнюю строку AppTraces на каждый SQL-запрос (проверка на живой рабочей
/// области 17.11, docs/plans/application-insights-plan.md). Если обновление Npgsql вернёт событие,
/// тест это поймает. Нужен локальный Postgres; без него тест молча проходит, как остальные *PgTests.
/// </summary>
[Collection(TelemetryCollection.Name)]
public class NpgsqlTracingPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev;Timeout=3";

    private static async Task<bool> PgAvailable()
    {
        try
        {
            await using var c = new NpgsqlConnection(Conn);
            await c.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task CommandSpan_Kept_FirstResponseEvent_Dropped()
    {
        if (!await PgAvailable()) return;

        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (spans) spans.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);

        var builder = new NpgsqlDataSourceBuilder(Conn);
        NpgsqlTracingDefaults.Apply(builder);
        await using var dataSource = builder.Build();
        await using (var cmd = dataSource.CreateCommand("SELECT 1"))
            await cmd.ExecuteScalarAsync();

        Activity[] select;
        lock (spans) select = spans.Where(a => a.DisplayName.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                                             || (a.GetTagItem("db.query.text") as string)?.Contains("SELECT 1") == true).ToArray();
        var span = Assert.Single(select);
        Assert.DoesNotContain(span.Events, e => e.Name == "received-first-response");
    }
}

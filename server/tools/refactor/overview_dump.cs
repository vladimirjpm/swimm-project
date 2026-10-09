#:project ../../Swimm.Infrastructure/Swimm.Infrastructure.csproj
#:property PublishAot=false
// Эталон ответа обзора соревнования (/api/competition-overview) на локальной базе — до/после
// деления ResultRepository.LoadCompetitionOverviewAsync. Кэш выключен (каждый вызов — в базу).
// Запуск из этой папки: dotnet run overview_dump.cs -- <outDir> <spec> [<spec> …]
//   spec: c57 — competitionId, c57+ — то же с combined, e1 / e1+ — eventId, last — Latest.
// Потом `diff -r before after` должен быть пуст.
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;

var conn = Environment.GetEnvironmentVariable("SWIMM_READ_CONN")
    ?? "Host=localhost;Port=5445;Database=swimm;Username=swimm_ro;Password=swimm_ro_local_dev";
var outDir = args[0];
Directory.CreateDirectory(outDir);
var json = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

foreach (var spec in args.Skip(1))
{
    var combined = spec.EndsWith('+');
    var core = spec.TrimEnd('+');
    var filter = core == "last"
        ? new ResultFilter { Latest = true, Combined = combined }
        : core[0] == 'e'
            ? new ResultFilter { EventId = int.Parse(core[1..]), Combined = combined }
            : new ResultFilter { CompetitionId = int.Parse(core[1..]), Combined = combined };

    // Свежий контекст на каждый вызов — как запрос API.
    await using var db = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
        .UseNpgsql(conn).Options);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var dto = await new ResultRepository(db, new NullCache()).GetCompetitionOverviewAsync(filter);
    File.WriteAllText(Path.Combine(outDir, spec.Replace('+', 'C') + ".json"), JsonSerializer.Serialize(dto, json));
    Console.WriteLine($"{spec}: {sw.ElapsedMilliseconds} ms, days {dto.Days.Count}, records {dto.Records.Count}, hp {dto.HighPointAwards.Count}");
}

sealed class NullCache : ICacheService
{
    public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
    public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
    public Task RemoveAsync(string key) => Task.CompletedTask;
    public Task InvalidateAllAsync() => Task.CompletedTask;
}

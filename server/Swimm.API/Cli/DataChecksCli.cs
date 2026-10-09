using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — реестр проверок данных (/Admin/Health). Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class DataChecksCli
{
    // Прогон реестра проверок данных (то же, что кнопка на /Admin/Health):
    //   dotnet run -- --check-data
    // Читающий: ставит диагноз и пишет находки, ничего не чинит.
    internal static async Task CheckDataAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IDataCheckRunner>();

        // Триггер отдельный от кнопки: в истории видно, откуда прогон (manual / cli / import).
        var run = await runner.RunAllAsync("cli");
        Console.WriteLine($"Прогон #{run.Id}: ошибок {run.ErrorCount}, предупреждений {run.WarningCount}, " +
                          $"инфо {run.InfoCount}, закрыто {run.FixedCount}\n");

        foreach (var g in await runner.GetCurrentAsync())
        {
            var mark = g.OpenCount == 0 ? "✓" : g.Severity == DataCheckSeverity.Error ? "!!" : " !";
            Console.WriteLine($"{mark} {g.Title} — открыто {g.OpenCount}" +
                              (g.AcceptedCount > 0 ? $", принято {g.AcceptedCount}" : "") + $"  [{g.CheckId}]");
            foreach (var f in g.Findings.Where(f => f.Resolution == null).Take(5))
                Console.WriteLine($"      {f.Message}");
        }
        return;
    }

    // Прогон ВСЕГО реестра проверок данных из консоли (пара к кнопке на /Admin/Health):
    //   dotnet run -- --data-checks
    // Нужен, когда админка недоступна (нет сессии, headless-машина) и чтобы увидеть картину
    // целиком после массовой правки данных. Проверки только читают — лечение всегда отдельное.
    internal static async Task DataChecksAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IDataCheckRunner>();
        var run = await runner.RunAllAsync("cli");
        Console.WriteLine($"Прогон #{run.Id}: ошибок {run.ErrorCount}, предупреждений {run.WarningCount}, info {run.InfoCount}");

        foreach (var group in await runner.GetCurrentAsync())
        {
            var total = group.Total ?? group.OpenCount;
            if (total == 0) continue;
            Console.WriteLine($"  [{group.Severity}] {group.CheckId}: {total} — {group.Title}");
            foreach (var finding in group.Findings.Take(5))
                Console.WriteLine($"      {finding.Message}");
        }
        return;
    }
}

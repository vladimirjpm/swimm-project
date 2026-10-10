using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — реестр проверок данных (/Admin/Health). Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class DataChecksCli
{
    // Прогон реестра проверок данных (то же, что кнопка на /Admin/Health):
    //   dotnet run -- --check-data        (псевдоним: --data-checks)
    // Читающий: ставит диагноз и пишет находки, ничего не чинит. Нужен, когда админка недоступна
    // (нет сессии, headless-машина) и чтобы увидеть картину целиком после массовой правки данных.
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

    // Починка английских имён, которые не подходят к ивритским (находки swimmers.en-name-mismatch):
    //   dotnet run -- --fix-en-names [--apply] [--ids 1,2,3] [--report <файл.tsv>]
    // Без --apply — сухой прогон: читает английские карточки loglig (пауза 1 с), печатает решение по
    // каждому пловцу, базу не трогает. С --apply — одна транзакция, аудит swimmer.en-name-fix на
    // каждую строку (было / стало / карточка / причина). Правила — EnglishNameRepairPlanner,
    // заметки и журнал прогонов — docs/plans/en-names-handoff.md.
    internal static async Task FixEnglishNamesAsync(WebApplication app, string[] args)
    {
        var apply = args.Contains("--apply");
        var idsIndex = Array.IndexOf(args, "--ids") + 1;
        int[]? ids = idsIndex > 0 && idsIndex < args.Length
            ? args[idsIndex].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray()
            : null;
        var reportIndex = Array.IndexOf(args, "--report") + 1;
        var reportPath = reportIndex > 0 && reportIndex < args.Length ? args[reportIndex] : null;

        using var scope = app.Services.CreateScope();
        var repair = scope.ServiceProvider.GetRequiredService<IEnglishNameRepairService>();
        Console.WriteLine(apply ? "Починка EN-имён — ЗАПИСЬ…" : "Починка EN-имён — СУХОЙ ПРОГОН (база не меняется)…");
        var report = await repair.RepairAsync(apply, ids);

        foreach (var r in report.Rows)
            Console.WriteLine($"  #{r.SwimmerId} {r.HeName} ({r.BirthYear}): «{r.OldLastEn} | {r.OldFirstEn}» → "
                              + $"{r.Decision.Action} «{r.Decision.LastEn} | {r.Decision.FirstEn}» — {r.Decision.Reason}");
        Console.WriteLine($"\nКандидатов {report.Rows.Count}: записать {report.Written}, очистить {report.Cleared}, "
                          + $"оставить {report.Kept}" + (report.Applied ? $", пропущено (поля менялись) {report.Skipped}" : ""));
        if (!report.Applied && report.Written + report.Cleared > 0)
            Console.WriteLine("Повтори с --apply, чтобы записать. Сделай бэкап базы перед этим.");

        if (reportPath != null)
        {
            var lines = report.Rows.Select(r => string.Join('\t',
                r.SwimmerId, r.HeName, r.BirthYear, r.LogligId, r.OldLastEn, r.OldFirstEn, r.Card,
                r.Decision.Action, r.Decision.LastEn, r.Decision.FirstEn, r.Decision.Reason));
            await File.WriteAllLinesAsync(reportPath,
                lines.Prepend("swimmer_id\the_name\tbirth_year\tloglig_id\told_last_en\told_first_en\tcard\taction\tnew_last_en\tnew_first_en\treason"));
            Console.WriteLine($"Отчёт: {reportPath}");
        }
    }
}

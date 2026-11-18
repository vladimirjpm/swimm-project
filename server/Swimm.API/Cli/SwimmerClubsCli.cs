using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;

namespace Swimm.API.Cli;

/// <summary>Текущий клуб в карточке пловца (Р94, Ф1 docs/plans/swimmer-club-change-plan.md).</summary>
internal static class SwimmerClubsCli
{
    // Пересчитать клуб в карточке у ВСЕХ пловцов по правилу «клуб последнего соревнования»
    // (SwimmerCurrentClubRule). Разовый догон: до 18.11.2026 импорт писал клуб только в пустое
    // поле, и карточки сменивших клуб застряли на старом. Дальше клуб пересчитывает сам импорт.
    //   dotnet run -- --refresh-swimmer-clubs --dry-run          план: пловец, был, стал, дата последнего старта
    //   dotnet run -- --refresh-swimmer-clubs                    записать (журнал Sys_SwimmerClubChanges, source=cli)
    //   … [--limit <N>]                                          сколько строк плана печатать (по умолчанию все)
    // Закреплённых (Swimmers.ClubLocked) не трогает. «⇄» в плане — периоды двух клубов пересекаются:
    // такие смены стоит проверить глазами (часто это дубль клуба, Ф0). Идемпотентно.
    internal static async Task RefreshSwimmerClubsAsync(WebApplication app, string[] args)
    {
        var dryRun = args.Contains("--dry-run");
        var limitIndex = Array.IndexOf(args, "--limit") + 1;
        var limit = limitIndex > 0 && limitIndex < args.Length && int.TryParse(args[limitIndex], out var l) ? l : int.MaxValue;

        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISwimmerClubRefreshService>();
        var report = await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: !dryRun);

        var overlap = report.Changes.Count(c => c.Overlap);
        Console.WriteLine(
            $"Пловцов с личными стартами: {report.Checked}; закреплено: {report.Locked}; ничья в последнем соревновании: {report.Ties}");
        Console.WriteLine(
            $"Смен клуба: {report.Changes.Count} (из них с пересекающимися периодами двух клубов, ⇄: {overlap})");
        Console.WriteLine();
        Console.WriteLine("swimmer\tname\tfrom\tto\tlast swim\toverlap");
        foreach (var c in report.Changes.OrderByDescending(c => c.LastSwimDate).ThenBy(c => c.SwimmerId).Take(limit))
            Console.WriteLine(
                $"{c.SwimmerId}\t{c.SwimmerName}\t{c.FromClubId?.ToString() ?? "—"} {c.FromClubName ?? ""}\t"
                + $"{c.ToClubId} {c.ToClubName}\t{c.LastSwimDate:yyyy-MM-dd}\t{(c.Overlap ? "⇄" : "")}");

        Console.WriteLine();
        Console.WriteLine(dryRun
            ? "Ничего не записано (dry-run). Повторите без --dry-run, чтобы записать."
            : $"Записано смен: {report.Changes.Count} (журнал Sys_SwimmerClubChanges, source=cli).");
    }
}

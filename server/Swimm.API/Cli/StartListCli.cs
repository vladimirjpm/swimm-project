using Swimm.Application.Abstractions;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — стартовые протоколы. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class StartListCli
{
    // Затянуть СТАРТОВЫЙ протокол соревнования (docs/plans/start-list-plan.md, шаг С4):
    //   dotnet run -- --pull-start-list <orgCompId>
    // orgCompId — compID на isr.org.il. Соревнование должно быть во «Входящих» и иметь loglig-id
    // (иначе вернётся статус empty — см. §1.6 плана, риск №1). Идёт по страницам заплывов,
    // у чемпионата это под две сотни запросов с вежливой паузой; повтор идемпотентен.
    internal static async Task PullStartListAsync(WebApplication app, string[] args)
    {
        var idIndex = Array.IndexOf(args, "--pull-start-list") + 1;
        if (idIndex >= args.Length || !int.TryParse(args[idIndex], out var pullOrgCompId))
        {
            Console.Error.WriteLine("Использование: --pull-start-list <orgCompId>");
            Environment.ExitCode = 1;
            return;
        }

        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IStartListPullService>();
        var report = await svc.PullAsync(pullOrgCompId);

        Console.WriteLine(
            $"Стартовый протокол {report.OrgCompId} (loglig {report.LogligId?.ToString() ?? "—"}): {report.Status}");
        if (report.Error is not null) Console.WriteLine($"  {report.Error}");
        Console.WriteLine(
            $"  заплывов {report.EventsFetched}/{report.Events}, строк {report.Entries}; " +
            $"новых {report.Added}, пересев {report.Moved}, снялись {report.Removed}, без изменений {report.Unchanged}");
        Console.WriteLine(
            $"  пловцов заведено {report.SwimmersCreated}, loglig-id проставлено {report.SwimmersStamped}, " +
            $"клуб не опознан у строк: {report.ClubsUnmatched}");
        return;
    }

    // Сшить заявки стартового протокола с результатами (шаг С9):
    //   dotnet run -- --stitch-start-list <orgCompId>
    // На импорте это делается само; прогон нужен для соревнований, чьи протоколы загрузили
    // раньше, чем появился стартовый. Идемпотентно.
    internal static async Task StitchStartListAsync(WebApplication app, string[] args)
    {
        var idIndex = Array.IndexOf(args, "--stitch-start-list") + 1;
        if (idIndex >= args.Length || !int.TryParse(args[idIndex], out var stitchOrgCompId))
        {
            Console.Error.WriteLine("Использование: --stitch-start-list <orgCompId>");
            Environment.ExitCode = 1;
            return;
        }

        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IStartListStitchService>();
        var report = await svc.StitchAsync(stitchOrgCompId);

        Console.WriteLine(
            $"Сшивка {report.OrgCompId}: дней {report.Days}, заявок {report.Entries}, " +
            $"привязано к дню {report.Linked}, проплыли {report.Swum}, без результата {report.NoShow}");
        if (report.MatchedByDiscipline > 0)
            Console.WriteLine($"  сматчено по дисциплине, а не по дорожке: {report.MatchedByDiscipline}");
        if (report.Unlinked > 0)
            Console.WriteLine($"  не удалось отнести ни к одному дню: {report.Unlinked}");
        return;
    }
}

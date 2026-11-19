using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.API.Cli;

/// <summary>Текущий клуб в карточке пловца (Р95 + Р98, docs/plans/swimmer-club-change-plan.md §2 Ф1, §6).</summary>
internal static class SwimmerClubsCli
{
    // Пересчитать клуб в карточке у ВСЕХ пловцов по правилу «клуб последнего соревнования»
    // (SwimmerCurrentClubRule) и сверить смены с loglig (Р98: «если клуб поменялся — сверяйся с
    // логлиг; если там не поменялся — пока не меняй»).
    //   dotnet run -- --refresh-swimmer-clubs --dry-run          план + вердикт loglig по каждому; в loglig
    //                                                            ХОДИТ (~1 запрос/с), в базу НЕ пишет
    //   dotnet run -- --refresh-swimmer-clubs                    завести кандидатов (Sys_SwimmerClubCandidates)
    //                                                            и сверить всех ждущих: подтверждённые loglig
    //                                                            записать (журнал, source=loglig), прочие ждут
    //   … [--swimmers <id,id,…>]                                 только эти пловцы
    //   … [--take <N>]                                           сверить с loglig только первые N смен плана
    //                                                            (dry-run; новые по дате старта — сверху)
    //   … [--limit <N>]                                          сколько строк плана печатать (по умолчанию все)
    // Закреплённых (Swimmers.ClubLocked) не трогает. «⇄» — периоды двух клубов пересекаются (часто дубль
    // клуба, Ф0). Идемпотентно: ответ loglig моложе суток берётся из кэша кандидата.
    // Р109: пловцам с мастерс-стартами ставит Swimmers.ClubFromMasters (в dry-run — только считает); у них
    // кандидат — клуб последнего МАСТЕРС-соревнования, колонка «M» в плане.
    internal static async Task RefreshSwimmerClubsAsync(WebApplication app, string[] args)
    {
        var dryRun = args.Contains("--dry-run");
        var limit = IntArg(args, "--limit") ?? int.MaxValue;
        var take = IntArg(args, "--take") ?? int.MaxValue;
        var swimmers = StringArg(args, "--swimmers")?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0).Where(id => id > 0).ToList();

        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISwimmerClubRefreshService>();
        var check = scope.ServiceProvider.GetRequiredService<ISwimmerClubLogligCheckService>();
        var report = await svc.RefreshAsync(swimmers, SwimmerClubChangeSources.Cli, apply: !dryRun);

        var overlap = report.Changes.Count(c => c.Overlap);
        Console.WriteLine(
            $"Пловцов с личными стартами: {report.Checked}; закреплено: {report.Locked}; ничья в последнем соревновании: {report.Ties}");
        Console.WriteLine(
            $"Кандидатов на смену клуба: {report.Changes.Count} (из них с пересекающимися периодами двух клубов, ⇄: {overlap}); "
            + $"{(dryRun ? "снялось бы" : "снято")} ждущих: {report.Withdrawn}");
        Console.WriteLine(
            $"Клуб по мастерс-стартам (Р109): у {report.FromMasters}; флаг {(dryRun ? "поставился бы" : "поставлен")} сейчас: {report.MastersFlagged}");

        var plan = report.Changes.OrderByDescending(c => c.LastSwimDate).ThenBy(c => c.SwimmerId).ToList();
        SwimmerClubCheckReport loglig;
        if (dryRun)
        {
            var toCheck = plan.Take(take).ToList();
            Console.WriteLine($"Сверяю с loglig: {toCheck.Count} (≈ {toCheck.Count} с, ответ моложе суток — из кэша)…");
            loglig = await check.PreviewAsync(toCheck, (done, total) =>
            {
                if (done % 25 == 0 || done == total) Console.WriteLine($"  … {done}/{total}");
            });
        }
        else
        {
            Console.WriteLine("Сверяю всех ждущих кандидатов с loglig (~1 запрос/с, ответ моложе суток — из кэша)…");
            loglig = await check.CheckPendingAsync(SwimmerClubCheckScope.AllPending);
        }

        var verdicts = loglig.Verdicts.ToDictionary(v => v.SwimmerId);
        Console.WriteLine();
        Console.WriteLine("swimmer\tname\tfrom\tto\tlast swim\toverlap\tM\tloglig\tloglig club");
        foreach (var c in plan.Take(limit))
        {
            var v = verdicts.GetValueOrDefault(c.SwimmerId);
            Console.WriteLine(
                $"{c.SwimmerId}\t{c.SwimmerName}\t{c.FromClubId?.ToString() ?? "—"} {c.FromClubName ?? ""}\t"
                + $"{c.ToClubId} {c.ToClubName}\t{c.LastSwimDate:yyyy-MM-dd}\t{(c.Overlap ? "⇄" : "")}\t{(c.FromMasters ? "M" : "")}\t"
                + $"{v?.Verdict ?? "—"}{(v?.Applied == true ? " ✓" : "")}\t{v?.LogligClubName ?? ""}");
        }

        Console.WriteLine();
        Console.WriteLine($"Вердикты loglig ({loglig.Checked}; запросов {loglig.Requests}, из кэша {loglig.FromCache}):");
        foreach (var (verdict, count) in loglig.ByVerdict.OrderByDescending(v => v.Value))
            Console.WriteLine($"  {verdict,-14}{count,5}  {VerdictText(verdict)}");
        Console.WriteLine();
        Console.WriteLine(dryRun
            ? "Ничего не записано (dry-run). Без --dry-run: кандидаты в Sys_SwimmerClubCandidates, подтверждённые loglig — в карточку."
            : $"Записано смен (подтверждено loglig): {loglig.Applied} (журнал Sys_SwimmerClubChanges, source=loglig). "
              + "Остальные ждут — дашборд, блок «Club changes».");
    }

    private static string VerdictText(string verdict) => verdict switch
    {
        SwimmerClubLogligVerdicts.Confirmed => "loglig = новый клуб → меняем",
        SwimmerClubLogligVerdicts.StillOld => "loglig = клуб карточки → ждём",
        SwimmerClubLogligVerdicts.Archive => "«ארכיון איגוד» → ждём, решает человек",
        SwimmerClubLogligVerdicts.OtherClub => "третий клуб → ждём, решает человек",
        SwimmerClubLogligVerdicts.NoLogligId => "нет подтверждённого loglig-id → ждём",
        SwimmerClubLogligVerdicts.Unavailable => "карточка не открылась → повтор позже",
        _ => "",
    };

    private static int? IntArg(string[] args, string name) =>
        StringArg(args, name) is { } s && int.TryParse(s, out var v) ? v : null;

    private static string? StringArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name) + 1;
        return i > 0 && i < args.Length ? args[i] : null;
    }
}

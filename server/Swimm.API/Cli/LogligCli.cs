using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using System.Globalization;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — пособытийный источник loglig: привязка пловцов, промежуточные, эстафеты, очки. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class LogligCli
{
    // Штамповка loglig-id пловцам по протоколам:
    //   dotnet run -- --stamp-loglig-ids
    // На импорте это делается само (настройка LogligStampOnImport); прогон нужен для стартов,
    // импортированных раньше. Идёт по страницам заплывов на loglig — небыстро; соревнования,
    // где все пловцы уже привязаны, пропускаются без обращения к сайту.
    internal static async Task StampLogligIdsAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILogligStampService>();
        var report = await svc.BackfillAsync();

        foreach (var line in report.Lines) Console.WriteLine(line);
        Console.WriteLine(
            $"\nИтог: соревнований {report.Competitions}, привязано {report.Stamped}, " +
            $"не нашлось {report.NotFound}, пропущено {report.Skipped}");
        return;
    }

    // Доклейка промежуточных БЕЗ переимпорта — основной путь получать промежуточные
    // (решение Влада 20.09.2026, docs/plans/splits-attach-without-repull-plan.md):
    //   dotnet run -- --attach-splits <competitionId> [--loglig <id>] [--dry-run]
    //
    // Пишет ровно два поля (RelayMembers.SplitTime, Results.TimeSplit) к строкам, которые уже в
    // базе. Переимпорт ради промежуточных при разъехавшемся ключе upsert заводит второй комплект
    // соревнования (1581: 3561 строка, И-28) — здесь этого не может быть по устройству.
    // Прогон идёт по ВСЕМ дням события, какой бы день ни передали.
    internal static async Task AttachSplitsAsync(WebApplication app, string[] args)
    {
        var idIndex = Array.IndexOf(args, "--attach-splits") + 1;
        if (idIndex >= args.Length || !int.TryParse(args[idIndex], out var competitionId))
        {
            Console.Error.WriteLine(
                "Usage: dotnet run -- --attach-splits <competitionId> [--loglig <id>] [--dry-run]");
            Environment.Exit(1);
            return;
        }

        // LogligId нужен только тем стартам, у которых нет строки discovery (склейки из
        // окружных, старый импорт из JSON) — остальным он находится сам.
        int? logligOverride = null;
        var logligIndex = Array.IndexOf(args, "--loglig") + 1;
        if (logligIndex > 0 && logligIndex < args.Length && int.TryParse(args[logligIndex], out var lg))
            logligOverride = lg;

        var apply = !args.Contains("--dry-run");
        using var scope = app.Services.CreateScope();
        var attach = scope.ServiceProvider.GetRequiredService<ISplitAttachService>();

        Console.WriteLine(apply
            ? $"Доклейка промежуточных соревнованию {competitionId}…"
            : $"Доклейка промежуточных соревнованию {competitionId} — СУХОЙ ПРОГОН…");
        var outcome = await attach.AttachAsync(competitionId, logligOverride, apply);

        Console.WriteLine($"\n{outcome.Message}");
        // Разбивка по дням — единственный способ увидеть, что день многодневки остался пустым.
        foreach (var day in outcome.Days)
            Console.WriteLine($"  #{day.CompetitionId} {day.Date} «{day.Name}»: "
                + $"ног {day.LegWrites}, личных {day.SwimWrites}");
        if (!apply && (outcome.Report.LegWrites > 0 || outcome.Report.SwimWrites > 0))
            Console.WriteLine("\nПовтори без --dry-run, чтобы записать.");
        return;
    }

    // Чистка невозможных этапов эстафет, записанных до сторожа RelayLegFloor (решение Влада
    // 10.10.2026, docs/relays.md «Невозможный этап»):
    //   dotnet run -- --clear-implausible-legs [--apply] [--report <файл.tsv>]
    // Без --apply — сухой прогон. С --apply — одна транзакция, аудит relay.splits-clear на команду.
    // Доклейка (--attach-splits) судит этапы тем же правилом и сама их уже не пишет.
    internal static async Task ClearImplausibleLegsAsync(WebApplication app, string[] args)
    {
        var apply = args.Contains("--apply");
        var reportIndex = Array.IndexOf(args, "--report") + 1;
        var reportPath = reportIndex > 0 && reportIndex < args.Length ? args[reportIndex] : null;

        using var scope = app.Services.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IRelayLegCleanupService>();
        Console.WriteLine(apply
            ? "Невозможные этапы эстафет — ЗАПИСЬ…"
            : "Невозможные этапы эстафет — СУХОЙ ПРОГОН (база не меняется)…");
        var report = await cleanup.RunAsync(apply);

        foreach (var r in report.Rows)
            Console.WriteLine($"  relay #{r.RelayId} comp {r.CompetitionId} {r.Distance} {r.Style} итог {r.Time}: "
                              + $"{r.Reason} | этапы {string.Join(" / ", r.OldSplits.Select(s => s ?? "—"))}");
        Console.WriteLine($"\nЭстафет с этапами {report.RelaysChecked}; невозможный этап у {report.Rows.Count}, "
                          + $"ног {(report.Applied ? "обнулено" : "к обнулению")} {report.LegsCleared}");
        if (!report.Applied && report.LegsCleared > 0)
            Console.WriteLine("Повтори с --apply, чтобы записать. Сделай бэкап базы перед этим.");

        if (reportPath != null)
        {
            await File.WriteAllLinesAsync(reportPath, report.Rows
                .Select(r => string.Join('\t', r.RelayId, r.CompetitionId, r.Distance, r.Style, r.Time, r.Reason,
                    string.Join(";", r.OldSplits.Select(s => s ?? ""))))
                .Prepend("relay_id\tcompetition_id\tdistance\tstyle\ttime\treason\told_splits"));
            Console.WriteLine($"Отчёт: {reportPath}");
        }
    }

    // Разведка пособытийного источника loglig (шаг 3 «склеенных сессий», data-integrity §10):
    //   dotnet run -- --pull-events <discoveredId> [--import]
    // Только читает и отчитывается: сколько событий, строк, по каким раундам, какие имена не
    // сопоставились с пловцами в БД и сколько официальных клубных очков насчитал организатор.
    // Ходит в чужой прод с паузой 2 с между запросами — на большом чемпионате это минуты.
    internal static async Task PullEventsAsync(WebApplication app, string[] args)
    {
        var peIndex = Array.IndexOf(args, "--pull-events") + 1;
        if (peIndex >= args.Length || !int.TryParse(args[peIndex], out var discoveredId))
        {
            Console.Error.WriteLine("Usage: dotnet run -- --pull-events <discoveredId>");
            Environment.Exit(1);
            return;
        }

        using var scope = app.Services.CreateScope();
        var pull = scope.ServiceProvider.GetRequiredService<ILogligEventPullService>();

        // Без --import это чистая разведка. С ним — переимпорт поверх существующего:
        // upsert с удалением исчезнувших (ключ сменился из-за Round) и сохранением эстафет.
        var doImport = args.Contains("--import");
        var summary = string.Empty;
        LogligPullReport report;
        if (doImport)
            (report, summary) = await pull.ImportAsync(discoveredId);
        else
            report = await pull.DryRunAsync(discoveredId);

        Console.WriteLine($"«{report.CompetitionName}»: событий {report.Events}, личных строк {report.IndividualRows}");
        Console.WriteLine($"  эстафетных событий пропущено: {report.RelayEvents} (состав команд страница не печатает)");
        foreach (var (round, count) in report.RowsByRound.OrderByDescending(r => r.Value))
            Console.WriteLine($"  раунд {round}: {count}");

        Console.WriteLine($"  официальные клубные очки: сумма {report.OfficialClubPoints.Values.Sum()}");
        foreach (var (club, points) in report.OfficialClubPoints.OrderByDescending(c => c.Value).Take(5))
            Console.WriteLine($"      {points,6}  {club}");

        Console.WriteLine($"  имена без пары в БД: {report.UnresolvedNames.Count}");
        foreach (var name in report.UnresolvedNames.Take(10))
            Console.WriteLine($"      {name}");

        if (doImport)
        {
            Console.WriteLine($"ИМПОРТ: {summary}");
            Console.WriteLine("Не забудь пересчитать зачёт: dotnet run -- --rebuild-club-standings");
        }
        return;
    }

    // Ремонт зачётных полос эстафет по пособытийному источнику (data-integrity §10):
    //   dotnet run -- --pull-relays <discoveredId> [--apply]
    // PDF печатает эстафеты сквозной дисциплиной (пол «none», места через все полосы), а сайт
    // держит их полосными событиями. Прогон правит ТОЛЬКО пол, полосу и место; состав, ноги и
    // RelayId остаются как есть. Без --apply — dry-run. Ходит в чужой прод с паузой 2 с.
    internal static async Task PullRelaysAsync(WebApplication app, string[] args)
    {
        var prIndex = Array.IndexOf(args, "--pull-relays") + 1;
        if (prIndex >= args.Length || !int.TryParse(args[prIndex], out var relayDiscoveredId))
        {
            Console.Error.WriteLine("Usage: dotnet run -- --pull-relays <discoveredId> [--apply]");
            Environment.Exit(1);
            return;
        }

        using var scope = app.Services.CreateScope();
        var relays = scope.ServiceProvider.GetRequiredService<ILogligRelayBandService>();
        var relayReport = await relays.RepairAsync(relayDiscoveredId, args.Contains("--apply"));

        Console.WriteLine($"«{relayReport.CompetitionName}»: эстафетных событий {relayReport.RelayEvents}, " +
                          $"строк у источника {relayReport.SourceRows}, у нас {relayReport.DbRows}");
        Console.WriteLine($"  клубные очки эстафет: было {relayReport.PointsBefore}, " +
                          $"станет {relayReport.PointsAfter}, официально {relayReport.OfficialPoints}");
        if (relayReport.UnreadableEvents is { Count: > 0 } unreadableEvents)
            Console.WriteLine($"  loglig не отдал события (пропущены): {string.Join(", ", unreadableEvents)}");

        if (relayReport.Plan.Problems.Count > 0)
        {
            Console.WriteLine($"  НЕПРИМЕНИМО, расхождений {relayReport.Plan.Problems.Count}:");
            foreach (var problem in relayReport.Plan.Problems.Take(20))
                Console.WriteLine($"      {problem}");
            Console.WriteLine("  План не применяется целиком: полосы разложены наполовину дали бы кривые места.");
            return;
        }

        var relayChanges = relayReport.Plan.Changes.Where(c => c.HasChanges).ToList();
        Console.WriteLine($"  строк к правке: {relayChanges.Count} из {relayReport.Plan.Changes.Count}");
        foreach (var band in relayChanges.GroupBy(c => $"{c.StyleName} {c.Distance} · {c.GenderAfter} {c.BandAfter}"))
            Console.WriteLine($"      {band.Key}: {band.Count()}");
        foreach (var change in relayChanges.Take(5))
            Console.WriteLine($"      #{change.ResultId} {change.Club}: " +
                              $"{change.GenderBefore}/{change.BandBefore}/место {change.PositionBefore} → " +
                              $"{change.GenderAfter}/{change.BandAfter}/место {change.PositionAfter}");

        Console.WriteLine(relayReport.Applied > 0
            ? $"ПРИМЕНЕНО: обновлено строк {relayReport.Applied}, зачёт соревнования пересчитан"
            : "Ничего не записано (dry-run). Повторите с --apply.");
        return;
    }

    // Построчный разбор расхождения с официальными клубными очками (data-integrity §10):
    //   dotnet run -- --points-diff <competitionId>
    // Проверка реестра показывает величину долга («наши X, официальные Y»), а этот прогон —
    // из чего он состоит: отделяет наши ошибки от дефектов источника. Только читает.
    internal static async Task PointsDiffAsync(WebApplication app, string[] args)
    {
        var pdIndex = Array.IndexOf(args, "--points-diff") + 1;
        if (pdIndex >= args.Length || !int.TryParse(args[pdIndex], out var diffCompetitionId))
        {
            Console.Error.WriteLine("Usage: dotnet run -- --points-diff <competitionId>");
            Environment.Exit(1);
            return;
        }

        using var scope = app.Services.CreateScope();
        var diffDb = scope.ServiceProvider.GetRequiredService<Swimm.Infrastructure.Data.SwimmDbContext>();

        var comp = await diffDb.Competitions.AsNoTracking()
            .Where(c => c.Id == diffCompetitionId)
            .Select(c => new { c.Id, c.Name, c.Date, c.IsMasters, c.PointRuleClubsId })
            .FirstOrDefaultAsync();
        if (comp is null)
        {
            Console.Error.WriteLine($"Соревнование #{diffCompetitionId} не найдено");
            Environment.Exit(1);
            return;
        }

        var diffRules = await diffDb.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync();
        var diffRule = Swimm.Infrastructure.Services.CompetitionRuleResolver.Resolve(
            diffRules, comp.PointRuleClubsId, comp.IsMasters,
            DateOnly.TryParseExact(comp.Date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedDate) ? parsedDate : DateOnly.MinValue);

        var diffRaw = await diffDb.Results.AsNoTracking()
            .Where(r => r.CompetitionId == diffCompetitionId && r.OfficialClubPoints != null)
            .Select(r => new
            {
                r.Id,
                Style = r.Style!.Name,
                r.Distance,
                r.Gender,
                r.EventStyleAge,
                r.Round,
                r.HeatType,
                r.Position,
                r.Heat,
                r.TimeMillisecond,
                r.TimeFail,
                IsRelay = r.RelayId != null,
                Official = r.OfficialClubPoints!.Value
            })
            .ToListAsync();

        var diffRows = diffRaw.Select(r =>
        {
            // Ровно как на витрине: предварительный заплыв (Р34) и общий финал «כללי» (Р43)
            // места не дают. Гашение — это НАШЕ правило, и разбор обязан его учитывать.
            var suppressed = r.HeatType == "prelim" || r.HeatType == "extra"
                          || r.Round == Swimm.Domain.ResultRounds.FinalOpen;
            var ours = Swimm.Infrastructure.Services.PointRulesClubsScoring.RelayPointsFor(
                diffRule, suppressed ? null : r.Position, r.TimeFail, r.IsRelay);
            return new OfficialPointsRow(
                r.Id,
                SectionKey: $"{r.Style}|{r.Distance}|{r.Gender}|{r.EventStyleAge}|{r.Round}",
                Label: $"{r.Distance} {r.Style} · {r.Gender} {r.EventStyleAge} · {r.Round ?? "—"}",
                r.Position, r.Heat, r.TimeMillisecond, suppressed, ours, r.Official);
        }).ToList();

        var diff = OfficialPointsDiffAnalyzer.Analyze(
            diffRows,
            place => Swimm.Infrastructure.Services.PointRulesClubsScoring.PointsFor(diffRule, place, false));

        Console.WriteLine($"«{comp.Name}» ({comp.Date}), правило #{diffRule?.Id.ToString() ?? "нет"}");
        Console.WriteLine($"  строк с эталоном {diff.Rows}: наши {diff.Ours}, официальные {diff.Official}, " +
                          $"расхождение {diff.Ours - diff.Official}, строк с расхождением {diff.Mismatched}");
        foreach (var group in diff.Groups)
        {
            Console.WriteLine($"\n  [{group.Kind}] строк {group.Rows}, очков {group.Diff:+#;-#;0}");
            Console.WriteLine($"      {group.Explanation}");
            foreach (var example in group.Examples) Console.WriteLine($"      · {example}");
        }
        return;
    }
}

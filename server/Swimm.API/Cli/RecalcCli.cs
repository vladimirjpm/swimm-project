using Swimm.Application.Abstractions;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — пересчёты и разовые бэкфиллы по всей базе. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class RecalcCli
{
    // Пересчёт объединённых мест «Combine All Results» во всех соревнованиях с этим флагом:
    //   dotnet run -- --recalc-combined
    // Нужен разово после миграции (бэкфилл) и как аварийная кнопка, если материализованные
    // значения разошлись с результатами.
    internal static async Task RecalcCombinedAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICompetitionRecalculationService>();
        var updated = await svc.RecalculateAllCombinedAsync();
        Console.WriteLine($"Combine All Results: пересчитано строк — {updated}");
        return;
    }

    // Флаг пара-очков (Р67) во всех соревнованиях:
    //   dotnet run -- --para-points-scan
    // Разово после миграции AddResultIsParaPoints; штатно флаг ставит пересчёт соревнования.
    internal static async Task ParaPointsScanAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICompetitionRecalculationService>();
        var changed = await svc.RecalculateAllParaPointsAsync();
        Console.WriteLine($"Пара-очки: изменено строк — {changed}");
        return;
    }

    // Пересчёт материализованного клубного зачёта во всех соревнованиях:
    //   dotnet run -- --rebuild-club-standings
    // Нужен разово после миграции (бэкфилл истории) и как аварийная кнопка, если зачёты
    // разошлись с результатами. Штатно таблица поддерживается на шве пересчёта соревнования.
    internal static async Task RebuildClubStandingsAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IClubStandingService>();
        var rows = await svc.RebuildAllAsync();
        Console.WriteLine($"Клубный зачёт: строк в таблице — {rows}");
        return;
    }

    // Пересобрать составы ВСЕХ групп, подписанных на клуб (docs/plans/hubgroup-club-subscription-plan.md П2):
    //   dotnet run -- --hubgroup-club-sync
    // Штатно состав пересобирается сам — при подписке, после импорта (по клубам импорта) и после
    // склейки клубов. Этот прогон — догнать, если пересборка после импорта упала, и раз в сезон:
    // окно «пловцы клуба» (текущий + прошлый сезон) сдвигается 1 сентября без всякого импорта.
    internal static async Task HubgroupClubSyncAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IHubGroupClubSubscriptionService>();
        var sync = await svc.SyncAllAsync();
        Console.WriteLine($"Группы с подпиской на клуб: пересобрано {sync.Groups}, пловцов добавлено {sync.Added}, убрано {sync.Removed}");
        return;
    }

    // Прогон «Проверить качество» по событию без админки (пара к --repull: после переимпорта
    // автопометки сброшены, и вернуть актуальные может только скан):
    //   dotnet run -- --quality-scan <eventId>
    //   dotnet run -- --quality-scan <competitionId> --competition   (однодневка без события)
    internal static async Task QualityScanAsync(WebApplication app, string[] args)
    {
        var qsIndex = Array.IndexOf(args, "--quality-scan") + 1;
        if (qsIndex >= args.Length || !int.TryParse(args[qsIndex], out var qsEventId))
        {
            Console.Error.WriteLine("Usage: dotnet run -- --quality-scan <eventId> | --quality-scan <competitionId> --competition");
            Environment.Exit(1);
            return;
        }

        using var scope = app.Services.CreateScope();
        var suspects = scope.ServiceProvider.GetRequiredService<ISuspectResultService>();
        var scan = args.Contains("--competition")
            ? await suspects.ScanAsync(null, qsEventId)
            : await suspects.ScanAsync(qsEventId, null);
        Console.WriteLine($"Просмотрено {scan.Scanned}, помечено {scan.Flagged}, снято {scan.Cleared}, ручных сохранено {scan.ManualKept}");
        foreach (var g in scan.Rows.GroupBy(r => r.Reason))
            Console.WriteLine($"  {g.Key}: {g.Count()}");
        return;
    }

    // Разовый бэкфилл структурного состава эстафет (RelayMembers) для данных,
    // импортированных до появления структурных ног:
    //   dotnet run -- --backfill-relay-members [--apply]
    // Без --apply — dry-run: печатает отчёт, БД не меняется. Идемпотентно.
    internal static async Task BackfillRelayMembersAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IRelayMemberBackfillService>();
        var rep = await backfill.BackfillAsync(apply: args.Contains("--apply"));
        Console.WriteLine(rep.Applied
            ? "=== ПРИМЕНЕНО: RelayMembers бэкфилл ==="
            : "=== DRY-RUN: RelayMembers бэкфилл, БД не изменена (добавь --apply) ===");
        Console.WriteLine($"Эстафет всего (без состава): {rep.RelaysTotal}");
        Console.WriteLine($"  уже с составом (пропущены):  {rep.RelaysAlreadyPopulated}");
        Console.WriteLine($"  залинковано (>=1 нога):      {rep.RelaysLinked}");
        Console.WriteLine($"Ног привязано:  {rep.LegsLinked}");
        Console.WriteLine($"Ног не сопоставлено: {rep.LegsUnmatched}");
        foreach (var s in rep.UnmatchedSamples) Console.WriteLine($"    ? {s}");
        return;
    }

    // Разовый бэкфилл Competition.OrgCompId по Discovery-строкам (для соревнований, импортированных
    // до того, как импорт научился штамповать OrgCompId; кросс-линк Competitions↔Discovery для них пуст):
    //   dotnet run -- --backfill-discovery-orgcompid [--apply]
    // Без --apply — dry-run: печатает mapping-таблицу, БД не меняется. Идемпотентно.
    internal static async Task BackfillDiscoveryOrgcompidAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
        var apply = args.Contains("--apply");
        var rows = await discovery.BackfillImportedOrgCompIdsAsync(apply: apply);

        Console.WriteLine(apply
            ? "=== ПРИМЕНЕНО ==="
            : "=== DRY-RUN: бэкфилл Discovery→OrgCompId, БД не изменена (добавь --apply) ===");
        foreach (var r in rows)
        {
            Console.WriteLine(
                $"[{r.Action}] compID {r.OrgCompId} → comp #{r.CompetitionId} «{r.CompetitionName}»  (discovered: «{r.DiscoveredName}»)");
        }

        var wouldLink = rows.Count(r => r.Action is "WouldLink");
        var linked = rows.Count(r => r.Action is "Linked");
        var already = rows.Count(r => r.Action is "AlreadyLinked");
        var takenByOther = rows.Count(r => r.Action is "TakenByOther");
        Console.WriteLine($"Итого: would-link {wouldLink}, linked {linked}, already {already}, taken-by-other {takenByOther}");
        return;
    }
}

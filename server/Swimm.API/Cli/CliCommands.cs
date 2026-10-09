namespace Swimm.API.Cli;

/// <summary>
/// Консольные команды API (<c>dotnet run --project server/Swimm.API -- --&lt;команда&gt;</c>): выполнить
/// одну и выйти, веб-сервер не поднимается. Раньше 35 блоков <c>if (args.Contains(…))</c> жили
/// прямо в <c>Program.cs</c> (1800 строк); перенесены по темам в <c>Cli/*Cli.cs</c> без изменений.
/// Описание и синтаксис каждой — комментарий над её методом.
///
/// ⚠ Порядок списка = прежний порядок в <c>Program.cs</c>: при двух флагах срабатывает первый.
/// Новая команда — метод в файле своей темы + строка сюда.
/// </summary>
internal static class CliCommands
{
    private static readonly (Func<string[], bool> Matches, Func<WebApplication, string[], Task> Run)[] Commands =
    [
        (args => args.Contains("--migrate"), (app, args) => { DatabaseCli.Migrate(app, args); return Task.CompletedTask; }),
        (args => args.Contains("--grant-admin"), DatabaseCli.GrantAdminAsync),
        (args => args.Contains("--recalc-combined"), RecalcCli.RecalcCombinedAsync),
        (args => args.Contains("--para-points-scan"), RecalcCli.ParaPointsScanAsync),
        (args => args.Contains("--rebuild-club-standings"), RecalcCli.RebuildClubStandingsAsync),
        (args => args.Contains("--hubgroup-club-sync"), RecalcCli.HubgroupClubSyncAsync),
        (args => args.Contains("--probe-club-standings"), DiscoveryCli.ProbeClubStandingsAsync),
        (args => args.Contains("--refresh-upcoming"), DiscoveryCli.RefreshUpcomingAsync),
        (args => args.Contains("--pull-start-list"), StartListCli.PullStartListAsync),
        (args => args.Contains("--stitch-start-list"), StartListCli.StitchStartListAsync),
        (args => args.Contains("--stamp-loglig-ids"), LogligCli.StampLogligIdsAsync),
        (args => args.Contains("--verify-records"), RecordsCli.VerifyRecordsAsync),
        (args => args.Contains("--seed-records"), RecordsCli.SeedRecordsAsync),
        (args => args.Contains("--seed-dolphin-training"), SeedCli.SeedDolphinTrainingAsync),
        (args => args.Contains("--seed-personas"), SeedCli.SeedPersonasAsync),
        (args => args.Contains("--audit-imports"), DiscoveryCli.AuditImportsAsync),
        (args => args.Contains("--check-data"), DataChecksCli.CheckDataAsync),
        (args => args.Contains("--merge-same-name-clubs"), MergeCli.MergeSameNameClubsAsync),
        (args => args.Contains("--delete-empty-clubs"), MergeCli.DeleteEmptyClubsAsync),
        (args => args.Contains("--attach-splits"), LogligCli.AttachSplitsAsync),
        (args => args.Contains("--split-days"), MultidayCli.SplitDaysAsync),
        (args => args.Contains("--join-days"), MultidayCli.JoinDaysAsync),
        (args => args.Contains("--collapsed-days"), MultidayCli.CollapsedDaysAsync),
        (args => args.Contains("--repull"), DiscoveryCli.RepullAsync),
        (args => args.Contains("--records-refresh") || args.Contains("--records-check"), RecordsCli.RecordsRefreshAsync),
        (args => args.Contains("--records-dump"), RecordsCli.RecordsDumpAsync),
        (args => args.Contains("--quality-scan"), RecalcCli.QualityScanAsync),
        (args => args.Contains("--pull-events"), LogligCli.PullEventsAsync),
        (args => args.Contains("--pull-relays"), LogligCli.PullRelaysAsync),
        (args => args.Contains("--points-diff"), LogligCli.PointsDiffAsync),
        (args => args.Contains("--data-checks"), DataChecksCli.DataChecksAsync),
        (args => args.Contains("--discovery-sync"), DiscoveryCli.DiscoverySyncAsync),
        (args => args.Contains("--merge-swimmers"), MergeCli.MergeSwimmersAsync),
        (args => args.Contains("--backfill-relay-members"), RecalcCli.BackfillRelayMembersAsync),
        (args => args.Contains("--backfill-discovery-orgcompid"), RecalcCli.BackfillDiscoveryOrgcompidAsync),
    ];

    /// <summary>Выполнить консольную команду из <paramref name="args"/>; false — команды нет, поднимаем веб.</summary>
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        foreach (var (matches, run) in Commands)
        {
            if (!matches(args)) continue;
            await run(app, args);
            return true;
        }
        return false;
    }
}

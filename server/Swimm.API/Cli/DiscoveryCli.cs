using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — входящие isr.org.il: синхронизация, детали, перезалив, ретро-сверка. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class DiscoveryCli
{
    // Проставить «есть ли ОФИЦИАЛЬНЫЙ клубный зачёт (דירוג מועדונים)» уже импортированным:
    //   dotnet run -- --probe-club-standings [--force]
    // Затягивание новых соревнований проставляет флаг само; этот прогон — для тех, кто попал в
    // базу раньше. --force перепроверяет и уже помеченные (зачёт публикуют не сразу).
    internal static async Task ProbeClubStandingsAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IOfficialClubStandingService>();
        var report = await svc.BackfillAsync(args.Contains("--force"));

        foreach (var line in report.Lines) Console.WriteLine(line);
        Console.WriteLine(
            $"\nИтог: проверено {report.Checked}, с официальным зачётом {report.WithStanding}, " +
            $"без него {report.WithoutStanding}, не удалось проверить {report.Unknown}");
        return;
    }

    // Дочитать детали будущих стартов без loglig-id (docs/plans/start-list-plan.md, шаг С2):
    //   dotnet run -- --refresh-upcoming [--days 14]
    // Без этого у будущего старта нет logligId, и стартовый протокол начать нечем.
    internal static async Task RefreshUpcomingAsync(WebApplication app, string[] args)
    {
        var daysAhead = 14;
        var daysIndex = Array.IndexOf(args, "--days");
        if (daysIndex >= 0 && daysIndex + 1 < args.Length && int.TryParse(args[daysIndex + 1], out var d))
            daysAhead = d;

        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
        var (checkedCount, resolved) = await svc.RefreshUpcomingDetailsAsync(daysAhead);

        Console.WriteLine($"Догрузка деталей будущих стартов (окно {daysAhead} дн.): проверено {checkedCount}, добыто {resolved}");
        return;
    }

    // Ретро-сверка загруженных протоколов с источником (docs/data-integrity.md, фаза Д1):
    //   dotnet run -- --audit-imports [--id <discoveredId>] [--limit N]
    // Качает протокол заново, парсит ТЕКУЩИМ парсером и сравнивает с БД. Диагноз, а не лечение:
    // результаты не меняются, пишется только журнал сверки. Ходит в чужой прод — с паузами,
    // поэтому первый прогон удобно делать на одной записи (--id или --limit 1).
    internal static async Task AuditImportsAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IImportAuditService>();

        int? Arg(string name)
        {
            var i = Array.IndexOf(args, name) + 1;
            return i > 0 && i < args.Length && int.TryParse(args[i], out var v) ? v : null;
        }

        var reports = Arg("--id") is int oneId
            ? [await audit.AuditDiscoveredAsync(oneId)]
            : await audit.AuditAllAsync(Arg("--limit"));

        foreach (var r in reports)
        {
            Console.WriteLine($"\n#{r.DiscoveredId} (compID {r.OrgCompId}) «{r.Name}»");
            if (r.Error != null) { Console.WriteLine($"  ОШИБКА: {r.Error}"); continue; }

            foreach (var d in r.Days)
            {
                if (d.CompetitionId == null)
                {
                    Console.WriteLine($"  {d.Date}: дня нет в БД (файл обещает {d.ExpectedRows} строк)");
                    continue;
                }
                var verdict = d.Mismatches.Count == 0 ? "сошлось" : $"РАСХОЖДЕНИЙ {d.Mismatches.Count}";
                Console.WriteLine($"  {d.Date} → comp {d.CompetitionId} «{d.CompetitionName}»: файл {d.ExpectedRows}, БД {d.ActualRows} — {verdict}");
                foreach (var m in d.Mismatches)
                    Console.WriteLine($"      {m.EventKey}: файл {m.ExpectedRows}, БД {m.ActualRows}");
            }
        }

        var bad = reports.Count(r => r.HasProblems);
        Console.WriteLine($"\nИтог: проверено {reports.Count}, с проблемами {bad}. Журнал — Sys_ImportReconciliation (ImportFileName начинается с 'audit:').");
        return;
    }

    // Переимпорт протокола из Discovery без админки (docs/data-integrity.md, чек-лист §8):
    //   dotnet run -- --repull <discoveredId> [--delete-missing] [--dry-run [--json-out <file>]]
    // Тот же путь, что кнопка «Перезатянуть»: качаем пару HE + EN, парсим, импортируем с
    // перезаписью. --delete-missing дополнительно удаляет строки, которых нет в файле (дубликаты
    // и следы старых разборов) — по умолчанию ВЫКЛЮЧЕНО, как и в UI: удаление отдельное решение.
    internal static async Task RepullAsync(WebApplication app, string[] args)
    {
        var idIndex = Array.IndexOf(args, "--repull") + 1;
        if (idIndex >= args.Length || !int.TryParse(args[idIndex], out var discoveredId))
        {
            Console.Error.WriteLine("Usage: dotnet run -- --repull <discoveredId> [--delete-missing] [--dry-run [--json-out <file>]]");
            Environment.Exit(1);
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Swimm.Infrastructure.Data.SwimmDbContext>();
        var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryProvider>();
        var source = scope.ServiceProvider.GetRequiredService<IResultSourceProvider>();
        var importer = scope.ServiceProvider.GetRequiredService<IImportService>();

        var row = await db.DiscoveredCompetitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == discoveredId);
        if (row?.LogligId is not int logligId)
        {
            Console.Error.WriteLine($"Запись #{discoveredId} не найдена или без LogligId");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine($"Качаю протокол loglig {logligId} для «{row.Name}» (compID {row.OrgCompId})…");
        var pdf = await discovery.FetchResultsPdfAsync(logligId, "he-IL");
        // EN вторым файлом — как у кнопки: без EN-имён импорт теряет фоллбек матчинга по
        // английскому имени и заводит теней (И-46). EN недоступен — едем на одном HE.
        byte[]? pdfEn = null;
        try { pdfEn = await discovery.FetchResultsPdfAsync(logligId, "en-US"); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            Console.WriteLine($"EN-протокол недоступен ({ex.Message}) — разбор только по HE");
        }
        var pair = await Swimm.Infrastructure.Services.ProtocolPairParser.ParseAsync(
            source, pdf, $"isrorg-{row.OrgCompId}-loglig-{logligId}-he.pdf",
            pdfEn, $"isrorg-{row.OrgCompId}-loglig-{logligId}-en.pdf");
        var parsed = pair.Parsed;
        if (pair.PairError != null)
            Console.WriteLine($"Пара HE+EN не склеилась ({pair.PairError}) — разбор только по HE, EN-имена не придут");
        Console.WriteLine($"Распознано строк: {parsed.ResultCount}" + (pair.UsedEn ? " (HE+EN)" : " (HE)"));

        // Промежуточные эстафет — тем же правилом, что у кнопки «Затянуть»: только чемпионаты
        // (по имени ИЛИ по галке соревнования в базе — у возрастных «ישראל» в имени нет).
        if (await Swimm.Infrastructure.Repositories.CompetitionAdminRepository.ShouldFetchSplitsAsync(
                db, row.Name, row.OrgCompId))
        {
            var splits = await scope.ServiceProvider.GetRequiredService<IRelaySplitProvider>()
                .EnrichAsync(logligId, parsed.ResultsJson);
            parsed = parsed with { ResultsJson = splits.ResultsJson };
            Console.WriteLine($"Эстафеты: {splits.Message}");

            // Даты дисциплин многодневки из сетки loglig (multiday-split-plan, М3): без них протокол
            // «03/01 - 04/01» лёг бы одной датой, и перезалив сложил бы все строки во второй день.
            var datesBefore = parsed.Competitions.Count;
            parsed = await scope.ServiceProvider.GetRequiredService<ICollapsedDaysFixer>().FixAsync(logligId, parsed);
            if (parsed.Competitions.Count != datesBefore)
                Console.WriteLine($"Даты: {parsed.Warnings.LastOrDefault()}");
        }

        // Дни этого старта в базе (Д2). Протокол многодневный, а в базе один день без события —
        // импорт завёл бы второй день отдельным соревнованием и задвоил бы строки первого в старом.
        var repullDays = await Swimm.Infrastructure.Services.CompetitionIdentity.ResolveDaysAsync(db, row.OrgCompId);
        if (parsed.Competitions.Count > 1 && repullDays is [{ EventId: null } collapsedDay])
        {
            Console.Error.WriteLine(
                $"НЕ ПЕРЕЗАЛИВАЮ: протокол на {parsed.Competitions.Count} дня, а в базе старт одним днём " +
                $"#{collapsedDay.Id} ({collapsedDay.Date}). Сначала: --split-days {collapsedDay.Id}");
            Environment.Exit(1);
            return;
        }

        // Новый день, если появится, — к событию этого старта, а не отдельным соревнованием.
        var repullEventId = repullDays.Select(d => d.EventId).FirstOrDefault(e => e != null);

        // Сухой прогон: весь конвейер (PDF → разбор → эстафеты → даты) без импорта. Проверить, что
        // перезалив положит, — до того как он что-то перепишет (08.10.2026: этап ויקטור בלכסן).
        if (args.Contains("--dry-run"))
        {
            var outIndex = Array.IndexOf(args, "--json-out") + 1;
            if (outIndex > 0 && outIndex < args.Length)
            {
                await File.WriteAllTextAsync(args[outIndex], parsed.ResultsJson);
                Console.WriteLine($"JSON разбора: {Path.GetFullPath(args[outIndex])}");
            }
            Console.WriteLine($"Сухой прогон: дней в протоколе {parsed.Competitions.Count}, в базе {repullDays.Count}" +
                              (repullEventId is int ev ? $" (событие #{ev})" : "") + ". Ничего не импортировано.");
            return;
        }

        var deleteMissing = args.Contains("--delete-missing");
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(parsed.ResultsJson));
        var result = await importer.ImportAsync(
            json, $"isrorg-{row.OrgCompId}-loglig-{logligId}-he.pdf", null,
            new ImportEventOptions(repullEventId, null, OverwriteExisting: true, DeleteMissing: deleteMissing),
            row.OrgCompId);

        Console.WriteLine($"\n{result.Message}");
        Console.WriteLine(result.Reconciliation);
        if (result.DataChecks.Length > 0) Console.WriteLine(result.DataChecks);
        foreach (var line in result.DiagnosticLog.Where(l =>
                     l.Contains("Идентичность") || l.Contains("Upsert") || l.Contains("Сверка")))
            Console.WriteLine("  " + line);
        if (result.ErrorMessages.Count > 0)
            Console.WriteLine("ОШИБКИ: " + string.Join("; ", result.ErrorMessages.Take(5)));
        return;
    }

    // Разовая синхронизация «входящих» автозабора isr.org.il (фаза 6) и выход:
    //   dotnet run -- --discovery-sync
    internal static async Task DiscoverySyncAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
        var sync = await discovery.SyncAsync();
        Console.WriteLine($"Discovery: на сайте {sync.TotalOnSite}, добавлено {sync.Added}, обновлено {sync.Updated}");
        return;
    }
}

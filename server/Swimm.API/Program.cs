using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.API.BackgroundServices;
using Swimm.API.Security;
using Swimm.API.Services;
using Microsoft.EntityFrameworkCore;
using Swimm.Application;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure;
using Swimm.Parsing;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json — gitignored, для локального переключения на другую БД.
// Перекрывает appsettings.json и appsettings.{Environment}.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddMemoryCache();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
// Парсеры протоколов + IResultSourceProvider (Swimm.Parsing) — потребитель появится
// на этапе 1.3 (страница Import: PDF → парс → превью → импорт).
builder.Services.AddParsing();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact",
        policy => policy
            .WithOrigins("http://localhost:5173", "http://localhost:5203")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Services.AddControllers();
builder.Services.AddRazorPages();

// Аудит админки (фаза 7.4): actor берётся из HTTP-контекста через ICurrentActor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, Swimm.API.Services.HttpCurrentActor>();
builder.Services.AddHostedService<ImportBackgroundService>();

// Rate limiting для чувствительных к перебору auth-эндпоинтов (login/register/forgot/reset).
// Фиксированное окно по IP: 10 запросов в минуту, лишнее — 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Реакции (❤/🎉): щедрее auth — это обычные клики, но защищаемся от бот-накрутки.
    // Ключ — userId (эндпоинты только для залогиненных), IP — фоллбек до авторизации.
    options.AddPolicy("reactions", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Медиа-мутации (добавление ссылок, заявки на публикацию): защита от массового
    // замусоривания таблицы и модерационной ленты. Ключ — userId ([Authorize]-эндпоинты).
    options.AddPolicy("media", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
// Antiforgery: header-based (double-submit) для защиты admin-мутаций.
// Клиент читает токен из JS-переменной, генерируемой в _Layout.cshtml, и посылает в этом заголовке.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");

// Authentication: Cookie + (опционально) Google
var googleSection = builder.Configuration.GetSection("Authentication:Google");
var googleClientId = googleSection["ClientId"];
var googleClientSecret = googleSection["ClientSecret"];
var googleEnabled = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = googleEnabled
            ? GoogleDefaults.AuthenticationScheme
            : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "Swimm.Auth";
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        // Ре-валидация сессии: отзыв доступа (IsActive), смена ролей и «выйти со всех»
        // через сверку SecurityStamp. Поход в БД троттлится интервалом внутри валидатора.
        options.Events.OnValidatePrincipal = CookieSecurityStampValidator.ValidateAsync;
    })
    // Транзитная схема для OAuth-рукопожатия. Google пишет промежуточные claims сюда,
    // а не в основную куку → OnValidatePrincipal их не трогает. Короткоживущая.
    .AddCookie(AuthSchemes.External, options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "Swimm.External";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    });

if (googleEnabled)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.CallbackPath = "/signin-google";
        // Промежуточный вход — в транзитную схему, а не в основную куку.
        options.SignInScheme = AuthSchemes.External;
        options.Scope.Add("profile");
        options.Scope.Add("email");
    });
}

builder.Services.AddSingleton<DbStatusService>();
builder.Services.AddHostedService<DbPingBackgroundService>();
builder.Services.AddHostedService<Swimm.API.BackgroundServices.CompetitionDiscoveryBackgroundService>();
builder.Services.AddHostedService<Swimm.API.BackgroundServices.LogligSuggestionVerificationBackgroundService>();
builder.Services.AddHostedService<Swimm.API.BackgroundServices.LogligBatchBackgroundService>();

var app = builder.Build();

// Миграции применяются явно — либо через `dotnet ef database update`, либо передав флаг
// `--migrate` при запуске приложения. Авто-миграция при старте отключена: runtime-процесс
// не должен иметь DDL-прав (подготовка к least-privilege DB role, Phase 3).
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<IDbMigrator>().Migrate();
    return;
}

// Пересчёт объединённых мест «Combine All Results» во всех соревнованиях с этим флагом:
//   dotnet run -- --recalc-combined
// Нужен разово после миграции (бэкфилл) и как аварийная кнопка, если материализованные
// значения разошлись с результатами.
if (args.Contains("--recalc-combined"))
{
    using var scope = app.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<ICompetitionRecalculationService>();
    var updated = await svc.RecalculateAllCombinedAsync();
    Console.WriteLine($"Combine All Results: пересчитано строк — {updated}");
    return;
}

// Пересчёт материализованного клубного зачёта во всех соревнованиях:
//   dotnet run -- --rebuild-club-standings
// Нужен разово после миграции (бэкфилл истории) и как аварийная кнопка, если зачёты
// разошлись с результатами. Штатно таблица поддерживается на шве пересчёта соревнования.
if (args.Contains("--rebuild-club-standings"))
{
    using var scope = app.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<IClubStandingService>();
    var rows = await svc.RebuildAllAsync();
    Console.WriteLine($"Клубный зачёт: строк в таблице — {rows}");
    return;
}

// Сверка справочника рекордов с нашими протоколами:
//   dotnet run -- --verify-records
// То же самое, что кнопка «Сверить с протоколами» на дашборде; отдельный флаг нужен, чтобы
// гонять сверку после массового переимпорта протоколов, не заходя в админку.
// ⚠ «Не найдено» — не ошибка справочника: протоколы загружены не за все годы
// (docs/plans/records-quality-plan.md).
if (args.Contains("--verify-records"))
{
    using var scope = app.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<IRecordQualityService>();
    var report = await svc.VerifyAllAsync();
    Console.WriteLine(
        $"Рекорды сверены: {report.Checked} · найдено {report.Found} · не найдено {report.NotFound} " +
        $"· с другой датой {report.FoundWrongDate}");
    return;
}

// Одноразовый сид рекордов/нормативов из легаси JS-файлов клиента:
//   dotnet run -- --seed-records <путь к client/public/data> [--force]
// --force заменяет содержимое таблиц целиком (иначе непустые таблицы — отказ).
if (args.Contains("--seed-records"))
{
    var dirIndex = Array.IndexOf(args, "--seed-records") + 1;
    if (dirIndex >= args.Length || args[dirIndex].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: dotnet run -- --seed-records <data-dir> [--force]");
        Environment.Exit(1);
        return;
    }

    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IRecordsSeeder>();
    var seedLog = await seeder.SeedAsync(args[dirIndex], args.Contains("--force"));
    foreach (var line in seedLog)
        Console.WriteLine(line);
    return;
}

// Одноразовый сид тренировок «Дельфин-мастерс» (соревнования не трогаем — они уже в БД):
//   dotnet run -- --seed-dolphin-training <json> <canon.csv> --group <hubGroupId> [--force]
if (args.Contains("--seed-dolphin-training"))
{
    string ArgAfter(string flag, string what)
    {
        var i = Array.IndexOf(args, flag) + 1;
        if (i <= 0 || i >= args.Length || args[i].StartsWith("--"))
        {
            Console.Error.WriteLine(
                "Usage: dotnet run -- --seed-dolphin-training <json> <canon.csv> --group <hubGroupId> [--force]");
            Environment.Exit(1);
        }
        return args[i];
    }

    var jsonPath = args[Array.IndexOf(args, "--seed-dolphin-training") + 1];
    var csvPath = args[Array.IndexOf(args, "--seed-dolphin-training") + 2];
    var groupId = int.Parse(ArgAfter("--group", "hubGroupId"));

    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IDolphinTrainingSeeder>();
    var seedLog = await seeder.SeedAsync(jsonPath, csvPath, groupId, args.Contains("--force"));
    foreach (var line in seedLog)
        Console.WriteLine(line);
    return;
}

// Ретро-сверка загруженных протоколов с источником (docs/data-integrity.md, фаза Д1):
//   dotnet run -- --audit-imports [--id <discoveredId>] [--limit N]
// Качает протокол заново, парсит ТЕКУЩИМ парсером и сравнивает с БД. Диагноз, а не лечение:
// результаты не меняются, пишется только журнал сверки. Ходит в чужой прод — с паузами,
// поэтому первый прогон удобно делать на одной записи (--id или --limit 1).
if (args.Contains("--audit-imports"))
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

// Прогон реестра проверок данных (то же, что кнопка на /Admin/Health):
//   dotnet run -- --check-data
// Читающий: ставит диагноз и пишет находки, ничего не чинит.
if (args.Contains("--check-data"))
{
    using var scope = app.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<IDataCheckRunner>();

    var run = await runner.RunAllAsync("manual");
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

// Склейка клубов с ОДИНАКОВЫМ именем из консоли (эвристика same-name — «уверенная»):
//   dotnet run -- --merge-same-name-clubs [--apply]
// Без --apply это dry-run. Нужен после ивритских переимпортов: пока матчинг клуба шёл по
// паре Name|NameEn, каждый HE-протокол плодил двойника канонического клуба (инцидент И-9).
if (args.Contains("--merge-same-name-clubs"))
{
    using var scope = app.Services.CreateScope();
    var dedup = scope.ServiceProvider.GetRequiredService<IClubDedupService>();
    var merge = scope.ServiceProvider.GetRequiredService<IClubMergeService>();

    var report = await dedup.FindCandidatesAsync();
    var pairs = report.Candidates
        .Where(c => c.Heuristic == "same-name")
        .Select(c => new ClubMergePair(c.CanonicalId, c.DuplicateId))
        .ToList();

    Console.WriteLine($"Пар с одинаковым именем: {pairs.Count}");
    foreach (var c in report.Candidates.Where(c => c.Heuristic == "same-name"))
        Console.WriteLine($"  #{c.CanonicalId} «{c.CanonicalName}» ({c.CanonicalResults}) ← #{c.DuplicateId} ({c.DuplicateResults})");

    if (pairs.Count == 0) return;

    var apply = args.Contains("--apply");
    var result = await merge.MergeAsync(pairs, dryRun: !apply);
    var bad = result.Pairs.Where(p => p.Status is not ("merged" or "dry-run")).ToList();
    Console.WriteLine($"\n{(apply ? "Применено" : "Dry-run")}: пар {result.Pairs.Count}, проблемных {bad.Count}");
    foreach (var p in bad)
        Console.WriteLine($"  #{p.CanonicalId}←#{p.DuplicateId}: {p.Status} {string.Join("; ", p.Conflicts)}");
    if (!apply) Console.WriteLine("Повтори с --apply, чтобы применить.");
    return;
}

// Чистка пустых клубов из консоли (то же, что кнопка «Удалить все пустые» на /Admin/Clubs):
//   dotnet run -- --delete-empty-clubs
// Зовёт тот же сервис, что и кнопка, — предикат «пустого клуба» живёт в одном месте
// (правило 1 в docs/data-integrity.md). Непрошедшие проверку печатаются с причиной.
if (args.Contains("--delete-empty-clubs"))
{
    using var scope = app.Services.CreateScope();
    var clubs = scope.ServiceProvider.GetRequiredService<IClubAdminRepository>();
    var report = await clubs.DeleteAllEmptyAsync();

    foreach (var c in report.Deleted)
        Console.WriteLine($"удалён #{c.Id} «{c.Name}»");
    foreach (var reason in report.Skipped)
        Console.WriteLine($"пропущен: {reason}");
    Console.WriteLine($"\nИтог: удалено {report.Deleted.Count}, пропущено {report.Skipped.Count}");
    return;
}

// Переимпорт протокола из Discovery без админки (docs/data-integrity.md, чек-лист §8):
//   dotnet run -- --repull <discoveredId> [--delete-missing]
// Тот же путь, что кнопка «Перезатянуть»: качаем HE-протокол, парсим, импортируем с
// перезаписью. --delete-missing дополнительно удаляет строки, которых нет в файле (дубликаты
// и следы старых разборов) — по умолчанию ВЫКЛЮЧЕНО, как и в UI: удаление отдельное решение.
if (args.Contains("--repull"))
{
    var idIndex = Array.IndexOf(args, "--repull") + 1;
    if (idIndex >= args.Length || !int.TryParse(args[idIndex], out var discoveredId))
    {
        Console.Error.WriteLine("Usage: dotnet run -- --repull <discoveredId> [--delete-missing]");
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
    using var pdfStream = new MemoryStream(pdf);
    var parsed = await source.ParseAsync(new ResultSourceRequest(
        pdfStream, $"isrorg-{row.OrgCompId}-loglig-{logligId}-he.pdf", "IsrOrg", Language: "he"));
    Console.WriteLine($"Распознано строк: {parsed.ResultCount}");

    var deleteMissing = args.Contains("--delete-missing");
    using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(parsed.ResultsJson));
    var result = await importer.ImportAsync(
        json, $"isrorg-{row.OrgCompId}-loglig-{logligId}-he.pdf", null,
        new ImportEventOptions(null, null, OverwriteExisting: true, DeleteMissing: deleteMissing),
        row.OrgCompId);

    Console.WriteLine($"\n{result.Message}");
    Console.WriteLine(result.Reconciliation);
    foreach (var line in result.DiagnosticLog.Where(l =>
                 l.Contains("Идентичность") || l.Contains("Upsert") || l.Contains("Сверка")))
        Console.WriteLine("  " + line);
    if (result.ErrorMessages.Count > 0)
        Console.WriteLine("ОШИБКИ: " + string.Join("; ", result.ErrorMessages.Take(5)));
    return;
}

// Разовая синхронизация «входящих» автозабора isr.org.il (фаза 6) и выход:
//   dotnet run -- --discovery-sync
if (args.Contains("--discovery-sync"))
{
    using var scope = app.Services.CreateScope();
    var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
    var sync = await discovery.SyncAsync();
    Console.WriteLine($"Discovery: на сайте {sync.TotalOnSite}, добавлено {sync.Added}, обновлено {sync.Updated}");
    return;
}

// Склейка пловцов-дублей (см. docs/tasks/dedup-report.md):
//   dotnet run -- --merge-swimmers <pairs.csv> [--apply]
// CSV: строки "canonicalId,duplicateId" (пустые и # -комментарии пропускаются).
// Без --apply — dry-run: печатает план, БД не меняется.
if (args.Contains("--merge-swimmers"))
{
    var csvIndex = Array.IndexOf(args, "--merge-swimmers") + 1;
    if (csvIndex >= args.Length || args[csvIndex].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: dotnet run -- --merge-swimmers <pairs.csv> [--apply]");
        Environment.Exit(1);
        return;
    }

    var mergePairs = new List<Swimm.Application.Dtos.SwimmerMergePair>();
    foreach (var line in File.ReadLines(args[csvIndex]))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
        var parts = trimmed.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var canon) || !int.TryParse(parts[1], out var dup))
        {
            Console.Error.WriteLine($"Некорректная строка CSV: «{line}» (ожидается canonicalId,duplicateId)");
            Environment.Exit(1);
            return;
        }
        mergePairs.Add(new Swimm.Application.Dtos.SwimmerMergePair(canon, dup));
    }

    using var scope = app.Services.CreateScope();
    var merge = scope.ServiceProvider.GetRequiredService<ISwimmerMergeService>();
    var mergeReport = await merge.MergeAsync(mergePairs, dryRun: !args.Contains("--apply"));
    Console.WriteLine(mergeReport.DryRun
        ? "=== DRY-RUN: план склейки, БД не изменена (добавь --apply для применения) ==="
        : "=== ПРИМЕНЕНО ===");
    foreach (var p in mergeReport.Pairs)
    {
        Console.WriteLine($"[{p.Status}] canonical {p.CanonicalId} ← duplicate {p.DuplicateId}");
        foreach (var a in p.Actions) Console.WriteLine($"    {a}");
        foreach (var c in p.Conflicts) Console.WriteLine($"    !! {c}");
    }
    return;
}

// Разовый бэкфилл структурного состава эстафет (RelayMembers) для данных,
// импортированных до появления структурных ног:
//   dotnet run -- --backfill-relay-members [--apply]
// Без --apply — dry-run: печатает отчёт, БД не меняется. Идемпотентно.
if (args.Contains("--backfill-relay-members"))
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
if (args.Contains("--backfill-discovery-orgcompid"))
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

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRouting();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.UseCors("AllowReact");

app.UseAuthentication();

// Dev-обход логина в админку: appsettings.Development.json → "DevAdminBypass": true.
// Работает ТОЛЬКО в Development и только если флаг включён явно; в проде ветка мертва.
// Неаутентифицированный запрос получает синтетического пользователя с ролью Admin,
// чтобы можно было работать с /Admin без Google OAuth (например, при вёрстке админки).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("DevAdminBypass"))
{
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var identity = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "0"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "dev-admin"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin"),
            ], authenticationType: "DevAdminBypass");
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        await next();
    });
}

app.UseAuthorization();

// Maintenance mode — если MaintenanceMode = true, пускаем только админов
app.Use(async (context, next) =>
{
    var settings = context.RequestServices.GetRequiredService<ISettingsService>();
    if (settings.GetValue("MaintenanceMode", false)
        && !context.Request.Path.StartsWithSegments("/admin")
        && !context.Request.Path.StartsWithSegments("/api/admin")
        && !context.Request.Path.StartsWithSegments("/auth")
        && !context.Request.Path.StartsWithSegments("/signin-google"))
    {
        if (context.User.IsInRole("Admin")) { await next(); return; }

        context.Response.StatusCode = 503;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync("""
            <!DOCTYPE html>
            <html><head><title>Maintenance</title>
            <style>body{font-family:sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:#1a1a2e;color:#e0e0e0;}
            .box{text-align:center}.box h1{font-size:3rem;margin-bottom:.5rem}.box p{color:#90a4ae;font-size:1.1rem}</style></head>
            <body><div class="box"><h1>🔧</h1><h1>Site is under maintenance</h1><p>We'll be back shortly.</p></div></body></html>
            """);
        return;
    }
    await next();
});

// Блокируем /admin/ для не-админов
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin"))
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.Redirect("/auth/login/google?returnUrl=" + Uri.EscapeDataString(context.Request.Path));
            return;
        }
        if (!context.User.IsInRole("Admin"))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Forbidden: Admin role required.");
            return;
        }
    }
    await next();
});

// Чистые URL → физический html в wwwroot (контракт синхронен с client/src/utils/routes.ts).
// Меняем ТОЛЬКО Request.Path, поэтому UseStaticFiles ниже отдаёт нужный файл, а в адресной
// строке браузера остаётся чистый путь; клиент читает идентичность из location.pathname.
// Query-строка сохраняется автоматически (её не трогаем).
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    // Пропускаем API/служебные ветки и любые запросы к реальным файлам (есть расширение).
    var lastSeg = path.AsSpan(path.LastIndexOf('/') + 1);
    var hasExtension = lastSeg.Contains('.');
    if (!hasExtension
        && !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
    {
        var rewrite = ResolveCleanUrl(path);
        if (rewrite is not null)
            context.Request.Path = rewrite;
    }

    await next();

    // Локальная функция: чистый путь → html-файл (null = не наш маршрут).
    static string? ResolveCleanUrl(string path)
    {
        // Нормализуем завершающий слэш (кроме корня).
        var p = path.Length > 1 ? path.TrimEnd('/') : path;
        var seg = p.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return seg.Length switch
        {
            0 => null, // корень отдаёт UseDefaultFiles (home.html)
            1 => seg[0] switch
            {
                "results" => "/results_main.html",
                "competitions" => "/competitions.html",
                "groups" => "/groups.html",
                "swimmers" => "/results_main.html", // /swimmers без id — пусть падает штатно
                "clubs" => "/results_main.html",     // /clubs без id — пусть падает штатно
                "my-media" => "/media.html",
                "about" => "/about.html",
                _ => null,
            },
            >= 2 => seg[0] switch
            {
                "competitions" => "/results_main.html",              // /competitions/{id}
                "swimmers" => "/swimmer.html",                       // /swimmers/{id}
                "clubs" => "/club.html",                             // /clubs/{id}
                "groups" when seg.Length >= 3 && seg[2] == "results"
                    => "/results_main.html",                         // /groups/{slug}/results
                "groups" => "/groups.html",                          // /groups/{slug}
                _ => null,
            },
            _ => null,
        };
    }
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "home.html", "index.html" }
});
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.PhysicalPath;
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.ContentType = "text/html; charset=utf-8";
    }
});

app.MapRazorPages();
app.MapControllers();

// SPA не получает antiforgery-токен из Razor-разметки (_Layout.cshtml), поэтому
// выставляем отдельный эндпоинт. Токен нужен для всех мутаций FavoritesController.
app.MapGet("/api/antiforgery/token", (IAntiforgery af, HttpContext ctx) =>
{
    var tokens = af.GetAndStoreTokens(ctx);
    return Results.Ok(new { token = tokens.RequestToken });
}).RequireAuthorization();

app.MapGet("/api/db-status",
    (DbStatusService s) => Results.Ok(new { available = s.IsAvailable }))
    .AllowAnonymous();

// Публичная конфигурация клиента (не секреты!). resultsLoadMode: full/paged — принудительный
// режим загрузки результатов (?loadMode= игнорируется), client — режим выбирает клиент.
// См. ResultsLoadMode в Admin/Settings и results-load-mode.ts на клиенте.
app.MapGet("/api/client-config",
    (ISettingsService settings) => Results.Ok(new
    {
        resultsLoadMode = settings.GetValue("ResultsLoadMode", "client")
    }))
    .AllowAnonymous();

app.Run();

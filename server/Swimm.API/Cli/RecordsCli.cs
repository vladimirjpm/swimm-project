using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — справочник рекордов. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class RecordsCli
{
    // Сверка справочника рекордов с нашими протоколами:
    //   dotnet run -- --verify-records
    // То же самое, что кнопка «Сверить с протоколами» на дашборде; отдельный флаг нужен, чтобы
    // гонять сверку после массового переимпорта протоколов, не заходя в админку.
    // ⚠ «Не найдено» — не ошибка справочника: протоколы загружены не за все годы
    // (docs/plans/records-quality-plan.md).
    internal static async Task VerifyRecordsAsync(WebApplication app, string[] args)
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
    internal static async Task SeedRecordsAsync(WebApplication app, string[] args)
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

    // Обновление справочника рекордов В ПРАВИЛЬНОМ ПОРЯДКЕ:
    //   dotnet run -- --records-refresh [--dry-run]
    //
    // Порядок здесь — не деталь реализации, а САМО ПРАВИЛО (решение Влада 2026-08-24).
    // На country/ISR/open пишут ДВА источника с одним ключом упсерта: отчёт NR World Aquatics
    // (есть по всем 246 странам, но по Израилю отстал местами на 10 лет — 50 на спине 50 м у
    // них 24.60 от 2016 против 24.36 от 14/08/2026 у федерации) и PDF isr.org.il. Побеждает
    // применённый ПОСЛЕДНИМ, поэтому Израиль идёт вторым: World Aquatics приносит широту
    // (мировые рекорды + всё, чего у федерации нет), федерация поверх возвращает свои свежие.
    //
    // Ровно поэтому источники перечислены списком, а не запускаются как попало из админки:
    // перепутанный порядок молча откатывает 19 национальных рекордов.
    // Подробности — docs/data-integrity.md, И-13.
    //
    // Федеральных источников ДВА, и оба идут ПОСЛЕ World Aquatics: isrorg-age (возрастные +
    // строки «National Record») и isrorg-masters (мастерские 25-29…). Между собой их порядок
    // не важен — дифф режет по Category (age / masters, RecordDiffService.SourceScopes), общего
    // ключа упсерта у них нет. Важно только, что оба поверх WA.
    // isrorg-masters добавлен 2026-09-16: с 2026-08-24 его в списке не было, и команда не
    // трогала 726 из 1 683 строк справочника вообще (пробел числился в plans/azure-deploy-plan.md).
    // `--source <ключ>` — один источник (21.09.2026, для wa-junior; см. предупреждение ниже).
    // `--records-check` — то же без Apply (= `--records-refresh --dry-run`): проверка свежести.
    // Обе команды пишут журнал Sys_RecordSourceChecks (records-freshness-plan U2): сходили в
    // источник — значит проверили, откуда бы ни нажали.
    internal static async Task RecordsRefreshAsync(WebApplication app, string[] args)
    {
        var dryRun = args.Contains("--dry-run") || args.Contains("--records-check");
        using var scope = app.Services.CreateScope();
        var providers = scope.ServiceProvider.GetServices<IRecordSourceProvider>()
            .ToDictionary(p => p.Source, StringComparer.OrdinalIgnoreCase);
        var checkService = scope.ServiceProvider.GetRequiredService<IRecordSourceCheckService>();

        // wa-masters стоит рядом с worldrecords: это тоже World Aquatics, и он единственный
        // владелец world/masters — ни с кем ключами упсерта не спорит, поэтому его место в списке
        // определяется только здравым смыслом «сначала мир, потом федерация».
        // wa-junior — туда же и по той же причине: единственный владелец world/junior.
        string[] order = [.. Swimm.Application.Constants.RecordSources.Order];

        // --source <ключ> — один источник из списка. Безопасно только для однохозяйной оси
        // (wa-masters, wa-junior): у country/ISR/open два владельца (И-13), и прогон одного
        // worldrecords без федерации откатит израильские рекорды — поэтому тут предупреждение.
        var onlyIdx = Array.IndexOf(args, "--source");
        if (onlyIdx >= 0)
        {
            var only = onlyIdx + 1 < args.Length ? args[onlyIdx + 1] : "";
            if (!order.Contains(only, StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"--source: '{only}' не из списка {string.Join(", ", order)}");
                Environment.Exit(1);
                return;
            }
            if (only.Equals("worldrecords", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine("⚠ worldrecords без isrorg-* откатит country/ISR/open (И-13) — догони федеральными.");
            order = [only];
        }

        foreach (var sourceKey in order)
        {
            if (!providers.ContainsKey(sourceKey))
            {
                Console.Error.WriteLine($"Источник '{sourceKey}' не зарегистрирован — пропуск");
                continue;
            }

            Console.WriteLine($"\n=== {sourceKey} ===");
            var check = await checkService.CheckAsync(sourceKey);
            if (check.Diff is not { } diff)
            {
                Console.Error.WriteLine($"  не скачалось: {check.Error}  (журнал: failed, #{check.CheckId})");
                continue;
            }

            Console.WriteLine($"  проверка #{check.CheckId}: {check.Outcome}; без изменений {diff.UnchangedCount}, "
                + $"изменится {diff.ChangedCount}, новых {diff.AddedCount}, нет в источнике {diff.MissingInSourceCount}");
            foreach (var e in diff.Changed.Take(30))
                Console.WriteLine($"    {e.RegionType,-7} {e.RegionCode,-3} {e.AgeKey,-6} {e.Gender,-6} {e.PoolType,-4} "
                    + $"{e.Style,-18} {e.Distance,-7} {e.OldTime ?? "(пусто)",9} -> {e.NewTime,9}");
            if (diff.ChangedCount > 30) Console.WriteLine($"    … ещё {diff.ChangedCount - 30}");
            foreach (var e in diff.Added.Take(15))
                Console.WriteLine($"    + {e.RegionType,-7} {e.RegionCode,-3} {e.AgeKey,-6} {e.Gender,-6} {e.PoolType,-4} "
                    + $"{e.Style,-18} {e.Distance,-7} {e.NewTime,9}");
            // Сторож правдоподобия (И-20): такие значения всё равно применяются (копия обязана
            // совпадать с источником), но уходят в реестр спорных кандидатами — их надо разобрать.
            var suspicious = diff.Suspicious ?? [];
            if (suspicious.Count > 0)
            {
                Console.WriteLine($"  ⚠ неправдоподобно: {suspicious.Count} — после Apply кандидатами в реестр (/Admin/Records?tab=issues)");
                foreach (var s in suspicious)
                    Console.WriteLine($"    ⚠ {s.RegionType,-7} {s.RegionCode,-3} {s.AgeKey,-6} {s.Gender,-6} {s.PoolType,-4} "
                        + $"{s.Style,-18} {s.Distance,-7} {s.Time,9}  {s.Reason}");
            }

            // Защита оспоренного времени (И-25): человек уже разобрал эти значения как ошибку
            // источника, Apply их не возьмёт — в базе останется то, что лежит.
            var protectedEntries = diff.Changed.Concat(diff.Added).Where(e => e.ProtectedByIssue).ToList();
            if (protectedEntries.Count > 0)
            {
                Console.WriteLine($"  🛡 защищено реестром: {protectedEntries.Count} — Apply НЕ перезапишет (/Admin/Records?tab=issues)");
                foreach (var e in protectedEntries)
                    Console.WriteLine($"    🛡 {e.RegionType,-7} {e.RegionCode,-3} {e.AgeKey,-6} {e.Gender,-6} {e.PoolType,-4} "
                        + $"{e.Style,-18} {e.Distance,-7} {e.OldTime ?? "(пусто)",9} <- отвергнут {e.NewTime}");
            }

            if (dryRun) { Console.WriteLine("  --dry-run: не применяю"); continue; }

            var applied = await checkService.ApplyAsync(
                new Swimm.Application.Dtos.RecordDiffApplyRequest(diff.DiffId, ApplyAdded: true, ApplyChanged: true));
            Console.WriteLine(applied.Success
                ? $"  применено: {applied.AppliedCount}"
                    + (applied.CandidatesCreated > 0 ? $"; в реестр кандидатами: {applied.CandidatesCreated}" : "")
                    + (applied.ProtectedCount > 0 ? $"; защищено реестром: {applied.ProtectedCount}" : "")
                : $"  ОШИБКА: {applied.Error}");
        }

        Console.WriteLine("\nГотово. Израиль обновлён ПОСЛЕ World Aquatics — иначе его рекорды откатятся.");
        return;
    }

    // Выгрузка источника рекордов в CSV — архив для будущих сверок:
    //   dotnet run -- --records-dump <источник> <файл.csv>
    //
    // Зачем. PDF доказывает, ЧТО отдал источник, но ответить «а что в нём стояло в этой полосе»
    // умеет только человек с открытым файлом. CSV отвечает на это `git diff`-ом двух прогонов —
    // ровно тот вопрос, на который 16.09.2026 ответить было нечем (docs/data-integrity.md, И-21).
    //
    // Строки сортируются по восьми осям: без устойчивого порядка диff показывал бы перестановки
    // вместо изменений, и смысл архива терялся бы.
    internal static async Task RecordsDumpAsync(WebApplication app, string[] args)
    {
        var dumpIdx = Array.IndexOf(args, "--records-dump") + 1;
        if (dumpIdx + 1 >= args.Length)
        {
            Console.Error.WriteLine("Usage: dotnet run -- --records-dump <worldrecords|wa-masters|wa-junior|isrorg-age|isrorg-masters> <файл.csv>");
            Environment.Exit(1);
            return;
        }

        var dumpSource = args[dumpIdx];
        var dumpPath = args[dumpIdx + 1];

        using var dumpScope = app.Services.CreateScope();
        var dumpProvider = dumpScope.ServiceProvider.GetServices<IRecordSourceProvider>()
            .FirstOrDefault(p => string.Equals(p.Source, dumpSource, StringComparison.OrdinalIgnoreCase));
        if (dumpProvider == null)
        {
            Console.Error.WriteLine($"Источник '{dumpSource}' не зарегистрирован.");
            Environment.Exit(1);
            return;
        }

        var dumped = await dumpProvider.FetchAsync(
            new Swimm.Application.Dtos.RecordSourceRequest(dumpSource, null, null, null, null, null));

        // Формат и порядок строк — общие с выгрузкой боевого прогона по странам
        // (Swimm.Application/Mapping/RecordCsvDump.cs). Две копии этого кода разъехались бы на
        // первой же правке, а весь смысл архива в том, что выгрузки разных дней сравнимы
        // построчно.
        //
        // UTF-8 БЕЗ BOM: выгрузка коммитится и сверяется контрольной суммой, BOM её сдвинет.
        // Перевод строки LF ставит сам RecordCsvDump: WriteAllLines дал бы CRLF на Windows и LF
        // на Linux, и сумма разъехалась бы на ровном месте (та же ловушка, что у бандла админки,
        // см. .gitattributes).
        await File.WriteAllTextAsync(
            dumpPath, RecordCsvDump.Build(dumped), new System.Text.UTF8Encoding(false));
        Console.WriteLine($"{dumpSource}: {dumped.Count} строк -> {dumpPath}");
        return;
    }
}

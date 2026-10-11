using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — склейки и чистка клубов и пловцов. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class MergeCli
{
    // Склейка клубов с ОДИНАКОВЫМ именем из консоли (эвристика same-name — «уверенная»):
    //   dotnet run -- --merge-same-name-clubs [--apply]
    // Без --apply это dry-run. Нужен после ивритских переимпортов: пока матчинг клуба шёл по
    // паре Name|NameEn, каждый HE-протокол плодил двойника канонического клуба (инцидент И-9).
    internal static async Task MergeSameNameClubsAsync(WebApplication app, string[] args)
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
    internal static async Task DeleteEmptyClubsAsync(WebApplication app, string[] args)
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

    // Склейка пловцов-дублей (см. docs/tasks/dedup-report.md):
    //   dotnet run -- --merge-swimmers <pairs.csv> [--apply]
    // CSV: строки "canonicalId,duplicateId" (пустые и # -комментарии пропускаются).
    // Без --apply — dry-run: печатает план, БД не меняется.
    internal static async Task MergeSwimmersAsync(WebApplication app, string[] args)
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
}

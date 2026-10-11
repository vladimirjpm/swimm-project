using Swimm.Application.Abstractions;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — многодневки: деление и сбор дней. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class MultidayCli
{
    // Деление схлопнутой многодневки по дням БЕЗ переимпорта (docs/plans/multiday-split-plan.md, М2):
    //   dotnet run -- --split-days <competitionId> [--loglig <id>] [--apply]
    // Даты дисциплин — из сетки loglig; строки переносятся UPDATE-ом (Id те же — медиа, пометки,
    // этапы и промежуточные едут с ними). Без --apply — только план. Всё или ничего.
    internal static async Task SplitDaysAsync(WebApplication app, string[] args)
    {
        var idIndex = Array.IndexOf(args, "--split-days") + 1;
        if (idIndex >= args.Length || !int.TryParse(args[idIndex], out var splitCompetitionId))
        {
            Console.Error.WriteLine("Usage: dotnet run -- --split-days <competitionId> [--loglig <id>] [--apply]");
            Environment.Exit(1);
            return;
        }

        int? splitLoglig = null;
        var splitLogligIndex = Array.IndexOf(args, "--loglig") + 1;
        if (splitLogligIndex > 0 && splitLogligIndex < args.Length && int.TryParse(args[splitLogligIndex], out var sl))
            splitLoglig = sl;

        var splitApply = args.Contains("--apply");
        using var scope = app.Services.CreateScope();
        var splitter = scope.ServiceProvider.GetRequiredService<ICompetitionDaySplitService>();
        var split = await splitter.SplitAsync(splitCompetitionId, splitLoglig, splitApply);

        Console.WriteLine($"#{split.CompetitionId} «{split.Name}», loglig {split.LogligId?.ToString() ?? "—"}");
        foreach (var day in split.Days)
            Console.WriteLine($"  день {day.DayNumber} {day.Date:dd/MM/yyyy}: строк {day.Rows}, медиа {day.Media} — "
                + (day.Existing ? $"остаётся #{day.CompetitionId}" : day.CompetitionId is int id ? $"новый #{id}" : "новое соревнование"));
        foreach (var (key, count) in split.Unresolved)
            Console.WriteLine($"  без дня: {key} ×{count}");

        if (split.Refusal != null)
        {
            Console.WriteLine($"НЕ ДЕЛИМ: {split.Refusal}");
            return;
        }
        if (!split.Applied)
        {
            Console.WriteLine("Ничего не записано (dry-run). Повторите с --apply.");
            return;
        }

        await scope.ServiceProvider.GetRequiredService<IAdminAuditService>().LogAsync(
            "competition.split-days", "Competition", split.CompetitionId.ToString(),
            $"Многодневка разделена по дням (loglig {split.LogligId}): событие #{split.EventId}, "
            + string.Join("; ", split.Days.Select(d => $"день {d.DayNumber} {d.Date:dd/MM/yyyy} #{d.CompetitionId} — {d.Rows} строк")));
        Console.WriteLine($"ПРИМЕНЕНО: событие #{split.EventId}, дни пересчитаны.");
        return;
    }

    // Сбор отдельных соревнований в многодневное событие (docs/plans/multiday-split-plan.md, М4):
    //   dotnet run -- --join-days <id>,<id>[,<id>…] [--apply]
    // Обратное к --split-days: дни уже лежат порознь (импорт разложил по датам), события нет.
    // Строки не двигаются; настройки дней не выравниваются — расхождения только печатаются.
    internal static async Task JoinDaysAsync(WebApplication app, string[] args)
    {
        var joinIndex = Array.IndexOf(args, "--join-days") + 1;
        var joinIds = joinIndex < args.Length
            ? args[joinIndex].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : 0).ToList()
            : [];
        if (joinIds.Count < 2 || joinIds.Contains(0))
        {
            Console.Error.WriteLine("Usage: dotnet run -- --join-days <id>,<id>[,<id>…] [--apply]");
            Environment.Exit(1);
            return;
        }

        var joinApply = args.Contains("--apply");
        using var scope = app.Services.CreateScope();
        var joiner = scope.ServiceProvider.GetRequiredService<ICompetitionDayJoinService>();
        var join = await joiner.JoinAsync(joinIds, joinApply);

        Console.WriteLine($"«{join.Name}», compID {join.OrgCompId?.ToString() ?? "—"}");
        foreach (var day in join.Days)
            Console.WriteLine($"  день {day.DayNumber} {day.Date:dd/MM/yyyy}: #{day.CompetitionId}, строк {day.Rows}" +
                              (day.OrgCompId is int org ? $", compID {org}" : ""));
        foreach (var diff in join.Differences)
            Console.WriteLine($"  ⚠ расходится {diff}");

        if (join.Refusal != null)
        {
            Console.WriteLine($"НЕ СОБИРАЕМ: {join.Refusal}");
            return;
        }
        if (!join.Applied)
        {
            Console.WriteLine("Ничего не записано (dry-run). Повторите с --apply.");
            return;
        }

        await scope.ServiceProvider.GetRequiredService<IAdminAuditService>().LogAsync(
            "competition.join-days", "CompetitionEvent", join.EventId!.Value.ToString(),
            $"Дни собраны в событие #{join.EventId} (compID {join.OrgCompId?.ToString() ?? "—"}): "
            + string.Join("; ", join.Days.Select(d => $"день {d.DayNumber} {d.Date:dd/MM/yyyy} #{d.CompetitionId} — {d.Rows} строк")));
        Console.WriteLine($"ПРИМЕНЕНО: событие #{join.EventId}, дни пересчитаны.");
        return;
    }

    // Обзор многодневок, лежащих не так, как плыли (docs/plans/multiday-split-plan.md, М4):
    //   dotnet run -- --collapsed-days [--pause-ms 1000]
    // Только чтение. Ходит в loglig по каждому однодневному старту с LogligId (с паузой);
    // схлопнутым прикладывает план --split-days, а «дни есть, события нет» ищет по базе.
    internal static async Task CollapsedDaysAsync(WebApplication app, string[] args)
    {
        var pauseIndex = Array.IndexOf(args, "--pause-ms") + 1;
        var pauseMs = pauseIndex > 0 && pauseIndex < args.Length && int.TryParse(args[pauseIndex], out var pm) ? pm : 1000;

        using var scope = app.Services.CreateScope();
        var review = await scope.ServiceProvider.GetRequiredService<ICollapsedDaysReviewService>()
            .ReviewAsync(TimeSpan.FromMilliseconds(pauseMs), line => Console.WriteLine($"  {line}"));

        Console.WriteLine($"\nСверено с сеткой loglig: {review.Checked}. Схлопнуты (сетка многодневная): {review.Collapsed.Count}");
        foreach (var c in review.Collapsed)
        {
            Console.WriteLine($"\n#{c.Split.CompetitionId} «{c.Split.Name}» — compID {c.OrgCompId}, loglig {c.LogligId}, " +
                              $"discovery #{c.DiscoveredId}; дни по сетке: {string.Join(", ", c.SourceDays.Select(d => d.ToString("dd/MM/yyyy")))}");
            foreach (var day in c.Split.Days)
                Console.WriteLine($"  день {day.DayNumber} {day.Date:dd/MM/yyyy}: строк {day.Rows}, медиа {day.Media}" +
                                  (day.Existing ? $" — остаётся #{day.CompetitionId}" : " — новый"));
            foreach (var (key, count) in c.Split.Unresolved)
                Console.WriteLine($"  без дня: {key} ×{count}");
            Console.WriteLine(c.Split.Refusal != null
                ? $"  --split-days НЕ поделит: {c.Split.Refusal}"
                : $"  лечится: --split-days {c.Split.CompetitionId} --apply");
        }

        Console.WriteLine($"\nДни есть, события нет: {review.DaysWithoutEvent.Count}");
        foreach (var g in review.DaysWithoutEvent)
        {
            Console.WriteLine($"  «{g.Name}» ({g.PoolType}): " + string.Join(", ", g.Days.Select(d =>
                $"#{d.CompetitionId} {d.Date:dd/MM/yyyy} — {d.Rows} строк" + (d.OrgCompId is int org ? $", compID {org}" : ""))));
            Console.WriteLine($"    лечится: --join-days {string.Join(",", g.Days.Select(d => d.CompetitionId))} --apply");
        }

        if (review.Failed.Count > 0)
        {
            Console.WriteLine($"\nСетку получить не удалось: {review.Failed.Count}");
            foreach (var f in review.Failed) Console.WriteLine($"  {f}");
        }
        return;
    }
}

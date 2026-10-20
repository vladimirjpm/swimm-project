using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Подсчёт сводки фоновых служб (docs/admin-pages/jobs.md) — один на страницу /Admin/Jobs и блок
/// «Jobs» дашборда. Чистые функции: карточки, статистика журнала, состояние процесса и время
/// приходят снаружи (<c>IBackgroundJobsStatus</c>), поэтому всё проверяется тестами без базы.
/// </summary>
public static class JobsOverviewBuilder
{
    /// <summary>
    /// «Просрочено» — пора уже дольше этого, а прогона нет. Фон смотрит журнал раз в 5–15 минут,
    /// так что полчаса — это точно не «ещё не дошла очередь».
    /// </summary>
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromMinutes(30);

    /// <summary>Работающее расписание, которому пора дольше <see cref="OverdueAfter"/>.</summary>
    public static bool IsOverdue(JobCard card, DateTime now) =>
        card.Kind == JobKinds.Schedule && card.Active
        && card.NextDueAt is { } at && now - at > OverdueAfter;

    /// <summary>
    /// Состояние расписания для карточки и списка служб: работает ли, упала ли последняя попытка,
    /// когда следующий раз, короткий статус и проблема.
    /// </summary>
    /// <param name="processOn">Фоновые расписания запущены в этом процессе (<c>BackgroundJobs:Enabled</c>).</param>
    /// <param name="enabled">Задача включена настройкой.</param>
    public static ScheduleState Schedule(
        bool processOn, bool enabled, string enabledKey, JobRunStateRow? state, TimeSpan interval, DateTime now)
    {
        var failed = state?.LastAttemptAt is DateTime attempt
                     && (state.LastSuccessAt is null || attempt > state.LastSuccessAt.Value);
        var next = state is null ? null
            : JobSchedule.NextDueAt(state.LastSuccessAt, state.LastAttemptAt, interval, JobSchedule.RetryAfterFailure);
        var active = processOn && enabled;
        var overdue = active && next is { } due && now - due > OverdueAfter;

        string? lastRun = state?.LastAttemptAt is { } last
            ? failed
                ? $"ошибка {JobTimeText.Ago(last, now)}" + (state.LastError is { } error ? $": {Clip(error, 80)}" : "")
                : $"ок {JobTimeText.Ago(last, now)}"
            : null;

        var status = !processOn ? "выключена флагом процесса (BackgroundJobs:Enabled)" + (lastRun is null ? "" : $"; последний — {lastRun}")
            : !enabled ? $"выключена настройкой ({enabledKey})" + (lastRun is null ? "" : $"; последний — {lastRun}")
            : overdue ? $"просрочена на {JobTimeText.Human(now - next!.Value)}" + (lastRun is null ? "" : $"; последний — {lastRun}")
            : lastRun ?? "ещё не запускалась";

        // Проблема — только у работающей задачи: старая ошибка выключенной ничего не ломает сейчас.
        var alert = active && failed ? "danger" : overdue ? "warning" : null;
        return new ScheduleState(active, failed, next, status, alert);
    }

    /// <summary>Сводка: плитки /Admin/Jobs, проблемные службы и худший тон — для блока дашборда.</summary>
    public static JobsSummary Build(IReadOnlyList<JobCard> cards, JobRunStats stats, JobsInfraState infra, DateTime now)
    {
        var schedules = cards.Where(c => c.Kind == JobKinds.Schedule).ToList();
        var running = schedules.Where(c => c.Active).ToList();
        var failed = schedules.Where(c => c.Failed).ToList();
        var overdue = running.Where(c => IsOverdue(c, now)).ToList();
        var next = running.Where(c => c.NextDueAt is { } at && at > now).MinBy(c => c.NextDueAt);

        var problems = cards
            .Where(c => c.Alert is not null)
            .OrderBy(c => c.Alert == "danger" ? 0 : 1)
            .Select(c => new JobProblem(c.Key, c.Title, c.Status, c.Alert!))
            .ToList();
        var tone = problems.Any(p => p.Tone == "danger") ? "danger" : problems.Count > 0 ? "warning" : "ok";

        var active = cards.Count(c => c.Active);
        var sync = infra.Sync;
        var bridge = infra.Bridge;

        List<OverviewTile> tiles =
        [
            new("services", "Службы", $"{active} / {cards.Count}",
                $"работают здесь; выключено {cards.Count - active} (флаги процесса или настройка)",
                problems.Count == 0 ? "neutral" : tone, "#services-list"),
            new("failed", "Расписания с ошибкой", failed.Count.ToString(),
                failed.Count == 0 ? "последние попытки удались" : string.Join(", ", failed.Select(c => c.Title)),
                failed.Count == 0 ? "ok" : "danger", failed.Count == 0 ? "#schedules" : $"#job-{failed[0].Key}"),
            new("overdue", "Просрочены", running.Count == 0 ? "—" : overdue.Count.ToString(),
                running.Count == 0 ? "расписания здесь не работают"
                : overdue.Count == 0 ? $"пора дольше {JobTimeText.Human(OverdueAfter)} — ни одного" : string.Join(", ", overdue.Select(c => c.Title)),
                running.Count == 0 ? "neutral" : overdue.Count == 0 ? "ok" : "warning",
                overdue.Count == 0 ? "#schedules" : $"#job-{overdue[0].Key}"),
            new("next", "Следующий запуск", next is null ? "—" : JobTimeText.Until(next.NextDueAt!.Value, now),
                next is not null ? next.Title
                : running.Count == 0 ? "расписания здесь не работают" : "пора — при ближайшей проверке",
                "neutral", next is null ? "#schedules" : $"#job-{next.Key}"),
            new("runs24h", "Прогонов за 24 ч", stats.Runs24h.ToString(),
                $"успешных {stats.Runs24h - stats.Failed24h}, с ошибкой {stats.Failed24h}",
                stats.Failed24h > 0 ? "danger" : "neutral", "#schedules"),
            new("runs7d", "Прогонов за 7 дней", stats.Runs7d.ToString(),
                $"успешных {stats.Runs7d - stats.Failed7d}, с ошибкой {stats.Failed7d}",
                stats.Failed7d > 0 ? "warning" : "neutral", "#schedules"),
            new("queues", "Очереди", $"{infra.Queued} · {infra.InProgress}",
                infra.AdminEnabled ? "в очереди · идёт сейчас" : "очередей здесь нет (Admin:Enabled)",
                infra.InProgress > 0 ? "info" : "neutral", "#queues"),
            new("db", "База", infra.DbOk is null ? "—" : infra.DbOk.Value ? "доступна" : "не отвечает",
                infra.DbPingAt is { } pingAt ? $"пинг {JobTimeText.Ago(pingAt, now)}" : "ждёт первого пинга",
                infra.DbOk is null ? "neutral" : infra.DbOk.Value ? "ok" : "danger", "#job-db-ping"),
            new("sync", "Локальная vs прод",
                sync is null ? "—" : sync.Error is not null ? "?" : sync.Behind ? "отстаёт" : "в синхроне",
                sync is null ? (infra.IsDevelopment ? "ещё не сверяла" : "сверяется только в разработке")
                : sync.Error is not null ? "прод недоступен"
                : $"прод v{sync.ProdVersion}, локальная v{sync.SyncedFromVersion?.ToString() ?? "—"}",
                sync is null ? "neutral" : sync.Error is not null || sync.Behind ? "warning" : "ok", "#job-data-sync-check"),
            new("bridge", "Мост кэша", !bridge.Enabled ? "выкл" : bridge.Listening ? "слушает" : "не слушает",
                $"принято {bridge.Received}, отправлено {bridge.Published}",
                !bridge.Enabled ? "neutral" : bridge.Listening ? "ok" : "danger", "#job-cache-bridge"),
        ];

        return new JobsSummary(cards.Count, active, problems, failed.Count, overdue.Count, stats,
            next?.Title, next?.NextDueAt, tone, tiles);
    }

    private static string Clip(string text, int max) => text.Length > max ? text[..max] + "…" : text;
}

/// <param name="Alert">danger — последняя попытка упала; warning — просрочена; null — в порядке.</param>
public sealed record ScheduleState(bool Active, bool Failed, DateTime? NextDueAt, string Status, string? Alert);

/// <summary>Время по-человечески для /Admin/Jobs: «3 ч назад», «через 20 мин», «20.10.2026 10:32».</summary>
public static class JobTimeText
{
    /// <summary>Локальное время сервера (у Влада — Израиль), как у остальных страниц админки.</summary>
    public static string Format(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public static string When(DateTime? utc, DateTime now) => utc is { } at ? $"{Format(at)} ({Ago(at, now)})" : "не было";

    public static string Ago(DateTime utc, DateTime now)
    {
        var d = now - DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return d < TimeSpan.Zero ? "только что" : Human(d) + " назад";
    }

    public static string Until(DateTime utc, DateTime now) => "через " + Human(DateTime.SpecifyKind(utc, DateTimeKind.Utc) - now);

    public static string Human(TimeSpan d) =>
        d.TotalMinutes < 1 ? $"{Math.Max(0, (int)d.TotalSeconds)} с"
        : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} мин"
        : d.TotalDays < 2 ? (d.Minutes == 0 || d.TotalHours >= 10 ? $"{(int)d.TotalHours} ч" : $"{(int)d.TotalHours} ч {d.Minutes} мин")
        : $"{(int)d.TotalDays} дн.";

    public static string Duration(DateTime started, DateTime finished)
    {
        var d = finished - started;
        return d.TotalSeconds < 1 ? "<1 с" : d.TotalMinutes < 1 ? $"{(int)d.TotalSeconds} с" : Human(d);
    }

    public static string Trigger(string trigger) => trigger switch
    {
        JobTriggers.Schedule => "фон",
        JobTriggers.Manual => "кнопка",
        JobTriggers.Cli => "CLI",
        _ => trigger,
    };
}

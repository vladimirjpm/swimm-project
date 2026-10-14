using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Swimm.Infrastructure.Data;

namespace Swimm.API.Hosting;

/// <summary>
/// Сторож режима ProdAdmin (docs/plans/azure-deploy-plan.md §0-тер, п. 2): локальная админка
/// пишет в прод-БД, поэтому код обязан совпадать со схемой прода. Запуск не из master — это и
/// есть «код разошёлся со схемой»: миграция ветки, которой на проде нет (или наоборот), и
/// EF начнёт писать в колонки, которых нет, или молча терять новые.
///
/// Проверка — по факту, а не по имени ветки: список миграций в коде против
/// <c>__EFMigrationsHistory</c> прода. Расхождение — отказ стартовать. Ветка — только
/// предупреждение и надпись на красной полосе: хотфикс из ветки без миграций схеме не вредит.
/// </summary>
public static class ProdAdminGuard
{
    /// <summary>Что видно на красной полосе админки: куда подключены и с какой ветки.</summary>
    public sealed record Info(string DatabaseHost, string DatabaseName, string? GitBranch);

    /// <summary>
    /// Отказ стартовать (исключение), если ProdAdmin смотрит не в прод (localhost — забыт
    /// appsettings.ProdAdmin.Local.json) или если миграции кода и прода расходятся. Ветка не
    /// master — громкое предупреждение, не отказ.
    /// </summary>
    public static async Task EnsureSafeToStartAsync(WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Swimm.ProdAdmin");
        var info = Describe(app.Configuration, app.Environment);

        if (IsLocalHost(info.DatabaseHost))
            throw new InvalidOperationException(
                $"ProdAdmin смотрит в локальную базу ({info.DatabaseHost}). Строки подключения к проду — " +
                "в server/Swimm.API/appsettings.ProdAdmin.Local.json (образец — *.example рядом). " +
                "Для работы с локальной базой запускай обычный профиль (Development).");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
            var mismatch = await CheckSchemaAsync(db);
            if (mismatch is not null)
                throw new InvalidOperationException(
                    $"ProdAdmin: код и схема прод-БД ({info.DatabaseHost}) расходятся — {mismatch}. Запускать только из master.");
        }

        if (info.GitBranch is not null && info.GitBranch != "master")
            log.LogWarning(
                "ProdAdmin запущен с ветки {Branch}, а не с master. Схема совпадает, но код, которого нет в master, " +
                "пишет в прод-данные. Так и задумано — продолжай; нет — переключись на master.", info.GitBranch);

        log.LogWarning("ProdAdmin: админка пишет в ПРОД-БД {Host}/{Database}, ветка {Branch}.",
            info.DatabaseHost, info.DatabaseName, info.GitBranch ?? "?");
    }

    internal static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host == "127.0.0.1" || host == "::1" || host == "?";

    /// <summary>
    /// Сверить миграции кода и прод-БД. Пусто — всё сходится; иначе текст для лога и отказа.
    /// <c>--migrate</c> этим сторожем не проверяется (CLI выполняется раньше): применить
    /// недостающие миграции на проде — ровно его работа.
    /// </summary>
    public static async Task<string?> CheckSchemaAsync(SwimmDbContext db, CancellationToken ct = default)
    {
        var inCode = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        return DescribeMismatch(inCode, applied);
    }

    /// <summary>Чистая часть сверки — для тестов.</summary>
    internal static string? DescribeMismatch(IReadOnlyCollection<string> inCode, IReadOnlyCollection<string> applied)
    {
        var codeAhead = inCode.Except(applied, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var dbAhead = applied.Except(inCode, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (codeAhead.Count == 0 && dbAhead.Count == 0) return null;

        var parts = new List<string>();
        if (codeAhead.Count > 0)
            parts.Add($"в коде есть миграции, которых нет в прод-БД ({codeAhead.Count}: {string.Join(", ", codeAhead.TakeLast(3))}) — " +
                      "либо ветка не master, либо прод ещё не мигрирован (--migrate в ProdAdmin)");
        if (dbAhead.Count > 0)
            parts.Add($"в прод-БД есть миграции, которых нет в коде ({dbAhead.Count}: {string.Join(", ", dbAhead.TakeLast(3))}) — " +
                      "код старее прода: git pull в master");
        return string.Join("; ", parts);
    }

    public static Info Describe(IConfiguration config, IHostEnvironment env)
    {
        var cs = config.GetConnectionString("AdminConnection") ?? config.GetConnectionString("DefaultConnection");
        string host = "?", database = "?";
        try
        {
            var b = new NpgsqlConnectionStringBuilder(cs);
            host = b.Host ?? "?";
            database = b.Database ?? "?";
        }
        catch (ArgumentException) { /* битая строка — всплывёт на первом запросе */ }

        return new Info(host, database, ReadGitBranch(env.ContentRootPath));
    }

    /// <summary>
    /// Текущая ветка из <c>.git/HEAD</c> (ищем вверх от ContentRoot). Worktree: <c>.git</c> —
    /// файл со ссылкой <c>gitdir: …</c>. Отсоединённый HEAD — короткий хэш. Нет репозитория — null.
    /// </summary>
    internal static string? ReadGitBranch(string startDir)
    {
        for (var dir = new DirectoryInfo(startDir); dir is not null; dir = dir.Parent)
        {
            var dotGit = Path.Combine(dir.FullName, ".git");
            string? headFile = null;
            if (Directory.Exists(dotGit))
                headFile = Path.Combine(dotGit, "HEAD");
            else if (File.Exists(dotGit))
            {
                var line = File.ReadAllText(dotGit).Trim();
                if (line.StartsWith("gitdir:", StringComparison.Ordinal))
                {
                    var gitDir = line["gitdir:".Length..].Trim();
                    if (!Path.IsPathRooted(gitDir)) gitDir = Path.Combine(dir.FullName, gitDir);
                    headFile = Path.Combine(gitDir, "HEAD");
                }
            }
            if (headFile is null) continue;
            if (!File.Exists(headFile)) return null;
            return ParseHead(File.ReadAllText(headFile));
        }
        return null;
    }

    internal static string? ParseHead(string head)
    {
        head = head.Trim();
        const string prefix = "ref: refs/heads/";
        if (head.StartsWith(prefix, StringComparison.Ordinal)) return head[prefix.Length..];
        return head.Length >= 8 ? head[..8] : (head.Length > 0 ? head : null);
    }
}

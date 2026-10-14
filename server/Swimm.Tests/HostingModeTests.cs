using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Swimm.API.Hosting;
using Swimm.Application.Abstractions;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Форма процесса после В17 (docs/plans/azure-deploy-plan.md §0-тер): на проде админка и фоновые
/// джобы выключены, локальная админка (ProdAdmin) стартует только со схемой, совпадающей с продом.
/// Хост здесь не поднимается — проверяются решения, на которых он строится.
/// </summary>
public class HostingModeTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    // ── Выключатели ──────────────────────────────────────────────────────────

    [Fact]
    public void Flags_DefaultOn_SoDevelopmentIsUnchanged()
    {
        var cfg = Config();
        Assert.True(HostingMode.AdminEnabled(cfg));
        Assert.True(HostingMode.BackgroundJobsEnabled(cfg));
    }

    [Fact]
    public void Flags_TurnOffByConfig()
    {
        var cfg = Config(("Admin:Enabled", "false"), ("BackgroundJobs:Enabled", "false"));
        Assert.False(HostingMode.AdminEnabled(cfg));
        Assert.False(HostingMode.BackgroundJobsEnabled(cfg));
    }

    [Fact]
    public void ProductionConfig_ShipsWithAdminAndJobsOff()
    {
        // Выключатель приезжает с кодом: appsettings.Production.json закоммичен и едет в publish.
        var path = Path.Combine(RepoRoot(), "server", "Swimm.API", "appsettings.Production.json");
        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();

        Assert.False(HostingMode.AdminEnabled(cfg));
        Assert.False(HostingMode.BackgroundJobsEnabled(cfg));
    }

    [Theory]
    [InlineData("/Admin", true)]
    [InlineData("/admin/Settings", true)]
    [InlineData("/ADMIN/Cache", true)]
    [InlineData("/api/admin/settings", true)]
    [InlineData("/API/Admin/records/check-all", true)]
    [InlineData("/admin-home.html", true)]
    [InlineData("/js/admin/import.js", true)]
    [InlineData("/administrator", false)]
    [InlineData("/api/admins", false)]
    [InlineData("/api/swimmers/5", false)]
    [InlineData("/competitions/1581", false)]
    [InlineData("/", false)]
    [InlineData("/home.html", false)]
    public void IsAdminPath(string path, bool expected) =>
        Assert.Equal(expected, HostingMode.IsAdminPath(new PathString(path)));

    // ── Сторож ProdAdmin ─────────────────────────────────────────────────────

    [Fact]
    public void SchemaCheck_SameMigrations_IsFine() =>
        Assert.Null(ProdAdminGuard.DescribeMismatch(["m1", "m2"], ["m2", "m1"]));

    [Fact]
    public void SchemaCheck_CodeAhead_NamesTheMigration()
    {
        var msg = ProdAdminGuard.DescribeMismatch(["m1", "m2", "m3_branch"], ["m1", "m2"]);
        Assert.NotNull(msg);
        Assert.Contains("m3_branch", msg);
        Assert.Contains("--migrate", msg);
    }

    [Fact]
    public void SchemaCheck_DatabaseAhead_SaysPull()
    {
        var msg = ProdAdminGuard.DescribeMismatch(["m1"], ["m1", "m2_master"]);
        Assert.NotNull(msg);
        Assert.Contains("m2_master", msg);
        Assert.Contains("git pull", msg);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("?", true)]   // строка не разобралась — тоже не прод
    [InlineData("swimhub-pg.postgres.database.azure.com", false)]
    public void LocalHost_IsNotProd(string host, bool local) =>
        Assert.Equal(local, ProdAdminGuard.IsLocalHost(host));

    [Theory]
    [InlineData("ref: refs/heads/master\n", "master")]
    [InlineData("ref: refs/heads/feature/x", "feature/x")]
    [InlineData("05ddaa32b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6", "05ddaa32")]
    [InlineData("", null)]
    public void ParseHead(string head, string? branch) =>
        Assert.Equal(branch, ProdAdminGuard.ParseHead(head));

    [Fact]
    public void ReadGitBranch_FindsHeadUpwards_AndFollowsWorktreeFile()
    {
        var root = Directory.CreateTempSubdirectory("swimm-git-").FullName;
        try
        {
            // обычный клон: .git — папка
            Directory.CreateDirectory(Path.Combine(root, "repo", ".git"));
            File.WriteAllText(Path.Combine(root, "repo", ".git", "HEAD"), "ref: refs/heads/master\n");
            var deep = Directory.CreateDirectory(Path.Combine(root, "repo", "server", "Swimm.API")).FullName;
            Assert.Equal("master", ProdAdminGuard.ReadGitBranch(deep));

            // worktree: .git — файл со ссылкой
            var gitDir = Directory.CreateDirectory(Path.Combine(root, "repo", ".git", "worktrees", "wt")).FullName;
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/entity-hero-roles\n");
            var wt = Directory.CreateDirectory(Path.Combine(root, "wt")).FullName;
            File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {gitDir}\n");
            Assert.Equal("entity-hero-roles", ProdAdminGuard.ReadGitBranch(wt));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ── Отставание локальной копии ───────────────────────────────────────────

    private static DataSyncState Sync(long? prod, long? local, string? error = null) =>
        new(prod, "Results", new DateTimeOffset(2026, 10, 14, 18, 3, 0, TimeSpan.Zero), local,
            local is null ? null : new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), error, DateTimeOffset.UtcNow);

    [Fact]
    public void Sync_Behind_WarnsWithBothVersionsAndScript()
    {
        var w = Sync(1250, 1234).Warning();
        Assert.NotNull(w);
        Assert.Contains("v1250", w);
        Assert.Contains("v1234", w);
        Assert.Contains("Results", w);
        Assert.Contains("sync-from-prod.sh", w);
    }

    [Fact]
    public void Sync_UpToDate_IsSilent() => Assert.Null(Sync(1250, 1250).Warning());

    [Fact]
    public void Sync_NeverSynced_IsBehind() => Assert.True(Sync(10, null).Behind);

    [Fact]
    public void Sync_ProdUnreachable_DoesNotScare()
    {
        // Файрвол Azure, нет сети — «не знаем» не равно «отстаёт».
        var s = Sync(null, 5, "прод: timeout");
        Assert.False(s.Behind);
        Assert.Null(s.Warning());
    }

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "server", "db", "seed-tables.txt"))) return dir.FullName;
        throw new InvalidOperationException("Корень репозитория не найден");
    }
}

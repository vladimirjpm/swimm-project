using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Swimm.API";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static readonly IHostEnvironment Development = new Env(Environments.Development);
    private static readonly IHostEnvironment Production = new Env(Environments.Production);
    private static readonly IHostEnvironment ProdAdmin = new Env(HostingMode.ProdAdminEnvironment);

    [Fact]
    public void Admin_DefaultOn()
    {
        Assert.True(HostingMode.AdminEnabled(Config()));
    }

    [Fact]
    public void Jobs_WithoutKey_OffInDevelopment_OnElsewhere()
    {
        // В10: локальный API по таймеру наружу не ходит, если явно не попросить.
        var cfg = Config();
        Assert.False(HostingMode.BackgroundJobsEnabled(cfg, Development));
        Assert.True(HostingMode.BackgroundJobsEnabled(cfg, Production));
        Assert.True(HostingMode.BackgroundJobsEnabled(cfg, ProdAdmin));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Jobs_ExplicitKey_WinsInAnyEnvironment(string value, bool expected)
    {
        var cfg = Config(("BackgroundJobs:Enabled", value));
        Assert.Equal(expected, HostingMode.BackgroundJobsEnabled(cfg, Development));
        Assert.Equal(expected, HostingMode.BackgroundJobsEnabled(cfg, Production));
        Assert.Equal(expected, HostingMode.BackgroundJobsEnabled(cfg, ProdAdmin));
    }

    // ── Телеметрия (docs/plans/application-insights-plan.md, А2.2) ───────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Telemetry_WithoutConnectionString_Off(string? value)
    {
        // Разработка, тесты и CI ничего не шлют: строки подключения там нет.
        var cfg = value is null ? Config() : Config((HostingMode.TelemetryConnectionStringKey, value));
        Assert.False(HostingMode.TelemetryEnabled(cfg));
    }

    [Fact]
    public void Telemetry_WithConnectionString_On()
    {
        var cfg = Config((HostingMode.TelemetryConnectionStringKey, "InstrumentationKey=00000000-0000-0000-0000-000000000000"));
        Assert.True(HostingMode.TelemetryEnabled(cfg));
    }

    [Fact]
    public void Telemetry_KillSwitch_WinsOverConnectionString()
    {
        var cfg = Config(
            (HostingMode.TelemetryConnectionStringKey, "InstrumentationKey=00000000-0000-0000-0000-000000000000"),
            (HostingMode.TelemetryEnabledKey, "false"));
        Assert.False(HostingMode.TelemetryEnabled(cfg));
    }

    [Fact]
    public void Telemetry_RoleName_PerEnvironment()
    {
        var cfg = Config();
        Assert.Equal("swimhub-web", Swimm.API.Telemetry.TelemetryRoleName.Resolve(cfg, Production));
        Assert.Equal("swimhub-prodadmin", Swimm.API.Telemetry.TelemetryRoleName.Resolve(cfg, ProdAdmin));
        Assert.Equal("swimhub-dev", Swimm.API.Telemetry.TelemetryRoleName.Resolve(cfg, Development));
        Assert.Equal("custom", Swimm.API.Telemetry.TelemetryRoleName.Resolve(Config(("Telemetry:RoleName", " custom ")), Production));
    }

    [Fact]
    public void Flags_TurnOffByConfig()
    {
        var cfg = Config(("Admin:Enabled", "false"), ("BackgroundJobs:Enabled", "false"));
        Assert.False(HostingMode.AdminEnabled(cfg));
        Assert.False(HostingMode.BackgroundJobsEnabled(cfg, ProdAdmin));
    }

    [Fact]
    public void ProductionConfig_ShipsWithAdminAndJobsOff()
    {
        // Выключатель приезжает с кодом: appsettings.Production.json закоммичен и едет в publish.
        // Без админки нет и очередей (они регистрируются под AdminEnabled), без флага — расписаний.
        var path = Path.Combine(RepoRoot(), "server", "Swimm.API", "appsettings.Production.json");
        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();

        Assert.False(HostingMode.AdminEnabled(cfg));
        Assert.False(HostingMode.BackgroundJobsEnabled(cfg, Production));
    }

    // ── Прод-расписание забора (prod-auto-pull-plan §4.2, П2) ────────────────

    [Fact]
    public void AutoPull_DefaultOff_ExplicitKeyOn()
    {
        Assert.False(HostingMode.AutoPullEnabled(Config()));
        Assert.True(HostingMode.AutoPullEnabled(Config(("AutoPull:Enabled", "true"))));
        Assert.False(HostingMode.AutoPullEnabled(Config(("AutoPull:Enabled", "false"))));
    }

    [Fact]
    public void ProductionConfig_ShipsWithAutoPullOn_AfterNetworkProbe()
    {
        // П0 пройдена 20.11.2026 (с App Service loglig.com:2053 и isr.org.il отвечают 200) — флаг включён.
        // Аварийное выключение — та же одна строка в файле; тест тогда меняется вместе с ней.
        var path = Path.Combine(RepoRoot(), "server", "Swimm.API", "appsettings.Production.json");
        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();

        Assert.True(HostingMode.AutoPullEnabled(cfg));
        Assert.True(HostingMode.DiscoveryScheduleEnabled(cfg, Production));
        // ProdAdmin читает тот же файл: прод забирает сам → своё расписание этих задач ProdAdmin снимает (Р-6).
        Assert.True(HostingMode.ProdAutoPullEnabled(Path.Combine(RepoRoot(), "server", "Swimm.API")));
    }

    [Fact]
    public void ProdAdminConfig_DoesNotTurnAutoPullOnItself()
    {
        var path = Path.Combine(RepoRoot(), "server", "Swimm.API", "appsettings.ProdAdmin.json");
        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();
        Assert.False(HostingMode.AutoPullEnabled(cfg));
    }

    [Theory]
    // backgroundJobs, autoPull, isProdAdmin, prodAutoPull → расписание discovery+start-list здесь
    [InlineData(false, false, false, false, false)] // прод сейчас (флаг выкл) — как до П2: нет
    [InlineData(false, true, false, false, true)]   // прод после П0 (AutoPull:Enabled=true) — да
    [InlineData(true, false, true, false, true)]    // ProdAdmin, прод не забирает — как до П2: да
    [InlineData(true, false, true, true, false)]    // ProdAdmin, прод забирает (Р-6) — нет, одна точка запуска
    [InlineData(false, false, true, true, false)]   // ProdAdmin без расписаний вообще — нет
    [InlineData(false, false, false, true, false)]  // разработка: флаг прода сюда не относится
    [InlineData(true, false, false, true, true)]    // разработка с BackgroundJobs:Enabled=true — как раньше
    public void DiscoverySchedule_RunsInExactlyOnePlace(
        bool backgroundJobs, bool autoPull, bool isProdAdmin, bool prodAutoPull, bool expected) =>
        Assert.Equal(expected, HostingMode.DiscoveryScheduleEnabled(backgroundJobs, autoPull, isProdAdmin, prodAutoPull));

    [Fact]
    public void ProdAdmin_ReadsProdAutoPullFromProductionFile()
    {
        var dir = Directory.CreateTempSubdirectory("utest-autopull-");
        try
        {
            var env = new Env(HostingMode.ProdAdminEnvironment) { ContentRootPath = dir.FullName };
            var cfg = Config(("BackgroundJobs:Enabled", "true"));

            // Файла нет — прод не забирает, ProdAdmin как раньше.
            Assert.False(HostingMode.ProdAutoPullEnabled(dir.FullName));
            Assert.True(HostingMode.DiscoveryScheduleEnabled(cfg, env));

            // Прод включили одной строкой — ProdAdmin своё расписание снимает (Р-6)…
            File.WriteAllText(Path.Combine(dir.FullName, HostingMode.ProductionSettingsFile),
                """{ "AutoPull": { "Enabled": true } }""");
            Assert.True(HostingMode.ProdAutoPullEnabled(dir.FullName));
            Assert.False(HostingMode.DiscoveryScheduleEnabled(cfg, env));
            // …а остальные его расписания живут под тем же BackgroundJobs:Enabled.
            Assert.True(HostingMode.BackgroundJobsEnabled(cfg, env));

            // Битый файл — не повод уронить старт: как будто флага нет.
            File.WriteAllText(Path.Combine(dir.FullName, HostingMode.ProductionSettingsFile), "{ не json");
            Assert.False(HostingMode.ProdAutoPullEnabled(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ProdAdminConfig_ShipsWithAdminAndJobsOn()
    {
        // ProdAdmin — единственное место, где расписания ходят наружу (В12): и очереди, и расписания.
        var path = Path.Combine(RepoRoot(), "server", "Swimm.API", "appsettings.ProdAdmin.json");
        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();

        Assert.True(HostingMode.AdminEnabled(cfg));
        Assert.True(HostingMode.BackgroundJobsEnabled(cfg, ProdAdmin));
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

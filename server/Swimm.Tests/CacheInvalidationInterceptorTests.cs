using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Domain.Entities;
using Swimm.Infrastructure;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сброс кэша сохранением EF (docs/plans/cache-tags-plan.md, К4): SaveChanges сбрасывает метки
/// изменённых таблиц сам, без ручного вызова, — и только ПОСЛЕ коммита транзакции.
///
/// Логика — на пробном контексте из двух таблиц: видно, что сброс роняет ровно метку
/// изменённой. Транзакции InMemory не ведёт — их проверяем на живом Postgres, во ВРЕМЕННЫХ
/// таблицах сессии теста: живые данные не трогаются; нет базы — пропуск.
/// </summary>
public class CacheInvalidationInterceptorTests
{
    private const string PgConn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    public sealed class ProbeA { public int Id { get; set; } public string V { get; set; } = ""; }
    public sealed class ProbeB { public int Id { get; set; } public string V { get; set; } = ""; }

    public sealed record Page(string Name);

    private sealed class ProbeDb(DbContextOptions<ProbeDb> options) : DbContext(options)
    {
        public DbSet<ProbeA> A => Set<ProbeA>();
        public DbSet<ProbeB> B => Set<ProbeB>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<ProbeA>(e => { e.ToTable("cache_k4_probe_a"); e.Property(p => p.Id).ValueGeneratedNever(); });
            b.Entity<ProbeB>(e => { e.ToTable("cache_k4_probe_b"); e.Property(p => p.Id).ValueGeneratedNever(); });
        }
    }

    private static readonly string TagA = CacheTags.Table("cache_k4_probe_a");
    private static readonly string TagB = CacheTags.Table("cache_k4_probe_b");

    private static MemoryCacheService NewCache() => new(new MemoryCache(new MemoryCacheOptions()));

    /// <summary>Две «витрины»: одна собрана из таблицы A, другая из B.</summary>
    private static async Task SeedPages(ICacheService cache)
    {
        await cache.SetAsync("page-a", new Page("a"), TimeSpan.FromMinutes(5), TagA);
        await cache.SetAsync("page-b", new Page("b"), TimeSpan.FromMinutes(5), TagB);
    }

    private static async Task<bool> Cached(ICacheService cache, string key) =>
        await cache.GetAsync<Page>(key) is not null;

    private static ProbeDb InMemory(ICacheService cache, string name) =>
        new(new DbContextOptionsBuilder<ProbeDb>()
            .UseInMemoryDatabase(name)
            .AddInterceptors(new CacheInvalidationInterceptor(cache))
            .Options);

    /// <summary>
    /// Пробный контекст на живом Postgres. Соединение открыто на всё время контекста: временные
    /// таблицы живут в сессии, и все команды теста обязаны идти через неё.
    /// </summary>
    private static ProbeDb? TryPg(ICacheService cache)
    {
        var db = new ProbeDb(new DbContextOptionsBuilder<ProbeDb>()
            .UseNpgsql(PgConn)
            .AddInterceptors(new CacheInvalidationInterceptor(cache))
            .Options);
        try
        {
            db.Database.OpenConnection();
            db.Database.ExecuteSqlRaw("""
                CREATE TEMP TABLE cache_k4_probe_a ("Id" int PRIMARY KEY, "V" text NOT NULL);
                CREATE TEMP TABLE cache_k4_probe_b ("Id" int PRIMARY KEY, "V" text NOT NULL);
                """);
            return db;
        }
        catch
        {
            db.Dispose();
            return null; // нет базы — пропуск
        }
    }

    // ── Без транзакции (InMemory) ──────────────────────────────────────────────────

    [Fact]
    public async Task Save_DropsTheChangedTable_KeepsTheOther()
    {
        var cache = NewCache();
        await SeedPages(cache);
        await using var db = InMemory(cache, nameof(Save_DropsTheChangedTable_KeepsTheOther));

        db.A.Add(new ProbeA { Id = 1, V = "x" });
        await db.SaveChangesAsync(); // ручного сброса нет — его делает перехватчик

        Assert.False(await Cached(cache, "page-a"));
        Assert.True(await Cached(cache, "page-b"));
    }

    [Fact]
    public async Task EditOfTrackedEntity_WithoutUpdateCall_IsSeen()
    {
        var cache = NewCache();
        await using var db = InMemory(cache, nameof(EditOfTrackedEntity_WithoutUpdateCall_IsSeen));
        var row = new ProbeA { Id = 1, V = "x" };
        db.A.Add(row);
        await db.SaveChangesAsync();
        await SeedPages(cache);

        row.V = "y"; // правка отслеживаемого объекта, без db.Update — так пишет большинство кода
        await db.SaveChangesAsync();

        Assert.False(await Cached(cache, "page-a"));
        Assert.True(await Cached(cache, "page-b"));
    }

    [Fact]
    public async Task NothingChanged_DropsNothing()
    {
        var cache = NewCache();
        await SeedPages(cache);
        await using var db = InMemory(cache, nameof(NothingChanged_DropsNothing));

        await db.SaveChangesAsync();

        Assert.True(await Cached(cache, "page-a"));
        Assert.True(await Cached(cache, "page-b"));
    }

    [Fact]
    public async Task FailedSave_DropsNothing_AndItsTagsDoNotLeakIntoTheNextSave()
    {
        var cache = NewCache();
        await SeedPages(cache);
        await using var db = InMemory(cache, nameof(FailedSave_DropsNothing_AndItsTagsDoNotLeakIntoTheNextSave));

        db.B.Remove(new ProbeB { Id = 42 }); // такой строки нет — сохранение падает
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        Assert.True(await Cached(cache, "page-b"));

        db.ChangeTracker.Clear();
        db.A.Add(new ProbeA { Id = 1, V = "x" });
        await db.SaveChangesAsync();

        Assert.False(await Cached(cache, "page-a"));
        Assert.True(await Cached(cache, "page-b")); // метка упавшего сохранения не прилипла к следующему
    }

    [Fact]
    public async Task RealModel_GroupSave_DropsGroupsTag_KeepsResultsTag()
    {
        var cache = NewCache();
        await cache.SetAsync("group-page", new Page("g"), TimeSpan.FromMinutes(5), CacheTags.Table("HubGroups"));
        await cache.SetAsync("season-best", new Page("sb"), TimeSpan.FromMinutes(5), CacheTags.Table("Results"));
        await using var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(nameof(RealModel_GroupSave_DropsGroupsTag_KeepsResultsTag))
            .AddInterceptors(new CacheInvalidationInterceptor(cache))
            .Options);

        db.HubGroups.Add(new HubGroup { Name = "Test", Slug = "test" });
        await db.SaveChangesAsync();

        Assert.False(await Cached(cache, "group-page"));
        Assert.True(await Cached(cache, "season-best")); // приёмка плана: группа не роняет season-best
    }

    [Fact]
    public async Task RealModel_GroupDelete_AlsoDropsTablesTheDatabaseCascadesInto()
    {
        var cache = NewCache();
        await using var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(nameof(RealModel_GroupDelete_AlsoDropsTablesTheDatabaseCascadesInto))
            .AddInterceptors(new CacheInvalidationInterceptor(cache))
            .Options);
        var group = new HubGroup
        {
            Name = "Test", Slug = "test",
            Owner = new AppUser { Email = "o@x.com", DisplayName = "O", SecurityStamp = "s" },
        };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Витрины из таблиц, куда удаление группы каскадит в БАЗЕ (строки трекер не загружал):
        // состав напрямую, результаты тренировок — через сессию тренировки (цепочка каскада).
        await cache.SetAsync("roster", new Page("r"), TimeSpan.FromMinutes(5), CacheTags.Table("HubGroupMembers"));
        await cache.SetAsync("trainings", new Page("t"), TimeSpan.FromMinutes(5), CacheTags.Table("Sys_TrainingResults"));
        await cache.SetAsync("season-best", new Page("sb"), TimeSpan.FromMinutes(5), CacheTags.Table("Results"));

        db.HubGroups.Remove(new HubGroup { Id = group.Id });
        await db.SaveChangesAsync();

        Assert.False(await Cached(cache, "roster"));
        Assert.False(await Cached(cache, "trainings"));
        Assert.True(await Cached(cache, "season-best"));
    }

    // ── Подпись для журнала сбросов (К4б.1) ───────────────────────────────────────

    [Fact]
    public async Task Journal_SignsTheSave_WithTableAndChangedColumns()
    {
        var cache = NewCache();
        await using var db = InMemory(cache, nameof(Journal_SignsTheSave_WithTableAndChangedColumns));
        var row = new ProbeA { Id = 1, V = "x" };
        db.A.Add(row);
        await db.SaveChangesAsync(); // кэш пуст — сброс ничего не выкинул, в журнал не попал
        await SeedPages(cache);

        row.V = "y";
        await db.SaveChangesAsync();

        // По подписи видно, какая запись выкинула страницы и какую колонку она правила —
        // по ней выбирают служебные колонки (К4б.6).
        var e = Assert.Single(cache.Journal().Events);
        Assert.Equal("SaveChanges: cache_k4_probe_a ~1 (V)", e.Reason);
        Assert.Equal(["page-a"], e.DroppedKeys);
    }

    // ── Транзакции (живой Postgres, временные таблицы) ─────────────────────────────

    [Fact]
    public async Task InTransaction_JournalsOneLine_WithEverySaveOfTheTransaction()
    {
        var cache = NewCache();
        await using var db = TryPg(cache);
        if (db == null) return;
        await SeedPages(cache);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.A.Add(new ProbeA { Id = 1, V = "x" });
            await db.SaveChangesAsync();
            db.B.Add(new ProbeB { Id = 1, V = "x" });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        var e = Assert.Single(cache.Journal().Events);
        Assert.Equal("транзакция: SaveChanges: cache_k4_probe_a +1 | SaveChanges: cache_k4_probe_b +1", e.Reason);
        Assert.Equal(2, e.DroppedCount);
    }

    [Fact]
    public async Task InTransaction_DropsOnlyAfterCommit()
    {
        var cache = NewCache();
        await using var db = TryPg(cache);
        if (db == null) return;
        await SeedPages(cache);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.A.Add(new ProbeA { Id = 1, V = "x" });
            await db.SaveChangesAsync();

            // Не закоммичено: соседний запрос ещё читает старые данные — сбросить сейчас значит
            // дать ему снова положить старое в кэш до конца TTL.
            Assert.True(await Cached(cache, "page-a"));

            await tx.CommitAsync();
        }

        Assert.False(await Cached(cache, "page-a"));
        Assert.True(await Cached(cache, "page-b"));
    }

    [Fact]
    public async Task RolledBack_DropsNothing_AndLeavesNothingForTheNextTransaction()
    {
        var cache = NewCache();
        await using var db = TryPg(cache);
        if (db == null) return;
        await SeedPages(cache);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.A.Add(new ProbeA { Id = 1, V = "x" });
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        Assert.True(await Cached(cache, "page-a"));

        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.B.Add(new ProbeB { Id = 1, V = "x" });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        Assert.True(await Cached(cache, "page-a")); // откаченное не сбросилось чужим коммитом
        Assert.False(await Cached(cache, "page-b"));
    }

    [Fact]
    public async Task DisposedWithoutCommit_DropsNothing()
    {
        var cache = NewCache();
        await using var db = TryPg(cache);
        if (db == null) return;
        await SeedPages(cache);

        // Брошенная транзакция откатывается молча — события об откате EF не шлёт.
        await using (await db.Database.BeginTransactionAsync())
        {
            db.A.Add(new ProbeA { Id = 1, V = "x" });
            await db.SaveChangesAsync();
        }
        Assert.True(await Cached(cache, "page-a"));

        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.B.Add(new ProbeB { Id = 1, V = "x" });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        Assert.True(await Cached(cache, "page-a")); // хвост брошенной не сбросился следующей
        Assert.False(await Cached(cache, "page-b"));
    }

    [Fact]
    public async Task SaveWithoutTransaction_DropsRightAfterSave()
    {
        var cache = NewCache();
        await using var db = TryPg(cache);
        if (db == null) return;
        await SeedPages(cache);

        // Несколько строк в двух таблицах — EF сам заворачивает их в свою транзакцию и
        // коммитит её ДО события «сохранено»; сброс обязан случиться и в этом порядке.
        db.A.AddRange(new ProbeA { Id = 1, V = "x" }, new ProbeA { Id = 2, V = "y" });
        db.B.Add(new ProbeB { Id = 1, V = "z" });
        await db.SaveChangesAsync();

        Assert.False(await Cached(cache, "page-a"));
        Assert.False(await Cached(cache, "page-b"));
    }

    [Fact]
    public async Task FailedSaveInsideTransaction_KeepsEarlierSavesOfThatTransaction()
    {
        var cache = NewCache();
        await using var db = TryPg(cache);
        if (db == null) return;
        await SeedPages(cache);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.A.Add(new ProbeA { Id = 1, V = "x" });
            await db.SaveChangesAsync();

            // NOT NULL в базе — сохранение падает; EF откатывает его до своей точки сохранения,
            // и транзакция живёт дальше.
            db.B.Add(new ProbeB { Id = 1, V = null! });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await tx.CommitAsync();
        }

        Assert.False(await Cached(cache, "page-a")); // удачное сохранение транзакции сбросилось
        Assert.True(await Cached(cache, "page-b"));  // упавшее — нет
    }

    [Fact]
    public async Task ExplicitTags_ForBulkWrites_AlsoWaitForCommit()
    {
        var cache = NewCache();
        await using var db = TryPg(cache);
        if (db == null) return;
        await SeedPages(cache);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // Массовая запись мимо трекера — перехватчик её не видит, таблицу сбрасывают явно.
            await db.A.ExecuteDeleteAsync();
            await db.InvalidateTableCacheAsync<ProbeA>();

            Assert.True(await Cached(cache, "page-a")); // до коммита — рано, как и у SaveChanges
            await tx.CommitAsync();
        }
        Assert.False(await Cached(cache, "page-a"));

        await SeedPages(cache);
        await db.InvalidateTableCacheAsync<ProbeB>(); // вне транзакции — сразу
        Assert.False(await Cached(cache, "page-b"));
        Assert.True(await Cached(cache, "page-a"));
    }

    // ── Регистрация ───────────────────────────────────────────────────────────────

    [Fact]
    public void ContextFromDI_CarriesTheInterceptor_OnlyOnTheWriteContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=none",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        static List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor> InterceptorsOf(DbContext db) =>
            db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors?.ToList() ?? [];

        var write = InterceptorsOf(scope.ServiceProvider.GetRequiredService<SwimmDbContext>());
        Assert.Contains(write, i => i is CacheInvalidationInterceptor);
        Assert.Contains(write, i => i is CacheDependencyInterceptor);

        // Контекст чтения ничего не сохраняет; метки он ставит, а сбрасывать ему нечего.
        var read = InterceptorsOf(scope.ServiceProvider.GetRequiredService<SwimmReadDbContext>());
        Assert.DoesNotContain(read, i => i is CacheInvalidationInterceptor);
        Assert.Contains(read, i => i is CacheDependencyInterceptor);
    }
}

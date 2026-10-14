using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Мост кэша через настоящий канал Postgres (решение В17): «процесс» A слушает, «процесс» B
/// сбрасывает метку у себя — и она пропадает у A. Канал у теста свой, чтобы не задеть живой API,
/// который слушает <c>swimm_cache</c> на той же базе. Плюс хранилище настроек в Sys_AdminSettings.
/// Пропуск, если Postgres недоступен.
/// </summary>
public class CacheBridgePgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private sealed record Payload(string Value);

    private static async Task<bool> PgAvailable()
    {
        try
        {
            await using var c = new NpgsqlConnection(Conn);
            await c.OpenAsync();
            return true;
        }
        catch { return false; }
    }

    private static async Task Until(Func<Task<bool>> condition, string what)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        Assert.Fail($"не дождались: {what}");
    }

    [Fact]
    public async Task ResetInOneProcess_ReachesAnother()
    {
        if (!await PgAvailable()) return;
        var options = new CacheBridgeOptions(true, "swimm_cache_test_" + Guid.NewGuid().ToString("N")[..8]);
        await using var dataSource = NpgsqlDataSource.Create(Conn);

        // Процесс A: свой кэш, слушатель.
        var cacheA = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        var monitorA = new CacheBridgeMonitor(options);
        var listener = new CacheBridgeListener(dataSource, cacheA, monitorA,
            new AdminSettingsService(new MemoryCache(new MemoryCacheOptions())), NullLogger<CacheBridgeListener>.Instance);
        await cacheA.SetAsync("results", new Payload("r"), TimeSpan.FromMinutes(5), CacheTags.Table("Results"));
        await cacheA.SetAsync("records", new Payload("x"), TimeSpan.FromMinutes(5), CacheTags.Table("Records"));

        await listener.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => Task.FromResult(monitorA.State().Listening), "слушатель подключился");

            // Процесс B: свой кэш и отправка в тот же канал.
            var monitorB = new CacheBridgeMonitor(options);
            ICacheService cacheB = new BridgingCacheService(
                new MemoryCacheService(new MemoryCache(new MemoryCacheOptions())),
                new PgNotifyTransport(dataSource, options.Channel), monitorB);

            await cacheB.InvalidateTagsAsync([CacheTags.Table("Results")], "ProdAdmin: импорт");

            await Until(async () => await cacheA.GetAsync<Payload>("results") is null, "сброс дошёл до A");
            Assert.NotNull(await cacheA.GetAsync<Payload>("records"));
            Assert.Equal(1, monitorB.State().Published);
            Assert.Equal(1, monitorA.State().Received);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SettingsStore_SavesAndLoads_RealTable()
    {
        if (!await PgAvailable()) return;
        var services = new ServiceCollection();
        services.AddDbContext<SwimmDbContext>(o => o.UseNpgsql(Conn));
        await using var sp = services.BuildServiceProvider();
        var store = new DbAdminSettingsStore(sp.GetRequiredService<IServiceScopeFactory>());

        // Ключ вне реестра: даже если тест упадёт посередине, живые настройки он не задевает
        // (неизвестные ключи из базы реестр игнорирует).
        const string key = "utest-pg-settings-store";
        try
        {
            store.Save(key, "one");
            store.Save(key, "two"); // вторая запись — обновление, не дубль ключа
            Assert.Equal("two", store.LoadAll()[key]);
        }
        finally
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
            await db.AdminSettingValues.Where(s => s.Key == key).ExecuteDeleteAsync();
        }
    }
}

using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Мост сброса кэша между процессами (решение В17, Services/CacheBridge.cs): сброс у себя →
/// pg_notify → чужие процессы сбрасывают у себя. Здесь — без базы: формат сообщения, обёртка
/// кэша и применение входящего. Настоящий канал Postgres — <c>CacheBridgePgTests</c>.
/// </summary>
public class CacheBridgeTests
{
    private sealed record Payload(string Value);

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly CacheBridgeOptions Options = new(true, CacheBridgeOptions.DefaultChannel);

    private static MemoryCacheService NewCache() => new(new MemoryCache(new MemoryCacheOptions()));

    private sealed class FakeTransport : ICacheBridgeTransport
    {
        public readonly List<string> Sent = [];
        public bool Fail;

        public Task PublishAsync(string payload, CancellationToken ct = default)
        {
            if (Fail) throw new NpgsqlException("канал недоступен");
            Sent.Add(payload);
            return Task.CompletedTask;
        }
    }

    private sealed class ReloadCounter : ISettingsService, IReloadableSettings
    {
        public int Reloads;
        public void Reload() => Reloads++;
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private static CacheBridgeListener Listener(MemoryCacheService local, CacheBridgeMonitor monitor, ISettingsService settings) =>
        // Источник соединений здесь не открывается: Apply не ходит в базу.
        new(NpgsqlDataSource.Create("Host=localhost"), local, monitor, settings, NullLogger<CacheBridgeListener>.Instance);

    // ── Сообщение ────────────────────────────────────────────────────────────

    [Fact]
    public void Message_RoundTrips()
    {
        var msg = new CacheBridgeMessage("p1", false, [CacheTags.Table("Results"), CacheTags.Row("Clubs", 438)], null, "импорт");

        var back = CacheBridgeMessage.Parse(msg.ToPayload(out var oversized));

        Assert.False(oversized);
        Assert.NotNull(back);
        Assert.Equal("p1", back!.Origin);
        Assert.False(back.All);
        Assert.Equal(msg.Tags, back.Tags);
        Assert.Equal("импорт", back.Reason);
    }

    [Fact]
    public void Message_TooManyTags_BecomesResetAll_WithinPostgresLimit()
    {
        // Сброс 2000 строк пловцов не влезет в 8000 байт NOTIFY — лишний общий сброс вместо недосброса.
        var tags = Enumerable.Range(1, 2000).Select(i => CacheTags.Row("Swimmers", i)).ToArray();
        var payload = new CacheBridgeMessage("p1", false, tags, null, "склейка").ToPayload(out var oversized);

        Assert.True(oversized);
        Assert.True(Encoding.UTF8.GetByteCount(payload) <= CacheBridgeMessage.MaxPayloadBytes);
        var back = CacheBridgeMessage.Parse(payload)!;
        Assert.True(back.All);
        Assert.Null(back.Tags);
        Assert.Contains("склейка", back.Reason);
    }

    [Fact]
    public void Message_LongReason_IsTrimmed()
    {
        var payload = new CacheBridgeMessage("p1", true, null, null, new string('я', 5000)).ToPayload(out var oversized);

        Assert.False(oversized);
        Assert.True(CacheBridgeMessage.Parse(payload)!.Reason!.Length < 400);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"a\":true}")] // без метки процесса — не наше сообщение
    public void Message_Garbage_IsNull(string payload) => Assert.Null(CacheBridgeMessage.Parse(payload));

    [Fact]
    public void TouchesSettings_OnSettingsTable_OrResetAll()
    {
        Assert.True(new CacheBridgeMessage("p", false, [CacheTags.Table(AdminSettingsService.TableName)], null, null).TouchesSettings());
        Assert.True(new CacheBridgeMessage("p", true, null, null, null).TouchesSettings());
        Assert.False(new CacheBridgeMessage("p", false, [CacheTags.Table("Results")], null, null).TouchesSettings());
    }

    [Theory]
    [InlineData("swimm_cache", true)]
    [InlineData("a1_b", true)]
    [InlineData("Swimm", false)]
    [InlineData("x; DROP TABLE y", false)]
    [InlineData("", false)]
    public void Channel_OnlySafeIdentifiers(string channel, bool ok) =>
        Assert.Equal(ok, CacheBridgeOptions.IsValidChannel(channel));

    // ── Обёртка кэша (процесс-писатель) ──────────────────────────────────────

    [Fact]
    public async Task Invalidate_DropsLocally_ThenPublishes()
    {
        var local = NewCache();
        var transport = new FakeTransport();
        var monitor = new CacheBridgeMonitor(Options);
        var cache = new BridgingCacheService(local, transport, monitor);
        await cache.SetAsync("results", new Payload("r"), Ttl, CacheTags.Table("Results"));

        await cache.InvalidateTagsAsync([CacheTags.Table("Results")], "импорт 1581");

        Assert.Null(await cache.GetAsync<Payload>("results"));
        var sent = CacheBridgeMessage.Parse(Assert.Single(transport.Sent))!;
        Assert.Equal(monitor.Origin, sent.Origin);
        Assert.Equal([CacheTags.Table("Results")], sent.Tags);
        Assert.Equal("импорт 1581", sent.Reason);
        Assert.Equal(1, monitor.State().Published);
    }

    [Fact]
    public async Task InvalidateAll_PublishesAll()
    {
        var transport = new FakeTransport();
        var cache = new BridgingCacheService(NewCache(), transport, new CacheBridgeMonitor(Options));

        await cache.InvalidateAllAsync("кнопка");

        Assert.True(CacheBridgeMessage.Parse(Assert.Single(transport.Sent))!.All);
    }

    [Fact]
    public async Task AllTagAmongTags_PublishesAll()
    {
        var transport = new FakeTransport();
        var cache = new BridgingCacheService(NewCache(), transport, new CacheBridgeMonitor(Options));

        await cache.InvalidateTagsAsync(CacheTags.Table("Results"), CacheTags.All);

        Assert.True(CacheBridgeMessage.Parse(Assert.Single(transport.Sent))!.All);
    }

    [Fact]
    public async Task EmptyTags_PublishNothing()
    {
        var transport = new FakeTransport();
        var cache = new BridgingCacheService(NewCache(), transport, new CacheBridgeMonitor(Options));

        await cache.InvalidateTagsAsync([], "пусто");

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task PublishFailure_DoesNotThrow_AndIsCounted()
    {
        // Запись в базу уже закоммичена — отказ канала не должен ронять сохранение.
        var transport = new FakeTransport { Fail = true };
        var monitor = new CacheBridgeMonitor(Options);
        var cache = new BridgingCacheService(NewCache(), transport, monitor);

        await cache.InvalidateTagsAsync([CacheTags.Table("Results")], "импорт");

        var state = monitor.State();
        Assert.Equal(1, state.PublishFailures);
        Assert.Equal(0, state.Published);
        Assert.Contains("канал недоступен", state.LastError);
    }

    [Fact]
    public async Task GetOrCreate_GoesThroughLocalCache_WithItsTags()
    {
        // Делегирование явное: реализация по умолчанию из интерфейса потеряла бы метки.
        var local = NewCache();
        var cache = new BridgingCacheService(local, new FakeTransport(), new CacheBridgeMonitor(Options));
        var builds = 0;

        for (var i = 0; i < 2; i++)
            await cache.GetOrCreateAsync("k", () => { builds++; return Task.FromResult(new Payload("v")); },
                Ttl, CacheTags.Table("Records"));

        Assert.Equal(1, builds);
        Assert.Contains(CacheTags.Table("Records"), local.Snapshot().Single(e => e.Key == "k").Tags);
    }

    // ── Слушатель (любой другой процесс) ─────────────────────────────────────

    [Fact]
    public async Task Apply_ForeignTags_DropsLocalEntries()
    {
        var local = NewCache();
        var monitor = new CacheBridgeMonitor(Options);
        await local.SetAsync("results", new Payload("r"), Ttl, CacheTags.Table("Results"));
        await local.SetAsync("records", new Payload("x"), Ttl, CacheTags.Table("Records"));

        var payload = new CacheBridgeMessage("other-process", false, [CacheTags.Table("Results")], null, "ProdAdmin: импорт").ToPayload(out _);
        Assert.True(Listener(local, monitor, new ReloadCounter()).Apply(payload));

        Assert.Null(await local.GetAsync<Payload>("results"));
        Assert.NotNull(await local.GetAsync<Payload>("records"));
        Assert.Equal(1, monitor.State().Received);
        Assert.Contains(local.Journal().Events, e => e.Reason.Contains("⇄") && e.Reason.Contains("ProdAdmin: импорт"));
    }

    [Fact]
    public async Task Apply_OwnMessage_IsSkipped()
    {
        var local = NewCache();
        var monitor = new CacheBridgeMonitor(Options);
        await local.SetAsync("results", new Payload("r"), Ttl, CacheTags.Table("Results"));

        var payload = new CacheBridgeMessage(monitor.Origin, true, null, null, "своё").ToPayload(out _);
        Assert.False(Listener(local, monitor, new ReloadCounter()).Apply(payload));

        Assert.NotNull(await local.GetAsync<Payload>("results"));
        Assert.Equal(0, monitor.State().Received);
    }

    [Fact]
    public async Task Apply_ResetAll_DropsEverything_AndReloadsSettings()
    {
        var local = NewCache();
        var settings = new ReloadCounter();
        await local.SetAsync("a", new Payload("a"), Ttl, CacheTags.Table("Results"));

        var payload = new CacheBridgeMessage("other", true, null, null, "sync-from-prod.sh").ToPayload(out _);
        Listener(local, new CacheBridgeMonitor(Options), settings).Apply(payload);

        Assert.Null(await local.GetAsync<Payload>("a"));
        Assert.Equal(1, settings.Reloads);
    }

    [Fact]
    public void Apply_SettingsTableTag_ReloadsSettings_OtherTagsDoNot()
    {
        var settings = new ReloadCounter();
        var listener = Listener(NewCache(), new CacheBridgeMonitor(Options), settings);

        listener.Apply(new CacheBridgeMessage("other", false, [CacheTags.Table("Results")], null, null).ToPayload(out _));
        Assert.Equal(0, settings.Reloads);

        listener.Apply(new CacheBridgeMessage("other", false, [CacheTags.Table(AdminSettingsService.TableName)], null, null).ToPayload(out _));
        Assert.Equal(1, settings.Reloads);
    }

    [Fact]
    public void Apply_Garbage_IsSkipped() =>
        Assert.False(Listener(NewCache(), new CacheBridgeMonitor(Options), new ReloadCounter()).Apply("{oops"));
}

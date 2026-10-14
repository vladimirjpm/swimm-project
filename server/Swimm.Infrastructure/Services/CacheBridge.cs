using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;

namespace Swimm.Infrastructure.Services;

// ═══════════════════════════════════════════════════════════════════════════════════════════
// Мост сброса кэша через Postgres LISTEN/NOTIFY (решение В17, docs/plans/azure-deploy-plan.md
// §0-тер п. 3; устройство — docs/admin-pages/cache.md, «Мост между процессами»).
//
// Зачем. Кэш у каждого процесса свой, в памяти (MemoryCacheService). Пока писатель был один —
// сам сайт — сброс после записи доставался тому, кто читает. С В17 пишут ДРУГИЕ процессы:
// локальная админка в режиме ProdAdmin и её CLI пишут прямо в прод-БД, и без моста сайт
// показывал бы старые данные до конца TTL. Тот же мост — основа второго инстанса без Redis.
//
// Как. Сброс в любом процессе (перехватчик EF после коммита, явные метки массовой записи, кнопка
// на /Admin/Cache) сначала выполняется у себя, затем уходит в канал pg_notify. Каждый процесс
// держит слушающее соединение (CacheBridgeListener) и применяет чужие сбросы к своему кэшу.
// Свои сообщения узнаёт по метке процесса и пропускает: у себя уже сброшено.
//
// Чего мост НЕ гарантирует. NOTIFY не хранится: пока соединение лежит, сообщения теряются. Поэтому
// после каждого переподключения — общий сброс (лишний сброс безопасен, недосброс — нет). Ошибка
// отправки запись не отменяет: данные в базе уже закоммичены, остаётся TTL.
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>Настройки моста: <c>CacheBridge:Enabled</c> (по умолчанию вкл.), <c>CacheBridge:Channel</c>.</summary>
public sealed record CacheBridgeOptions(bool Enabled, string Channel)
{
    public const string DefaultChannel = "swimm_cache";

    /// <summary>Имя канала идёт в LISTEN как идентификатор — только безопасные символы.</summary>
    public static bool IsValidChannel(string channel) => Regex.IsMatch(channel, "^[a-z_][a-z0-9_]{0,62}$");
}

/// <summary>
/// Сообщение в канале. Короткие имена полей — пейлоад NOTIFY ограничен 8000 байтами.
/// </summary>
public sealed record CacheBridgeMessage(
    [property: JsonPropertyName("o")] string Origin,
    [property: JsonPropertyName("a")] bool All,
    [property: JsonPropertyName("t")] string[]? Tags,
    [property: JsonPropertyName("k")] string? Key,
    [property: JsonPropertyName("r")] string? Reason)
{
    /// <summary>
    /// Предел пейлоада Postgres — «короче 8000 байт» (в UTF-8). Берём с запасом: имя канала и
    /// служебные байты протокола в этот предел не входят, но рисковать тут нечем.
    /// </summary>
    public const int MaxPayloadBytes = 7900;
    private const int MaxReasonLength = 300;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// Пейлоад для отправки. Метки не влезли — общий сброс с той же подписью: дороже, но верно
    /// (недосброс хуже). <paramref name="oversized"/> — так и вышло.
    /// </summary>
    public string ToPayload(out bool oversized)
    {
        var msg = this with { Reason = Trim(Reason) };
        var payload = JsonSerializer.Serialize(msg, Json);
        oversized = Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes;
        if (!oversized) return payload;

        var all = msg with
        {
            All = true, Tags = null, Key = null,
            Reason = Trim($"{msg.Reason} (меток {msg.Tags?.Length ?? 0} — не влезли в NOTIFY, сброшено всё)"),
        };
        return JsonSerializer.Serialize(all, Json);
    }

    public static CacheBridgeMessage? Parse(string payload)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<CacheBridgeMessage>(payload, Json);
            return msg is { Origin.Length: > 0 } ? msg : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Сообщение задевает настройки сайта — их надо перечитать из БД (В7).</summary>
    public bool TouchesSettings() =>
        All || (Tags?.Contains(CacheTags.Table(AdminSettingsService.TableName), StringComparer.Ordinal) ?? false);

    private static string? Trim(string? reason) =>
        reason is { Length: > MaxReasonLength } ? reason[..MaxReasonLength] + "…" : reason;
}

/// <summary>Куда уходит сообщение. Отдельно от моста — тесты подставляют свою.</summary>
public interface ICacheBridgeTransport
{
    Task PublishAsync(string payload, CancellationToken ct = default);
}

/// <summary><c>SELECT pg_notify(channel, payload)</c> отдельным коротким соединением.</summary>
public sealed class PgNotifyTransport(NpgsqlDataSource dataSource, string channel) : ICacheBridgeTransport
{
    public async Task PublishAsync(string payload, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("SELECT pg_notify($1, $2)");
        cmd.Parameters.AddWithValue(channel);
        cmd.Parameters.AddWithValue(payload);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// Счётчики моста для /Admin/Cache (<see cref="ICacheBridgeStatus"/>) и метка процесса. Один на
/// процесс: его делят отправитель (<see cref="BridgingCacheService"/>) и слушатель.
/// </summary>
public sealed class CacheBridgeMonitor(CacheBridgeOptions options) : ICacheBridgeStatus
{
    /// <summary>Метка процесса: свои сообщения слушатель пропускает.</summary>
    public string Origin { get; } = Guid.NewGuid().ToString("N");

    public CacheBridgeOptions Options => options;

    private readonly object _gate = new();
    private DateTimeOffset? _listeningSince;
    private long _reconnects, _published, _publishFailures, _oversized, _received;
    private DateTimeOffset? _lastReceivedAt, _lastErrorAt;
    private string? _lastReceivedReason, _lastError;

    public CacheBridgeState State()
    {
        lock (_gate)
            return new CacheBridgeState(
                options.Enabled, options.Channel, _listeningSince is not null, _listeningSince,
                _reconnects, _published, _publishFailures, _oversized, _received,
                _lastReceivedAt, _lastReceivedReason, _lastError, _lastErrorAt);
    }

    internal void Published(bool oversized)
    {
        lock (_gate) { _published++; if (oversized) _oversized++; }
    }

    internal void Failed(string error)
    {
        lock (_gate) { _lastError = error; _lastErrorAt = DateTimeOffset.UtcNow; }
    }

    internal void PublishFailed(string error)
    {
        lock (_gate) { _publishFailures++; _lastError = error; _lastErrorAt = DateTimeOffset.UtcNow; }
    }

    internal void Received(string? reason)
    {
        lock (_gate) { _received++; _lastReceivedAt = DateTimeOffset.UtcNow; _lastReceivedReason = reason; }
    }

    internal void Listening(bool on, bool reconnect = false)
    {
        lock (_gate)
        {
            _listeningSince = on ? DateTimeOffset.UtcNow : null;
            if (on && reconnect) _reconnects++;
        }
    }
}

/// <summary>
/// Обёртка кэша: всё чтение и запись — как было, в локальный кэш; сброс — сначала у себя, потом
/// в канал. Каждый член делегирован ЯВНО: у <see cref="ICacheService"/> есть реализации по
/// умолчанию, и забытый член молча ушёл бы в упрощённую версию (без склейки промахов и меток).
/// </summary>
public sealed class BridgingCacheService(
    ICacheService inner,
    ICacheBridgeTransport transport,
    CacheBridgeMonitor monitor,
    ILogger<BridgingCacheService>? logger = null) : ICacheService
{
    /// <summary>Сколько ждём отправку: запись уже в базе, вызывающего держать нельзя.</summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    public Task<T?> GetAsync<T>(string key) => inner.GetAsync<T>(key);
    public Task SetAsync<T>(string key, T value, TimeSpan ttl) => inner.SetAsync(key, value, ttl);
    public Task SetAsync<T>(string key, T value, TimeSpan ttl, params string[] tags) => inner.SetAsync(key, value, ttl, tags);

    public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, params string[] tags)
        where T : class? => inner.GetOrCreateAsync(key, factory, ttl, tags);

    public async Task RemoveAsync(string key)
    {
        await inner.RemoveAsync(key);
        await PublishAsync(new CacheBridgeMessage(monitor.Origin, false, null, key, null));
    }

    public async Task InvalidateTagsAsync(params string[] tags)
    {
        await inner.InvalidateTagsAsync(tags);
        await PublishTagsAsync(tags, null);
    }

    public async Task InvalidateTagsAsync(IReadOnlyCollection<string> tags, string reason)
    {
        await inner.InvalidateTagsAsync(tags, reason);
        await PublishTagsAsync(tags, reason);
    }

    public async Task InvalidateAllAsync()
    {
        await inner.InvalidateAllAsync();
        await PublishAsync(new CacheBridgeMessage(monitor.Origin, true, null, null, null));
    }

    public async Task InvalidateAllAsync(string reason)
    {
        await inner.InvalidateAllAsync(reason);
        await PublishAsync(new CacheBridgeMessage(monitor.Origin, true, null, null, reason));
    }

    private Task PublishTagsAsync(IReadOnlyCollection<string> tags, string? reason)
    {
        if (tags.Count == 0) return Task.CompletedTask;
        var all = tags.Contains(CacheTags.All);
        return PublishAsync(new CacheBridgeMessage(monitor.Origin, all, all ? null : [.. tags], null, reason));
    }

    private async Task PublishAsync(CacheBridgeMessage message)
    {
        var payload = message.ToPayload(out var oversized);
        try
        {
            using var cts = new CancellationTokenSource(PublishTimeout);
            await transport.PublishAsync(payload, cts.Token);
            monitor.Published(oversized);
        }
        catch (Exception ex)
        {
            // Запись уже закоммичена — отказ отправки её не отменяет. Другие процессы увидят
            // изменение через TTL; в журнал — чтобы было видно на /Admin/Cache.
            monitor.PublishFailed(ex.Message);
            logger?.LogWarning(ex, "Мост кэша: сброс не отправлен в канал — другие процессы увидят изменение только через TTL.");
        }
    }
}

/// <summary>Настройки, которые умеют перечитаться из БД (В7): мост дёргает их по сообщению.</summary>
public interface IReloadableSettings
{
    void Reload();
}

/// <summary>
/// Слушатель канала: одно долгое соединение с <c>LISTEN</c>; чужие сообщения применяются к
/// локальному кэшу (мимо <see cref="BridgingCacheService"/> — иначе эхо). Обрыв — переподключение
/// с паузой до минуты и общий сброс после него. Работает всегда, когда мост включён, — от
/// <c>BackgroundJobs:Enabled</c> не зависит: на проде он и есть единственный способ узнать о записи
/// локальной админки.
/// </summary>
public sealed class CacheBridgeListener(
    NpgsqlDataSource dataSource,
    MemoryCacheService localCache,
    CacheBridgeMonitor monitor,
    ISettingsService settings,
    ILogger<CacheBridgeListener> logger) : BackgroundService
{
    /// <summary>
    /// Как часто проверять, что соединение живо, если в канале тихо. Заодно не даёт балансировщику
    /// Azure закрыть простаивающее соединение.
    /// </summary>
    private static readonly TimeSpan PingEvery = TimeSpan.FromMinutes(1);

    /// <summary>Страховка настроек: перечитать, даже если сообщение потерялось.</summary>
    private static readonly TimeSpan SettingsRefreshEvery = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var channel = monitor.Options.Channel;
        var backoff = TimeSpan.FromSeconds(2);
        var attempt = 0;
        var warned = false;

        while (!ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await using var conn = await dataSource.OpenConnectionAsync(ct);
                conn.Notification += (_, e) => Apply(e.Payload);
                await using (var listen = new NpgsqlCommand($"LISTEN {channel}", conn))
                    await listen.ExecuteNonQueryAsync(ct);

                var reconnect = attempt > 1;
                monitor.Listening(true, reconnect);
                if (reconnect)
                {
                    // Пока соединения не было, сообщения терялись: сбрасываем всё и перечитываем
                    // настройки. Первое подключение на старте — кэш и так пуст.
                    await localCache.InvalidateAllAsync("мост кэша: переподключение к каналу — сообщения могли потеряться");
                    ReloadSettings();
                    logger.LogInformation("Мост кэша: снова слушаю канал {Channel}.", channel);
                }
                else
                    logger.LogInformation("Мост кэша: слушаю канал {Channel}.", channel);

                backoff = TimeSpan.FromSeconds(2);
                warned = false;
                var lastSettingsRefresh = DateTimeOffset.UtcNow;

                while (!ct.IsCancellationRequested)
                {
                    if (await conn.WaitAsync(PingEvery, ct)) continue;

                    await using (var ping = new NpgsqlCommand("SELECT 1", conn))
                        await ping.ExecuteScalarAsync(ct);

                    if (DateTimeOffset.UtcNow - lastSettingsRefresh >= SettingsRefreshEvery)
                    {
                        ReloadSettings();
                        lastSettingsRefresh = DateTimeOffset.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                monitor.Failed(ex.Message);
                // Лежащая база — не повод сыпать предупреждениями раз в минуту: громко — первый раз.
                if (!warned)
                    logger.LogWarning(ex, "Мост кэша: канал {Channel} недоступен, переподключаюсь.", channel);
                else
                    logger.LogDebug(ex, "Мост кэша: канал {Channel} всё ещё недоступен.", channel);
                warned = true;
            }
            finally
            {
                monitor.Listening(false);
            }

            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    /// <summary>Применить сообщение из канала. Возвращает false — своё или битое, пропущено.</summary>
    internal bool Apply(string payload)
    {
        var msg = CacheBridgeMessage.Parse(payload);
        if (msg is null)
        {
            logger.LogWarning("Мост кэша: непонятное сообщение в канале, пропущено.");
            return false;
        }
        if (msg.Origin == monitor.Origin) return false;

        var reason = $"⇄ из другого процесса: {msg.Reason ?? "без подписи"}";
        try
        {
            // MemoryCacheService сбрасывает синхронно (готовая задача) — ждать нечего, а колбэк
            // Npgsql и не асинхронный.
            if (msg.All)
                localCache.InvalidateAllAsync(reason).GetAwaiter().GetResult();
            else if (msg.Tags is { Length: > 0 } tags)
                localCache.InvalidateTagsAsync(tags, reason).GetAwaiter().GetResult();
            else if (msg.Key is { Length: > 0 } key)
                localCache.RemoveAsync(key).GetAwaiter().GetResult();

            monitor.Received(msg.Reason);
            if (msg.TouchesSettings()) ReloadSettings();
            return true;
        }
        catch (Exception ex)
        {
            monitor.Failed(ex.Message);
            logger.LogWarning(ex, "Мост кэша: не удалось применить сброс из канала.");
            return false;
        }
    }

    private void ReloadSettings()
    {
        try { (settings as IReloadableSettings)?.Reload(); }
        catch (Exception ex) { logger.LogWarning(ex, "Мост кэша: настройки из БД не перечитались."); }
    }
}

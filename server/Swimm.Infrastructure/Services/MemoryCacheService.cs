using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Validation;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// IMemoryCache реализация ICacheService — подходит для одного инстанса.
/// Для Redis: реализовать RedisCacheService через IDistributedCache + System.Text.Json
/// и заменить регистрацию в DependencyInjection.cs (docs/plans/cache-tags-plan.md §5).
///
/// Метки — токены отмены: у каждой метки свой <see cref="CancellationTokenSource"/>, и запись
/// подписана на токены своих меток плюс общий (метка all). Сброс метки = отмена её токена:
/// все подписанные записи вылетают разом, без перебора ключей. В Redis то же самое делает
/// версия метки (INCR), поэтому потребителям разница не видна.
///
/// Откуда метки (К3): явные — от вызывающего; метки таблиц — САМИ, из SQL, выполненного во
/// время сборки (<see cref="CacheBuildScope"/> + перехватчик команд EF); метки вложенных
/// записей — наследуются при попадании в них. Запись хранит свои метки рядом со значением.
///
/// Сужение до строк (К4б.3, docs/plans/cache-row-precision-plan.md): сборка знает, включён ли
/// выключатель <see cref="CacheSettings.RowPrecision"/>; попадание в суженную запись с долей
/// <see cref="CacheSettings.HitVerifyPercent"/> сверяется с ответом, собранным заново.
/// </summary>
public class MemoryCacheService : ICacheService, ICacheDiagnostics
{
    private readonly IMemoryCache _cache;

    // Выключатели кэша (/Admin/Settings). Нет — сужение выключено и сверки нет: так кэш живёт в
    // тестах, которые собирают его без настроек.
    private readonly ISettingsService? _settings;

    // Общий токен (метка all) и токены меток. Старые токены только ОТМЕНЯЕМ, но не Dispose:
    // соседний поток мог уже взять ссылку на источник и вот-вот спросит у него Token —
    // у освобождённого источника это ObjectDisposedException. Без таймеров и связанных
    // регистраций неосвобождённый источник ничего не держит.
    private CancellationTokenSource _all = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tags = new();

    // Сборки в полёте: ключ → Lazy<Task<Entry>>. Параллельные промахи одного ключа ждут одну сборку.
    private readonly ConcurrentDictionary<string, object> _inflight = new();

    // Живые записи — для диагностики (/Admin/Cache). Убираются колбэком вытеснения.
    private readonly ConcurrentDictionary<string, Entry> _index = new();

    // Журнал сбросов (К4б.1, docs/plans/cache-row-precision-plan.md): последние сбросы, выкинувшие
    // хоть одну запись, и счётчики «кого выкидывают» по видам записей — с запуска процесса.
    private const int JournalSize = 200;
    private const int JournalKeysPerEvent = 20;
    private const int JournalTagsPerEvent = 100;
    private const int JournalReasonLength = 500;
    private const string NoReason = "без подписи";
    private readonly ConcurrentQueue<CacheInvalidationEvent> _journal = new();
    private readonly ConcurrentDictionary<string, DropCounter> _drops = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _since = DateTimeOffset.UtcNow;
    private long _emptyInvalidations;

    private sealed class DropCounter
    {
        public long ByTags;
        public long ByAll;
    }

    // Сверка на попадании (К4б.3, §4-6): счётчики с запуска процесса и последние расхождения.
    private const int MismatchesKept = 50;
    private const int MismatchSnippet = 60;
    private readonly ConcurrentQueue<CacheHitMismatch> _mismatches = new();
    private long _hitChecks;
    private long _hitMismatches;

    // Подметание источников меток (§7): метки строк копятся в _tags до общего сброса. Раз в 1024
    // касания смотрим размер; больше порога — выбрасываем источники, которых не носит ни одна запись.
    private int _captures;
    private int _sweeping;

    /// <summary>Сколько источников меток держать до подметания; тесты ставят маленький порог.</summary>
    internal int TagSweepThreshold { get; init; } = 20_000;

    /// <summary>Сколько источников меток сейчас в словаре — для тестов подметания.</summary>
    internal int TagSourceCount => _tags.Count;

    public MemoryCacheService(IMemoryCache cache, ISettingsService? settings = null)
    {
        _cache = cache;
        _settings = settings;
    }

    /// <summary>
    /// Запись в IMemoryCache: значение и его метки с токенами, с которыми оно валидно.
    /// Токены нужны при попадании во вложенный кэш — внешняя сборка наследует ИМЕННО их.
    /// </summary>
    private sealed record Entry(
        object? Value,
        IReadOnlyDictionary<string, CancellationToken> Tokens,
        string ValueType,
        DateTimeOffset StoredAt,
        DateTimeOffset ExpiresAt)
    {
        /// <summary>Запись сужена до строк (носит <c>row:</c>/<c>anyrow:</c>) — её сверяет попадание.</summary>
        public bool RowLevel { get; } = Tokens.Keys.Any(CacheTags.IsRowLevel);
    }

    public Task<T?> GetAsync<T>(string key)
    {
        if (_cache.TryGetValue(key, out Entry? entry) && entry?.Value is T value)
        {
            CacheBuildScope.Current?.Inherit(entry.Tokens);
            return Task.FromResult<T?>(value);
        }
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl) => SetAsync(key, value, ttl, []);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, params string[] tags)
    {
        // Ручная запись внутри чужой сборки: какие из её таблиц нужны именно этому значению,
        // уже не отличить — берём все, накопленные на этот момент. Лишний сброс безопасен.
        var tokens = CaptureTokens(tags);
        if (CacheBuildScope.Current is { } scope)
            foreach (var (tag, token) in scope.Tokens) tokens.TryAdd(tag, token);

        Store(key, value, typeof(T), ttl, tokens);
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, params string[] tags)
        where T : class?
    {
        // Внутри сверки — мимо кэша целиком: вложенная запись тоже строится заново, иначе
        // устаревшая вложенная совпала бы сама с собой (§4-6). Ничего не читаем и не кладём.
        if (CacheBuildScope.Current is { IsVerification: true }) return await factory();

        for (var attempt = 0; ; attempt++)
        {
            if (_cache.TryGetValue(key, out Entry? hit) && hit?.Value is T cached)
            {
                if (!ShouldVerify(hit) || await MatchesFreshAsync(key, hit, cached, factory))
                {
                    CacheBuildScope.Current?.Inherit(hit.Tokens);
                    return cached;
                }
                // Не совпало: запись врала и выкинута — ответ строится обычным путём, ниже.
            }

            var mine = new Lazy<Task<Entry>>(() => BuildAsync(key, factory, ttl, tags));
            var build = (Lazy<Task<Entry>>)_inflight.GetOrAdd(key, mine);
            try
            {
                var entry = await build.Value;
                // Наследует и тот, кто строил, и те, кто ждал его сборку: у каждого своя внешняя
                // сборка, и каждой нужно знать, из чего собран этот ответ.
                CacheBuildScope.Current?.Inherit(entry.Tokens);
                return (T)entry.Value!;
            }
            catch (OperationCanceledException) when (!ReferenceEquals(build, mine) && attempt == 0)
            {
                // Сборку отменил её владелец — оборвали ЕГО запрос (фабрики получают токен
                // запроса). Ждавшие не должны падать вместе с ним: строим сами, один раз.
            }
            finally
            {
                // Снимаем ровно ЭТУ сборку: упавшую — чтобы следующий запрос попробовал заново,
                // удачную — потому что результат уже лежит в кэше.
                _inflight.TryRemove(new KeyValuePair<string, object>(key, build));
            }
        }
    }

    private async Task<Entry> BuildAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, string[] tags)
    {
        // Явные метки и общий токен — ДО сборки, метки таблиц — в момент касания (перехватчик).
        // Сбросили данные, пока ответ строился, — снятый токен уже отменён, и собранное из
        // старых данных в кэш не ляжет. Со снятием ПОСЛЕ сборки оно пролежало бы до конца TTL.
        var tokens = CaptureTokens(tags);
        using var scope = CacheBuildScope.Begin(CaptureTagToken, rowPrecision: RowPrecisionOn);

        var value = await factory();

        foreach (var (tag, token) in scope.Tokens) tokens.TryAdd(tag, token);
        // «Не найдено» (null) не кэшируем — как и до К3: следующий запрос спросит базу снова.
        return value is null
            ? new Entry(null, tokens, typeof(T).Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            : Store(key, value, typeof(T), ttl, tokens);
    }

    // Без настроек (кэш из тестов, собранный без них) сужения нет — такие тесты живут как до К4б.
    private bool RowPrecisionOn =>
        _settings?.GetValue(CacheSettings.RowPrecision, CacheSettings.DefaultRowPrecision) ?? false;

    /// <summary>Сверять ли это попадание: запись сужена до строк и выпала доля сверки.</summary>
    private bool ShouldVerify(Entry hit)
    {
        if (!hit.RowLevel || _settings is null) return false;
        var percent = _settings.GetValue(CacheSettings.HitVerifyPercent, 0);
        return percent > 0 && Random.Shared.Next(100) < percent;
    }

    /// <summary>
    /// Сверка на попадании (§4-6): ответ строится заново целиком мимо кэша (вложенные записи
    /// тоже) в изолированной сборке, и его JSON сравнивается с тем, что лежит в кэше. Не совпало —
    /// «подозрение на недосброс» в журнал и запись вон. Ловит нарушение правила сужения, которого
    /// не предусмотрели тесты, при обычном прокликивании. Табличные записи не сверяются: их
    /// доказал К4, а шум «от времени» в них не нужен.
    /// </summary>
    private async Task<bool> MatchesFreshAsync<T>(string key, Entry hit, T cached, Func<Task<T>> factory)
    {
        T fresh;
        using (CacheBuildScope.BeginVerification())
        {
            try { fresh = await factory(); }
            // Сверка — диагностика: упавшая сборка страницу не роняет, её просто нечем сверить.
            catch (Exception e) when (e is not OperationCanceledException) { return true; }
        }

        Interlocked.Increment(ref _hitChecks);
        // Запись сбросили, пока сверяли: данные поменялись, и сброс до неё дошёл — это не недосброс.
        if (hit.Tokens.Values.Any(t => t.IsCancellationRequested)) return true;

        var was = JsonSerializer.Serialize(cached, typeof(T));
        var now = JsonSerializer.Serialize(fresh, typeof(T));
        if (was == now) return true;

        Interlocked.Increment(ref _hitMismatches);
        _mismatches.Enqueue(new CacheHitMismatch(
            DateTimeOffset.UtcNow,
            key,
            hit.Tokens.Keys
                .Where(t => t != CacheTags.All)
                .OrderBy(t => !CacheTags.IsRowLevel(t))
                .ThenBy(t => t, StringComparer.Ordinal)
                .Take(JournalTagsPerEvent)
                .ToList(),
            Difference(was, now)));
        while (_mismatches.Count > MismatchesKept && _mismatches.TryDequeue(out _)) { }

        // Выкидываем ровно ЭТУ запись: пока сверяли, её могли уже пересобрать.
        if (_cache.TryGetValue(key, out Entry? current) && ReferenceEquals(current, hit)) _cache.Remove(key);
        return false;
    }

    /// <summary>Где JSON из кэша разошёлся с собранным заново: позиция и кусок с каждой стороны.</summary>
    internal static string Difference(string was, string now)
    {
        var at = 0;
        var common = Math.Min(was.Length, now.Length);
        while (at < common && was[at] == now[at]) at++;
        return $"с символа {at}: в кэше «{Snippet(was, at)}», заново «{Snippet(now, at)}»";

        static string Snippet(string json, int at)
        {
            var from = Math.Max(0, at - 20);
            var length = Math.Min(MismatchSnippet, json.Length - from);
            return (from > 0 ? "…" : "") + json.Substring(from, length) + (from + length < json.Length ? "…" : "");
        }
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task InvalidateTagsAsync(params string[] tags) => InvalidateTagsAsync(tags, NoReason);

    public Task InvalidateTagsAsync(IReadOnlyCollection<string> tags, string reason)
    {
        // Среди меток общая — это общий сброс: он отменяет и все остальные.
        if (tags.Contains(CacheTags.All)) return InvalidateAllAsync(reason);

        // Жертв считаем ДО отмены: после неё выкинутую запись уже не отличить от протухшей.
        var victims = LiveKeys(tags);
        foreach (var tag in tags)
            if (_tags.TryRemove(tag, out var source)) source.Cancel();

        Record(reason, tags, all: false, victims);
        return Task.CompletedTask;
    }

    public Task InvalidateAllAsync() => InvalidateAllAsync(NoReason);

    public Task InvalidateAllAsync(string reason)
    {
        var victims = LiveKeys(tags: null);
        InvalidateAll();
        Record(reason, [CacheTags.All], all: true, victims);
        return Task.CompletedTask;
    }

    public CacheJournal Journal() => new(
        _since,
        // Очередь перечисляется от старых к новым — журнал показывают свежими первыми.
        _journal.Reverse().ToList(),
        Interlocked.Read(ref _emptyInvalidations),
        _drops
            .Select(d => new CacheDropStats(d.Key, Interlocked.Read(ref d.Value.ByTags), Interlocked.Read(ref d.Value.ByAll)))
            .OrderByDescending(d => d.ByTags + d.ByAll)
            .ThenBy(d => d.Kind, StringComparer.Ordinal)
            .ToList(),
        new CacheHitChecks(
            Interlocked.Read(ref _hitChecks),
            Interlocked.Read(ref _hitMismatches),
            _mismatches.Reverse().ToList()));

    /// <summary>
    /// Вид записи для счётчиков журнала: у HTTP-ответов — два первых сегмента ключа
    /// (<c>http:swimmer</c>, <c>http:hub-groups</c>), у записей данных — первый
    /// (<c>swimmer-profile</c>, <c>results</c>). Id в вид не входит: считаем «страницы пловца»,
    /// а не пловца 5825.
    /// </summary>
    internal static string KindOf(string key)
    {
        var parts = key.Split(':', 3);
        return parts is ["http", var second, ..] ? $"http:{second}" : parts[0];
    }

    /// <summary>Живые записи, которые носят любую из меток; null — все живые (общий сброс).</summary>
    private List<string> LiveKeys(IReadOnlyCollection<string>? tags)
    {
        var set = tags is null ? null : new HashSet<string>(tags, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var keys = new List<string>();
        foreach (var (key, entry) in _index)
        {
            if (entry.ExpiresAt <= now || entry.Tokens.Values.Any(t => t.IsCancellationRequested)) continue;
            if (set is null || entry.Tokens.Keys.Any(set.Contains)) keys.Add(key);
        }
        return keys;
    }

    private void Record(string reason, IReadOnlyCollection<string> tags, bool all, List<string> victims)
    {
        // Пустой сброс (метку никто не носил) не пишем — только считаем: иначе он вытеснил бы
        // из журнала то, ради чего журнал заведён. Общий сброс пишем всегда: импорт в 12:00 —
        // событие, даже если кэш был пуст.
        if (victims.Count == 0 && !all)
        {
            Interlocked.Increment(ref _emptyInvalidations);
            return;
        }

        foreach (var key in victims)
        {
            var counter = _drops.GetOrAdd(KindOf(key), static _ => new DropCounter());
            if (all) Interlocked.Increment(ref counter.ByAll);
            else Interlocked.Increment(ref counter.ByTags);
        }

        _journal.Enqueue(new CacheInvalidationEvent(
            DateTimeOffset.UtcNow,
            reason.Length > JournalReasonLength ? reason[..JournalReasonLength] + "…" : reason,
            tags.Take(JournalTagsPerEvent).ToList(),
            all,
            victims.Count,
            victims.Order(StringComparer.Ordinal).Take(JournalKeysPerEvent).ToList()));
        while (_journal.Count > JournalSize && _journal.TryDequeue(out _)) { }
    }

    public IReadOnlyList<CacheEntryInfo> Snapshot() => _index
        // IMemoryCache вытесняет протухшее лениво — в снимок живые записи, а не хвост индекса.
        .Where(e => e.Value.ExpiresAt > DateTimeOffset.UtcNow
                    && !e.Value.Tokens.Values.Any(t => t.IsCancellationRequested))
        .Select(e => new CacheEntryInfo(
            e.Key,
            e.Value.Tokens.Keys.Where(t => t != CacheTags.All).OrderBy(t => t, StringComparer.Ordinal).ToList(),
            e.Value.ValueType,
            e.Value.StoredAt,
            e.Value.ExpiresAt))
        .OrderBy(e => e.Key, StringComparer.Ordinal)
        .ToList();

    private void InvalidateAll()
    {
        // Атомарно заменяем общий токен: новые записи получат новый, старые вылетают по отмене.
        Interlocked.Exchange(ref _all, new CancellationTokenSource()).Cancel();

        // Токены меток тоже отменяем и выбрасываем. Просто очистить словарь нельзя: запись,
        // которая собиралась в этот момент, могла взять новый общий токен и СТАРЫЙ токен метки —
        // после очистки сброс этой метки её бы уже не нашёл. Отмена даёт лишний сброс, не
        // недосброс, и заодно не даёт словарю копить метки строк (row:Swimmers:…) бесконечно.
        foreach (var tag in _tags.Keys)
            if (_tags.TryRemove(tag, out var source)) source.Cancel();
    }

    private CancellationToken CaptureTagToken(string tag)
    {
        // Размер словаря — не на каждое касание: Count у ConcurrentDictionary берёт все блокировки.
        if ((Interlocked.Increment(ref _captures) & 1023) == 0 && _tags.Count > TagSweepThreshold) SweepTags();
        return _tags.GetOrAdd(tag, _ => new CancellationTokenSource()).Token;
    }

    /// <summary>
    /// Подметание (§7): выбросить источники меток, которых не носит ни одна запись в индексе.
    /// ОТМЕНИТЬ, а не просто выбросить: сборка в полёте могла уже снять токен такой метки — отмена
    /// даст ей лишний сброс (её ответ не ляжет в кэш), а выброс без отмены — недосброс: сброс
    /// метки такую сборку уже не нашёл бы.
    /// </summary>
    internal void SweepTags()
    {
        if (Interlocked.Exchange(ref _sweeping, 1) == 1) return;
        try
        {
            // Протухшие, но ещё не вытесненные записи тоже в счёт: их метки лишний раз
            // остаются — это безопасно.
            var worn = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in _index.Values) worn.UnionWith(entry.Tokens.Keys);
            foreach (var tag in _tags.Keys)
                if (!worn.Contains(tag) && _tags.TryRemove(tag, out var source)) source.Cancel();
        }
        finally
        {
            Volatile.Write(ref _sweeping, 0);
        }
    }

    private Dictionary<string, CancellationToken> CaptureTokens(string[] tags)
    {
        var tokens = new Dictionary<string, CancellationToken>(tags.Length + 1)
        {
            [CacheTags.All] = Volatile.Read(ref _all).Token,
        };
        foreach (var tag in tags) tokens.TryAdd(tag, CaptureTagToken(tag));
        return tokens;
    }

    private Entry Store(string key, object? value, Type valueType, TimeSpan ttl,
        IReadOnlyDictionary<string, CancellationToken> tokens)
    {
        // Правило Redis проверяется уже на памяти: память проглотит кортеж, а Redis запишет его
        // как {} (CacheValueRules). Вердикт кэшируется на тип — проверка бесплатна.
        CacheValueRules.EnsureStorable(valueType);

        var now = DateTimeOffset.UtcNow;
        var entry = new Entry(value, tokens, valueType.Name, now, now + ttl);

        var options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(ttl);
        foreach (var token in tokens.Values)
            options.AddExpirationToken(new CancellationChangeToken(token));
        options.RegisterPostEvictionCallback(static (k, v, _, state) =>
        {
            // Убираем из индекса ровно ЭТУ запись: при перезаписи ключа колбэк старой записи
            // не должен стереть новую.
            if (v is Entry evicted && state is MemoryCacheService self)
                self._index.TryRemove(new KeyValuePair<string, Entry>((string)k, evicted));
        }, this);

        _index[key] = entry;
        // Токен уже отменён (сброс пришёл во время сборки) — IMemoryCache сочтёт запись
        // протухшей сразу, и в кэш она не ляжет.
        _cache.Set(key, entry, options);
        return entry;
    }
}

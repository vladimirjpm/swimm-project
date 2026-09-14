using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;

namespace Swimm.Infrastructure.Data;

/// <summary>
/// Сброс кэша по меткам — САМ (docs/plans/cache-tags-plan.md, К4): сохранение через EF
/// сбрасывает метки таблиц, которые оно изменило (<c>table:HubGroups</c>…), — те самые, что
/// записи кэша получили при сборке от <see cref="CacheDependencyInterceptor"/>. Ручной сброс в
/// месте записи больше не нужен, и «забыли сбросить» исчезает как класс. Правка расписания
/// группы роняет страницы групп, а season-best (он из <c>HubGroups</c> не собран) остаётся.
///
/// ⚠ Сброс — ПОСЛЕ коммита, не внутри транзакции. Сбросить до коммита — соседний запрос успеет
/// прочитать ещё старые данные и снова положить их в кэш до конца TTL. Поэтому внутри открытой
/// транзакции метки копятся и сбрасываются на <c>TransactionCommitted</c>; откат их выбрасывает,
/// а сбой коммита — сбрасывает: закоммитилось или нет, неизвестно, а лишний сброс безопасен.
///
/// Удаление добавляет таблицы, куда оно каскадит в базе (зависимые строки трекер не видит).
///
/// Видит ТОЛЬКО SaveChanges. Массовые операции мимо трекера (<c>ExecuteUpdate/Delete</c>, сырой
/// SQL) сбрасывают метки явно (<see cref="DbContextCacheExtensions"/>). Это сознательно: общий перехват
/// записей по тексту SQL выбивал бы страницы групп на каждом запросе — отметка «был онлайн»
/// пишется массовым UPDATE в <c>Sys_AppUsers</c>, а страница группы собрана и из этой таблицы.
/// </summary>
public sealed class CacheInvalidationInterceptor(ICacheService cache)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    // Перехватчик один на все контексты (синглтон), а сохранение и транзакция — у каждого
    // экземпляра контекста свои. Слабая таблица не держит контекст после его Dispose.
    private readonly ConditionalWeakTable<DbContext, State> _states = new();

    private sealed class State
    {
        /// <summary>Метки идущего SaveChanges — ждут его успеха.</summary>
        public string[]? Saving;

        /// <summary>Подпись идущего SaveChanges для журнала сбросов: что именно поменялось.</summary>
        public string SavingReason = "";

        /// <summary>Метки сохранений внутри открытой транзакции — ждут её коммита.</summary>
        public readonly HashSet<string> Pending = new(StringComparer.Ordinal);

        /// <summary>Подписи тех же сохранений — уходят в журнал одной строкой на коммите.</summary>
        public readonly List<string> PendingReasons = [];
    }

    // ── SaveChanges ────────────────────────────────────────────────────────────────

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    // Синхронный SaveChanges ждёт сброс синхронно: у кэша в памяти он завершён сразу.
    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AfterSaveAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await AfterSaveAsync(eventData.Context);
        return result;
    }

    // Упавшее или отменённое сохранение ничего не записало (внутри транзакции EF откатывает его
    // до своей точки сохранения) — его метки выбрасываем; накопленные раньше в транзакции живут.
    public void SaveChangesFailed(DbContextErrorEventData eventData) => ForgetSaving(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ForgetSaving(eventData.Context);
        return Task.CompletedTask;
    }

    public void SaveChangesCanceled(DbContextEventData eventData) => ForgetSaving(eventData.Context);

    public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        ForgetSaving(eventData.Context);
        return Task.CompletedTask;
    }

    // ── Транзакции ─────────────────────────────────────────────────────────────────

    // Новая транзакция: хвост прошлой, брошенной без коммита (Dispose откатывает её молча, без
    // события), выбрасываем — те данные не записаны.
    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        ForgetPending(eventData.Context);
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        ForgetPending(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        FlushPendingAsync(eventData.Context, failed: false).GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        FlushPendingAsync(eventData.Context, failed: false);

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        ForgetPending(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ForgetPending(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        FlushPendingAsync(eventData.Context, failed: true).GetAwaiter().GetResult();

    public Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default) =>
        FlushPendingAsync(eventData.Context, failed: true);

    // ── Устройство ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Явный сброс для записи мимо трекера (<see cref="DbContextCacheExtensions.InvalidateCacheTagsAsync"/>):
    /// в открытой транзакции — после её коммита, иначе сразу. Правило то же, что у SaveChanges.
    /// </summary>
    internal Task InvalidateAfterCommitAsync(DbContext context, string[] tags, string reason)
    {
        if (tags.Length == 0) return Task.CompletedTask;

        // Внутри транзакции — ждём коммита. Нереляционный провайдер (InMemory в тестах)
        // транзакций не ведёт и событий о них не шлёт — сбрасываем сразу.
        if (context.Database.IsRelational() && context.Database.CurrentTransaction is not null)
        {
            var state = _states.GetOrCreateValue(context);
            state.Pending.UnionWith(tags);
            state.PendingReasons.Add(reason);
            return Task.CompletedTask;
        }
        return cache.InvalidateTagsAsync(tags, reason);
    }

    private void Collect(DbContext? context)
    {
        if (context is null) return;

        var tables = new HashSet<string>(StringComparer.Ordinal);
        // Что поменялось по таблицам — подпись для журнала сбросов (/Admin/Cache): по ней
        // видно, какая именно запись выкинула страницы, и какие колонки пишут чаще всего.
        var changes = new SortedDictionary<string, TableChange>(StringComparer.Ordinal);
        // Entries() сама делает DetectChanges (если он не выключен — тогда не сделал бы и
        // SaveChanges), так что правка отслеживаемого объекта без Update() тоже видна.
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.Metadata.GetTableName() is { } table)
            {
                tables.Add(table);
                if (!changes.TryGetValue(table, out var change)) changes[table] = change = new TableChange();
                change.Note(entry);
            }

            // Удаление каскадит в БАЗЕ на зависимые строки, которых трекер не видел (их не
            // загружали): удалили группу — ушли её состав, медиа, тренировки. Их таблицы берём
            // из модели; есть ли там строки на самом деле, неважно — лишний сброс безопасен.
            if (entry.State == EntityState.Deleted) AddCascadeTables(entry.Metadata, tables, []);
        }

        var state = _states.GetOrCreateValue(context);
        state.Saving = tables.Select(CacheTags.Table).ToArray();
        state.SavingReason = Describe(changes, tables);
    }

    /// <summary>Сколько строк таблицы добавлено, изменено, удалено и какие колонки правились.</summary>
    private sealed class TableChange
    {
        private int _added, _modified, _deleted;
        private readonly SortedSet<string> _columns = new(StringComparer.Ordinal);

        public void Note(EntityEntry entry)
        {
            switch (entry.State)
            {
                case EntityState.Added: _added++; break;
                case EntityState.Deleted: _deleted++; break;
                case EntityState.Modified:
                    _modified++;
                    foreach (var p in entry.Properties)
                        if (p.IsModified) _columns.Add(p.Metadata.Name);
                    break;
            }
        }

        /// <summary>«~2 (Name, UpdatedAt) +1 −3» — что увидит админ в журнале.</summary>
        public override string ToString()
        {
            var parts = new List<string>(3);
            if (_modified > 0) parts.Add(_columns.Count > 0 ? $"~{_modified} ({string.Join(", ", _columns)})" : $"~{_modified}");
            if (_added > 0) parts.Add($"+{_added}");
            if (_deleted > 0) parts.Add($"−{_deleted}");
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Подпись сохранения: «SaveChanges: HubGroups ~1 (TrainingSchedule, UpdatedAt); каскад: …».
    /// Каскад — таблицы, которые сбрасываются только потому, что удаление уходит в них в базе.
    /// </summary>
    private static string Describe(SortedDictionary<string, TableChange> changes, HashSet<string> tables)
    {
        var text = "SaveChanges: " + string.Join("; ", changes.Select(c => $"{c.Key} {c.Value}"));
        var cascade = tables.Where(t => !changes.ContainsKey(t)).Order(StringComparer.Ordinal).ToList();
        return cascade.Count > 0 ? $"{text}; каскад: {string.Join(", ", cascade)}" : text;
    }

    private static void AddCascadeTables(IEntityType principal, HashSet<string> tables, HashSet<IEntityType> seen)
    {
        foreach (var fk in principal.GetReferencingForeignKeys())
        {
            if (fk.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull)) continue;
            var dependent = fk.DeclaringEntityType;
            if (dependent.GetTableName() is { } table) tables.Add(table);

            // SET NULL правит строку, а не удаляет её — дальше по цепочке каскад не идёт.
            if (fk.DeleteBehavior == DeleteBehavior.Cascade && seen.Add(dependent))
                AddCascadeTables(dependent, tables, seen);
        }
    }

    private Task AfterSaveAsync(DbContext? context)
    {
        if (context is null || !_states.TryGetValue(context, out var state) || state.Saving is not { } tags)
            return Task.CompletedTask;
        state.Saving = null;
        return InvalidateAfterCommitAsync(context, tags, state.SavingReason);
    }

    private Task FlushPendingAsync(DbContext? context, bool failed)
    {
        if (context is null || !_states.TryGetValue(context, out var state) || state.Pending.Count == 0)
            return Task.CompletedTask;
        var tags = state.Pending.ToArray();
        var reason = state.PendingReasons.Count == 1
            ? state.PendingReasons[0]
            : "транзакция: " + string.Join(" | ", state.PendingReasons);
        if (failed) reason = "сбой коммита, сброшено на всякий случай — " + reason;
        state.Pending.Clear();
        state.PendingReasons.Clear();
        return cache.InvalidateTagsAsync(tags, reason);
    }

    private void ForgetSaving(DbContext? context)
    {
        if (context is not null && _states.TryGetValue(context, out var state)) state.Saving = null;
    }

    private void ForgetPending(DbContext? context)
    {
        if (context is null || !_states.TryGetValue(context, out var state)) return;
        state.Pending.Clear();
        state.PendingReasons.Clear();
    }
}

/// <summary>
/// ЯВНЫЙ сброс — там, где запись идёт мимо трекера EF (<c>ExecuteUpdate</c>, <c>ExecuteDelete</c>,
/// сырой SQL) и <see cref="CacheInvalidationInterceptor"/> её не видит:
/// <code>await _db.InvalidateCacheTagsAsync(_db.TableTag&lt;ResultRecord&gt;());</code>
/// </summary>
public static class DbContextCacheExtensions
{
    /// <summary>
    /// Метка таблицы сущности. Имя — из модели EF, а не литералом: иначе метка сброса разойдётся
    /// с меткой, которую запись кэша получила из SQL, и сброс молча промахнётся.
    /// </summary>
    public static string TableTag<TEntity>(this DbContext db) where TEntity : class =>
        CacheTags.Table(db.Model.FindEntityType(typeof(TEntity))?.GetTableName()
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} не отображён на таблицу в модели {db.GetType().Name}"));

    /// <summary>
    /// Сбросить метки после записи мимо трекера — по тем же правилам, что SaveChanges: в открытой
    /// транзакции после её коммита, иначе сразу. Контекст без перехватчика (собран в обход DI —
    /// миграции, тесты) ничего не сбрасывает: кэша у него нет.
    /// </summary>
    public static Task InvalidateCacheTagsAsync(this DbContext db, params string[] tags) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors?
            .OfType<CacheInvalidationInterceptor>().FirstOrDefault()?
            .InvalidateAfterCommitAsync(db, tags, "массовая запись мимо EF (ExecuteUpdate/ExecuteDelete)")
        ?? Task.CompletedTask;
}

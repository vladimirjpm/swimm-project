using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Infrastructure.Services;

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
/// Метки строк (docs/plans/cache-row-precision-plan.md §2.2, К4б.2): кроме <c>table:T</c>
/// сохранение сбрасывает <c>row:R:id</c> — строка корня (<see cref="CacheRowRoots"/>) свою, строка
/// с FK на корень — метку корня по старому И новому значению FK (участник ушёл из группы 24 в
/// 17 → обе). Временный ключ пропускается: строки ещё нет в базе, от неё не зависит ни одна
/// запись кэша. Где задетые строки неизвестны — <c>anyrow:T</c>: таблицы каскада, больше
/// <see cref="MaxRowsPerTable"/> строк одной таблицы, строка с FK на корень, подключённая не
/// запросом (<see cref="Watch"/>).
///
/// Служебные колонки (§2.6, К4б.6; выключатель <see cref="CacheSettings.ColumnPrecision"/>):
/// изменённая строка, у которой ВСЕ изменённые колонки из реестра <see cref="CacheServiceColumns"/>,
/// даёт <c>col:T.C</c> на каждую из них и, если таблица — корень, метку своей строки, — без
/// <c>table:T</c> и без меток корней по FK. Колонку читают только те, кто её назвал в SQL, и
/// они носят <c>col:</c>; не носит его лишь корень, прочитанный в своём блоке, — его строку
/// закрывает её <c>row:</c> (<see cref="CacheBuildScope.TouchTable"/>). Поэтому правка loglig-
/// привязки пловца не роняет ни состав, ни обзор его клуба. Добавление, удаление и правка хоть
/// одной обычной колонки — как всегда.
///
/// Видит ТОЛЬКО SaveChanges. Массовые операции мимо трекера (<c>ExecuteUpdate/Delete</c>, сырой
/// SQL) сбрасывают метки явно (<see cref="DbContextCacheExtensions"/>). Это сознательно: общий перехват
/// записей по тексту SQL выбивал бы страницы групп на каждом запросе — отметка «был онлайн»
/// пишется массовым UPDATE в <c>Sys_AppUsers</c>, а страница группы собрана и из этой таблицы.
/// </summary>
public sealed class CacheInvalidationInterceptor(ICacheService cache, ISettingsService? settings = null)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    /// <summary>
    /// Сжатие (§2.2 п. 4): сохранение — или транзакция — меняет больше строк одной таблицы, и
    /// вместо их меток строк идёт одна <c>anyrow:T</c>. Слиянию клубов, пересчёту объединённых
    /// мест, переносу результатов метки на тысячи строк ни к чему, а в Redis каждая метка — INCR.
    /// </summary>
    internal const int MaxRowsPerTable = 500;

    // Перехватчик один на все контексты (синглтон), а сохранение и транзакция — у каждого
    // экземпляра контекста свои. Слабая таблица не держит контекст после его Dispose.
    private readonly ConditionalWeakTable<DbContext, State> _states = new();

    private static readonly object AttachedMark = new();

    // Без настроек (перехватчик из тестов, собранный без них) точности по колонкам нет: служебная
    // правка сбрасывает таблицу, как до К4б.6.
    private bool ColumnPrecisionOn =>
        settings?.GetValue(CacheSettings.ColumnPrecision, CacheSettings.DefaultColumnPrecision) ?? false;

    private sealed class State
    {
        /// <summary>Метки идущего SaveChanges — ждут его успеха.</summary>
        public ChangeTags? Saving;

        /// <summary>Подпись идущего SaveChanges для журнала сбросов: что именно поменялось.</summary>
        public string SavingReason = "";

        /// <summary>Метки сохранений внутри открытой транзакции — ждут её коммита.</summary>
        public readonly ChangeTags Pending = new();

        /// <summary>Подписи тех же сохранений — уходят в журнал одной строкой на коммите.</summary>
        public readonly List<string> PendingReasons = [];

        /// <summary>
        /// Строки, которые контекст подключил НЕ запросом (<see cref="Watch"/>). <c>null</c> —
        /// контекст за этим не следит, и не запросом могла прийти любая строка.
        /// </summary>
        public ConditionalWeakTable<object, object>? Attached;
    }

    /// <summary>
    /// Следить, какие строки контекст подключил НЕ запросом (§2.2 п. 7). У строки из запроса
    /// исходные значения — из базы. У подключённой (<c>Attach</c>, <c>Update</c>, <c>Remove</c>
    /// заглушки <c>new X { Id = … }</c>, повторное подключение после <c>ChangeTracker.Clear</c>) —
    /// те, что дал код: у заглушки FK пуст, и метку старого родителя (группы, откуда ушёл
    /// участник) не построить. Такая строка вместо меток строк даёт <c>anyrow:T</c>. Новая строка
    /// (Added) не в счёт: её значения и пишутся.
    ///
    /// Зовёт конструктор контекста: следить надо с первого подключения, иначе ранняя заглушка
    /// сошла бы за строку из базы. Контекст, который не следит, не верит ни одной изменённой или
    /// удалённой строке с FK на корень.
    /// </summary>
    public static void Watch(DbContext context, DbContextOptions options)
    {
        var interceptor = options.FindExtension<CoreOptionsExtension>()?.Interceptors?
            .OfType<CacheInvalidationInterceptor>().FirstOrDefault();
        if (interceptor is null) return; // контекст без сброса кэша (чтение, миграции) — следить незачем

        var attached = new ConditionalWeakTable<object, object>();
        interceptor._states.GetOrCreateValue(context).Attached = attached;
        context.ChangeTracker.Tracked += (_, e) =>
        {
            if (e.FromQuery || e.Entry.State == EntityState.Added) return;
            attached.AddOrUpdate(e.Entry.Entity, AttachedMark);
        };
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
    /// Явный сброс таблицы после записи мимо трекера (<see cref="DbContextCacheExtensions.InvalidateTableCacheAsync"/>):
    /// какие строки задеты, неизвестно — <c>table:T</c> и <c>anyrow:T</c>.
    /// </summary>
    internal Task InvalidateTableAfterCommitAsync(DbContext context, string table)
    {
        var tags = new ChangeTags();
        tags.AddUnknown(table);
        return InvalidateAfterCommitAsync(context, tags, $"массовая запись мимо EF (ExecuteUpdate/ExecuteDelete): {table}");
    }

    /// <summary>
    /// Явный сброс после массовой записи ТОЛЬКО служебных колонок мимо трекера
    /// (<see cref="DbContextCacheExtensions.InvalidateColumnsCacheAsync"/>, §2.6): <c>col:T.C</c>
    /// на каждую, а если таблица — корень, ещё <c>anyrow:T</c> (какие строки задеты, неизвестно, а
    /// корень в своём блоке <c>col:</c> не носит). Выключатель выключен — как обычная массовая
    /// запись. Колонки уже проверены по реестру (колонка не из него — исключение: её <c>col:</c>
    /// никто не носит, и сброс прошёл бы мимо всех — недосброс).
    /// </summary>
    internal Task InvalidateColumnsAfterCommitAsync(DbContext context, IEntityType type, string table, IReadOnlyList<string> columns)
    {
        var tags = new ChangeTags();
        if (!ColumnPrecisionOn) tags.AddUnknown(table);
        else
        {
            tags.AddColumns(table, columns);
            if (CacheRowRoots.IsRoot(type)) tags.AddUnknown(table, whole: false);
        }
        return InvalidateAfterCommitAsync(context, tags,
            $"массовая запись служебных колонок мимо EF (ExecuteUpdate): {table} ({string.Join(", ", columns)})");
    }

    /// <summary>
    /// Сброс после записи: в открытой транзакции — после её коммита, иначе сразу. Правило одно
    /// для SaveChanges и для явного сброса массовой записи.
    /// </summary>
    private Task InvalidateAfterCommitAsync(DbContext context, ChangeTags tags, string reason)
    {
        if (tags.IsEmpty) return Task.CompletedTask;

        // Внутри транзакции — ждём коммита. Нереляционный провайдер (InMemory в тестах)
        // транзакций не ведёт и событий о них не шлёт — сбрасываем сразу.
        if (context.Database.IsRelational() && context.Database.CurrentTransaction is not null)
        {
            var state = _states.GetOrCreateValue(context);
            tags.MergeInto(state.Pending);
            state.PendingReasons.Add(reason);
            return Task.CompletedTask;
        }
        return cache.InvalidateTagsAsync(tags.ToTags(), reason);
    }

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        var state = _states.GetOrCreateValue(context);
        (state.Saving, state.SavingReason) = Gather(context, state, ColumnPrecisionOn);
    }

    /// <summary>
    /// Метки, которые сбросило бы сохранение прямо сейчас, — для тестов: временный ключ виден
    /// только до сохранения, и только у провайдера, который его выдаёт (Npgsql, не InMemory).
    /// </summary>
    internal string[] PreviewTags(DbContext context) =>
        Gather(context, _states.GetOrCreateValue(context), ColumnPrecisionOn).Tags.ToTags();

    private static (ChangeTags Tags, string Reason) Gather(DbContext context, State state, bool columnPrecision)
    {
        var tags = new ChangeTags();
        var cascade = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<string>();
        var service = columnPrecision ? CacheServiceColumns.Of(context.Model) : null;
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
                if (!changes.TryGetValue(table, out var change)) changes[table] = change = new TableChange();
                change.Note(entry);

                rows.Clear();
                if (service is not null && ServiceOnly(entry, service) is { } columns)
                {
                    // Служебная правка (§2.6): col: на колонки и, у корня, метка своей строки.
                    // table:T и метки корней по FK не нужны — см. описание класса.
                    tags.AddColumns(table, columns);
                    if (CacheRowRoots.IsRoot(entry.Metadata))
                    {
                        var ownKnown = tags.CollectsRowsOf(table) && CollectOwnRowTag(entry, rows);
                        tags.AddRow(table, ownKnown ? rows : null, whole: false);
                    }
                    continue; // правка, не удаление: каскада нет
                }

                var known = tags.CollectsRowsOf(table) && CollectRowTags(entry, state, rows);
                tags.AddRow(table, known ? rows : null);
            }

            // Удаление каскадит в БАЗЕ на зависимые строки, которых трекер не видел (их не
            // загружали): удалили группу — ушли её состав, медиа, тренировки. Их таблицы берём
            // из модели; есть ли там строки на самом деле, неважно — лишний сброс безопасен.
            // Какие строки задеты и чьи они, трекер не знает — anyrow (§2.2 п. 5): удалили
            // медиа — ушли его публикации, а страница группы сужена по группе, не по медиа.
            if (entry.State == EntityState.Deleted) AddCascadeTables(entry.Metadata, cascade, []);
        }
        foreach (var table in cascade) tags.AddUnknown(table);

        return (tags, Describe(changes, cascade));
    }

    /// <summary>
    /// Метки строк одной изменённой строки (§2.2 п. 1–3) — в <paramref name="into"/>. false —
    /// какие строки корней она задела, неизвестно: исходные значения её FK взяты не из базы.
    /// </summary>
    private static bool CollectRowTags(EntityEntry entry, State state, List<string> into)
    {
        var links = RowLinks.Of(entry.Metadata);
        if (!links.Supported) return false;

        // п. 1: своя строка корня. Ключ не меняется — старое значение равно новому. Временный
        // (новая строка, id выдаст база) пропускается: от строки, которой нет, никто не зависит.
        if (links.OwnKey is { } key)
        {
            var k = entry.Property(key);
            if (!k.IsTemporary) AddRowTag(into, links.OwnTable, k.CurrentValue);
        }
        if (links.Parents.Length == 0) return true;

        // п. 2: FK на корни. У изменённой и удалённой строки — и старое значение: участник ушёл
        // из группы 24 в 17, меняются обе. Старое верно, только если строку загрузил запрос.
        var added = entry.State == EntityState.Added;
        if (!added && !OriginalsFromDatabase(state, entry)) return false;
        foreach (var (fk, root) in links.Parents)
        {
            var p = entry.Property(fk);
            if (!p.IsTemporary) AddRowTag(into, root, p.CurrentValue);
            if (!added) AddRowTag(into, root, p.OriginalValue);
        }
        return true;
    }

    /// <summary>
    /// Изменённые служебные колонки строки (§2.6) — если правка задела ТОЛЬКО их; иначе null:
    /// добавление, удаление, хоть одна обычная колонка (<c>Update()</c> помечает все), сложное
    /// свойство (его правка в <c>Properties</c> не видна) или ни одной помеченной колонки.
    /// </summary>
    private static List<string>? ServiceOnly(EntityEntry entry, CacheServiceColumns.Resolved service)
    {
        if (entry.State != EntityState.Modified) return null;
        List<string>? columns = null;
        foreach (var p in entry.Properties)
        {
            if (!p.IsModified) continue;
            if (!service.Columns.TryGetValue(p.Metadata, out var column)) return null;
            (columns ??= []).Add(column);
        }
        return columns is not null && !entry.ComplexProperties.Any(c => c.IsModified) ? columns : null;
    }

    /// <summary>
    /// Метка строки служебной правки корня (§2.6) — только своя: её носит корень, прочитанный в
    /// своём блоке по id, а <c>col:</c> он не носит. Ключ настоящий и у строки не из запроса.
    /// Корня, которого читают ещё и по FK на себя, реестр служебных колонок не допускает
    /// (<see cref="CacheServiceColumns"/>). false — ключ не целый (в модели таких корней нет).
    /// </summary>
    private static bool CollectOwnRowTag(EntityEntry entry, List<string> into)
    {
        var links = RowLinks.Of(entry.Metadata);
        if (links.OwnKey is not { } key) return false;
        AddRowTag(into, links.OwnTable, entry.Property(key).CurrentValue);
        return true;
    }

    /// <summary>
    /// Исходные значения строки — из базы: её загрузил запрос, а не подключил код (<see cref="Watch"/>).
    /// Контекст, который не следит, не знает этого ни про одну строку.
    /// </summary>
    private static bool OriginalsFromDatabase(State state, EntityEntry entry) =>
        state.Attached is { } attached && !attached.TryGetValue(entry.Entity, out _);

    private static void AddRowTag(List<string> into, string table, object? id)
    {
        if (id is null) return; // связи нет — и метки нет
        // Тип ключа — целое: другие RowLinks отбраковал (Supported = false).
        into.Add(CacheTags.Row(table, Convert.ToInt64(id, CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Что у сущности даёт метки строк: свой ключ, если она корень, и FK на корни. Считается по
    /// модели один раз на тип сущности.
    /// </summary>
    private sealed class RowLinks
    {
        private static readonly ConditionalWeakTable<IEntityType, RowLinks> Cache = new();

        /// <summary>Ключ строки — если сущность сама корень.</summary>
        public IProperty? OwnKey { get; private init; }
        public string OwnTable { get; private init; } = "";

        /// <summary>FK на корни и таблица корня.</summary>
        public (IProperty Fk, string Root)[] Parents { get; private init; } = [];

        /// <summary>
        /// false — ключ корня или FK на корень не одна целая колонка на первичный ключ: метку
        /// строки не построить, изменённые строки таблицы всегда «неизвестны» (anyrow). В модели
        /// таких нет; появятся — сброс станет грубее, но не пропустит строку.
        /// </summary>
        public bool Supported { get; private init; } = true;

        public static RowLinks Of(IEntityType type) => Cache.GetValue(type, Build);

        private static RowLinks Build(IEntityType type)
        {
            var supported = true;
            IProperty? ownKey = null;
            if (CacheRowRoots.IsRoot(type))
            {
                if (type.FindPrimaryKey() is { Properties: [var k] } && IsWholeNumber(k)) ownKey = k;
                else supported = false;
            }

            var parents = new List<(IProperty, string)>();
            foreach (var fk in type.GetForeignKeys())
            {
                // FK на корень «только своей строки» (пользователь) меток не даёт — потомков под
                // ним никто не сужает (CacheRowRoots.OwnRowOnly).
                if (!CacheRowRoots.IsParentRoot(fk.PrincipalEntityType)) continue;
                if (fk.Properties is [var p] && fk.PrincipalKey.IsPrimaryKey() && IsWholeNumber(p)
                    && fk.PrincipalEntityType.GetTableName() is { } root)
                    parents.Add((p, root));
                else supported = false;
            }

            return new RowLinks
            {
                OwnKey = ownKey,
                OwnTable = type.GetTableName() ?? "",
                Parents = [.. parents],
                Supported = supported,
            };
        }

        private static bool IsWholeNumber(IProperty p) =>
            (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) is var t && (t == typeof(int) || t == typeof(long));
    }

    /// <summary>
    /// Метки одного сохранения — или всей транзакции (сохранения в неё сливаются): изменённые
    /// таблицы, а по каждой — метки её строк либо «какие строки — неизвестно», и нужна ли таблица
    /// целиком или только служебные колонки (§2.6).
    /// </summary>
    internal sealed class ChangeTags
    {
        private sealed class TableRows
        {
            public int Count;

            /// <summary>Метки строк; <c>null</c> — строки неизвестны, вместо них <c>anyrow:T</c>.</summary>
            public HashSet<string>? Rows = new(StringComparer.Ordinal);

            /// <summary>Есть правка не только служебных колонок (или неизвестно какая) — нужна <c>table:T</c>.</summary>
            public bool Whole;

            /// <summary>Служебные колонки служебных правок — по <c>col:T.C</c> на каждую.</summary>
            public SortedSet<string>? Columns;
        }

        private readonly Dictionary<string, TableRows> _tables = new(StringComparer.Ordinal);

        public bool IsEmpty => _tables.Count == 0;

        /// <summary>Нужны ли ещё метки строк таблицы — или она уже сжата до anyrow.</summary>
        public bool CollectsRowsOf(string table) =>
            !_tables.TryGetValue(table, out var t) || (t.Rows is not null && t.Count < MaxRowsPerTable);

        /// <summary>
        /// Одна изменённая строка таблицы; <paramref name="rows"/> null — чьи строки задеты, неизвестно.
        /// <paramref name="whole"/> false — служебная правка корня: метки её строк без <c>table:T</c>.
        /// </summary>
        public void AddRow(string table, List<string>? rows, bool whole = true)
        {
            var t = Of(table);
            t.Count++;
            t.Whole |= whole;
            if (rows is null || t.Count > MaxRowsPerTable) t.Rows = null;
            else t.Rows?.UnionWith(rows);
        }

        /// <summary>Служебные колонки таблицы, изменённые служебной правкой (§2.6).</summary>
        public void AddColumns(string table, IEnumerable<string> columns)
        {
            var t = Of(table);
            (t.Columns ??= new SortedSet<string>(StringComparer.Ordinal)).UnionWith(columns);
        }

        /// <summary>
        /// Строки таблицы изменены, но какие — неизвестно (каскад, массовая запись). <paramref name="whole"/>
        /// false — массовая запись только служебных колонок корня: <c>anyrow:T</c> без <c>table:T</c>.
        /// </summary>
        public void AddUnknown(string table, bool whole = true)
        {
            var t = Of(table);
            t.Rows = null;
            t.Whole |= whole;
        }

        public void MergeInto(ChangeTags target)
        {
            foreach (var (table, src) in _tables)
            {
                var t = target.Of(table);
                t.Count += src.Count;
                t.Whole |= src.Whole;
                if (src.Rows is null || t.Count > MaxRowsPerTable) t.Rows = null;
                else t.Rows?.UnionWith(src.Rows);
                if (src.Columns is not null) target.AddColumns(table, src.Columns);
            }
        }

        /// <summary>
        /// Метки для сброса: по каждой таблице <c>table:T</c> (если правка не только служебная), её
        /// строки или <c>anyrow:T</c>, и <c>col:T.C</c> служебных правок. <c>col:</c> нужен и рядом с
        /// <c>table:T</c>: суженные читатели таблицы <c>table:T</c> не носят, а метки чужих корней
        /// служебная правка не дала.
        /// </summary>
        public string[] ToTags()
        {
            var tags = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (table, t) in _tables)
            {
                if (t.Whole) Add(CacheTags.Table(table));
                if (t.Rows is null) Add(CacheTags.AnyRow(table));
                else foreach (var row in t.Rows) Add(row);
                if (t.Columns is not null) foreach (var column in t.Columns) Add(CacheTags.Column(table, column));
            }
            return [.. tags];

            void Add(string tag)
            {
                if (seen.Add(tag)) tags.Add(tag);
            }
        }

        public void Clear() => _tables.Clear();

        private TableRows Of(string table)
        {
            if (!_tables.TryGetValue(table, out var t)) _tables[table] = t = new TableRows();
            return t;
        }
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
    private static string Describe(SortedDictionary<string, TableChange> changes, HashSet<string> cascadeTables)
    {
        var text = "SaveChanges: " + string.Join("; ", changes.Select(c => $"{c.Key} {c.Value}"));
        var cascade = cascadeTables.Where(t => !changes.ContainsKey(t)).Order(StringComparer.Ordinal).ToList();
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
        if (context is null || !_states.TryGetValue(context, out var state) || state.Pending.IsEmpty)
            return Task.CompletedTask;
        var tags = state.Pending.ToTags();
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
/// <code>await _db.InvalidateTableCacheAsync&lt;ResultRecord&gt;();</code>
/// </summary>
public static class DbContextCacheExtensions
{
    /// <summary>
    /// Сбросить таблицу после записи мимо трекера: <c>table:T</c> и <c>anyrow:T</c>. Какие строки
    /// задела массовая запись, неизвестно, поэтому падают и записи, сузившие чтение таблицы до
    /// своих строк: одна <c>table:T</c> прошла бы мимо них — недосброс (§2.2 п. 6).
    ///
    /// По тем же правилам, что SaveChanges: в открытой транзакции после её коммита, иначе сразу.
    /// Контекст без перехватчика (собран в обход DI — миграции, тесты) ничего не сбрасывает: кэша
    /// у него нет.
    /// </summary>
    public static Task InvalidateTableCacheAsync<TEntity>(this DbContext db) where TEntity : class
    {
        // Имя — из модели EF, а не литералом: иначе метка сброса разойдётся с меткой, которую
        // запись кэша получила из SQL, и сброс молча промахнётся.
        var table = db.Model.FindEntityType(typeof(TEntity))?.GetTableName()
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} не отображён на таблицу в модели {db.GetType().Name}");
        return Interceptor(db)?.InvalidateTableAfterCommitAsync(db, table) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Сбросить служебные колонки после массовой записи ТОЛЬКО их мимо трекера (стирание
    /// объединённых мест в пересчёте) — <c>col:T.C</c> вместо всей таблицы, пока включена точность
    /// по колонкам (docs/plans/cache-row-precision-plan.md §2.6):
    /// <code>await _db.InvalidateColumnsCacheAsync&lt;ResultRecord&gt;(nameof(ResultRecord.CombinedPlace));</code>
    /// Свойство не из реестра <see cref="CacheServiceColumns"/> — исключение: его <c>col:</c> никто
    /// не носит. Запись задела и обычные колонки — это <see cref="InvalidateTableCacheAsync"/>.
    /// </summary>
    public static Task InvalidateColumnsCacheAsync<TEntity>(this DbContext db, params string[] properties)
        where TEntity : class
    {
        var type = db.Model.FindEntityType(typeof(TEntity));
        var table = type?.GetTableName()
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} не отображён на таблицу в модели {db.GetType().Name}");
        if (properties.Length == 0) throw new ArgumentException("Не названа ни одна колонка", nameof(properties));

        // Проверка — всегда, и без перехватчика: ошибка в списке должна всплыть в любом тесте пути.
        var service = CacheServiceColumns.Of(db.Model);
        var columns = properties.Select(name =>
                type!.FindProperty(name) is { } p && service.Columns.TryGetValue(p, out var column)
                    ? column
                    : throw new InvalidOperationException(
                        $"{typeof(TEntity).Name}.{name} не служебная колонка (CacheServiceColumns): " +
                        "массовую запись обычных колонок сбрасывают InvalidateTableCacheAsync"))
            .ToList();

        return Interceptor(db)?.InvalidateColumnsAfterCommitAsync(db, type!, table, columns) ?? Task.CompletedTask;
    }

    private static CacheInvalidationInterceptor? Interceptor(DbContext db) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors?
            .OfType<CacheInvalidationInterceptor>().FirstOrDefault();
}

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Data;

/// <summary>
/// Сужение чтения до строк корня (docs/plans/cache-row-precision-plan.md §2.3, К4б.3):
/// <code>
/// // Запросы внутри блока читают из этих таблиц ТОЛЬКО строки группы groupId.
/// using (_read.CacheRows&lt;HubGroup&gt;(groupId, typeof(HubGroupMember), typeof(HubGroupMedia)))
/// {
///     ...
/// }
/// </code>
/// Внутри блока SQL, читающий перечисленные таблицы или сам корень, даёт записи кэша
/// <c>row:HubGroups:{id}</c> + <c>anyrow:T</c> вместо <c>table:T</c>; прочие таблицы того же SQL —
/// как раньше. Правка чужой строки сбрасывает <c>table:T</c> и чужую <c>row:</c> — суженную запись
/// не задевает ни то, ни другое.
///
/// ⚠ ПРАВИЛО — единственное, на чём держится корректность. Таблицу можно перечислить, только
/// если КАЖДЫЙ запрос блока, который её читает, фильтрует её строки по FK на корень
/// (<c>m.HubGroupId == id</c>) или по id корня, и никакой другой экземпляр этой таблицы в том же
/// SQL чужие строки не читает. JOIN не сужают — делят запрос; id корня — ДО запроса (поиск по slug
/// внутри блока не сузить); потомок — только прямой. Нарушение — недосброс: страница врёт до
/// конца TTL. Ловят его сценарные тесты (§4-3) и сверка на попадании (§4-6); каждое место
/// сужения обязано стоять в списке сценарного теста (страж §4-4).
///
/// По модели проверяется при каждом открытии блока (результат кэшируется): корень — в
/// <see cref="CacheRowRoots"/>, у каждой перечисленной таблицы прямой FK на корень. Нарушение —
/// исключение, в тестах всплывает сразу, в том числе при выключенном сужении. Вне сборки записи
/// кэша блок пустой: тот же метод зовут и некэшированные пути.
/// </summary>
public static class CacheRowsExtensions
{
    private static readonly ConditionalWeakTable<IModel, ConcurrentDictionary<string, Plan>> Plans = new();

    /// <summary>Проверенный блок: таблица корня и суженные таблицы (корень среди них).</summary>
    private sealed record Plan(string RootTable, IReadOnlySet<string> Tables);

    /// <summary>Блок сужения до одной строки корня (страница одной группы, одного клуба).</summary>
    public static IDisposable CacheRows<TRoot>(this DbContext db, int id, params Type[] tables) where TRoot : class =>
        db.CacheRows<TRoot>([id], tables);

    /// <summary>Блок сужения до строк корня с этими id (медиа публикаций группы — по списку).</summary>
    public static IDisposable CacheRows<TRoot>(this DbContext db, IEnumerable<int> ids, params Type[] tables)
        where TRoot : class
    {
        var plan = PlanOf(db.Model, typeof(TRoot), tables);
        if (CacheBuildScope.Current is not { } scope) return NoBlock.Instance;
        return scope.Narrow(plan.RootTable, ids.Distinct().Select(i => (long)i).ToArray(), plan.Tables);
    }

    private static Plan PlanOf(IModel model, Type root, Type[] tables)
    {
        var key = root.FullName + "|" + string.Join("|", tables.Select(t => t.FullName));
        return Plans.GetOrCreateValue(model).GetOrAdd(key, _ => Check(model, root, tables));
    }

    private static Plan Check(IModel model, Type root, Type[] tables)
    {
        if (!CacheRowRoots.Types.Contains(root))
            throw new InvalidOperationException(
                $"{root.Name} не корень сужения кэша (CacheRowRoots): метку его строки запись в базу не сбрасывает");
        var rootTable = TableOf(model, root);

        var names = new HashSet<string>(StringComparer.Ordinal) { rootTable };
        foreach (var type in tables)
        {
            var table = TableOf(model, type);
            if (type != root && !model.FindEntityType(type)!.GetForeignKeys().Any(fk =>
                    fk.PrincipalEntityType.ClrType == root && fk.Properties.Count == 1 && fk.PrincipalKey.IsPrimaryKey()))
                throw new InvalidOperationException(
                    $"У {type.Name} нет FK на {root.Name}: сузить её чтение до строк {root.Name} нельзя — " +
                    "метку row: сбрасывают только прямые потомки корня");
            names.Add(table);
        }
        return new Plan(rootTable, names);
    }

    private static string TableOf(IModel model, Type type) =>
        model.FindEntityType(type)?.GetTableName()
        ?? throw new InvalidOperationException($"{type.Name} не отображён на таблицу в модели");

    private sealed class NoBlock : IDisposable
    {
        public static readonly NoBlock Instance = new();
        public void Dispose() { }
    }
}

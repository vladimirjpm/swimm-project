namespace Swimm.Application.Constants;

/// <summary>
/// Метки кэша — из каких данных собрана запись (docs/plans/cache-tags-plan.md §3.1).
///
/// Единственное место, где метки рождаются: строка-литерал по месту разошлась бы опечаткой
/// между тем, кто кладёт запись, и тем, кто её сбрасывает, — и сброс молча промахнулся бы.
///
/// Точность — «таблица» или «таблица + id»; по колонкам не дробим (решение плана §3.3):
/// лишний сброс дешевле, чем схема, в которой легко забыть зависимость.
/// </summary>
public static class CacheTags
{
    /// <summary>Есть у каждой записи неявно. Её сброс = <c>InvalidateAllAsync</c>.</summary>
    public const string All = "all";

    /// <summary>Любые данные таблицы: <c>table:Records</c>. Имя — как в БД (имя таблицы EF).</summary>
    public static string Table(string table) => $"table:{table}";

    /// <summary>Одна строка таблицы: <c>row:HubGroups:24</c> — страница одной группы, а не все.</summary>
    public static string Row(string table, long id) => $"row:{table}:{id}";
}

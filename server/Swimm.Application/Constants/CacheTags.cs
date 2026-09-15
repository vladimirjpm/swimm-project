namespace Swimm.Application.Constants;

/// <summary>
/// Метки кэша — из каких данных собрана запись (docs/plans/cache-tags-plan.md §3.1).
///
/// Единственное место, где метки рождаются: строка-литерал по месту разошлась бы опечаткой
/// между тем, кто кладёт запись, и тем, кто её сбрасывает, — и сброс молча промахнулся бы.
///
/// Точность — «таблица» или «строка корня» (docs/plans/cache-row-precision-plan.md §2.1); по
/// колонкам не дробим (решение плана §3.3): лишний сброс дешевле, чем схема, в которой легко
/// забыть зависимость. Единственное исключение — короткий реестр служебных колонок
/// (<see cref="Column"/>, §2.6 того же плана): их пишут часто, а читают единицы.
/// </summary>
public static class CacheTags
{
    /// <summary>Есть у каждой записи неявно. Её сброс = <c>InvalidateAllAsync</c>.</summary>
    public const string All = "all";

    /// <summary>Любые данные таблицы: <c>table:Records</c>. Имя — как в БД (имя таблицы EF).</summary>
    public static string Table(string table) => $"table:{table}";

    private const string RowPrefix = "row:";
    private const string AnyRowPrefix = "anyrow:";
    private const string ColumnPrefix = "col:";

    /// <summary>
    /// Строка корня или её прямой потомок: <c>row:HubGroups:24</c> — изменилась группа 24, её
    /// участник, медиа… Страница одной группы зависит от неё, а не от всех групп.
    /// </summary>
    public static string Row(string table, long id) => $"{RowPrefix}{table}:{id}";

    /// <summary>
    /// Изменились строки таблицы, но какие — неизвестно: <c>anyrow:HubGroupMembers</c> (массовая
    /// запись, каскад в базе, сжатие большого сохранения). Её носят записи, сузившие чтение
    /// таблицы до своих строк, — вместе с <see cref="Row"/>.
    /// </summary>
    public static string AnyRow(string table) => $"{AnyRowPrefix}{table}";

    /// <summary>
    /// Метка точнее таблицы (<see cref="Row"/>, <see cref="AnyRow"/>). Её носит только запись,
    /// сузившая чтение до строк, — такие записи и сверяет попадание
    /// (docs/plans/cache-row-precision-plan.md §4-6).
    /// </summary>
    public static bool IsRowLevel(string tag) =>
        tag.StartsWith(RowPrefix, StringComparison.Ordinal) || tag.StartsWith(AnyRowPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Служебная колонка таблицы: <c>col:Swimmers.LogligId</c> — изменилась только она (loglig-
    /// привязка пловца), остальные данные строки те же. Её носят записи, в SQL которых эта колонка
    /// названа; правка одной служебной колонки <see cref="Table"/> не сбрасывает
    /// (docs/plans/cache-row-precision-plan.md §2.6). Имена — как в БД.
    /// </summary>
    public static string Column(string table, string column) => $"{ColumnPrefix}{table}.{column}";

    /// <summary>Метка служебной колонки (<see cref="Column"/>).</summary>
    public static bool IsColumn(string tag) => tag.StartsWith(ColumnPrefix, StringComparison.Ordinal);

    // ── Метки страниц — для ручного сброса из админки ────────────────────────────────────
    // Данные они не описывают: запись в базу через сайт сбрасывает кэш сама метками таблиц,
    // строк и колонок. Эти нужны, когда данные поменяли мимо этого процесса API (правка в базе
    // руками, `dotnet run -- --флаг`, другой экземпляр) — сброс нужен точечный, а не весь кэш.

    /// <summary>Все страницы клубов (обзор, состав, season-best клуба, стена рекордов) — кнопка «все клубы».</summary>
    public const string ClubPages = "page:clubs";

    /// <summary>Страницы одного клуба: <c>page:club:438</c> — кнопка «этот клуб» в табе Admin клуба.</summary>
    public static string ClubPage(int clubId) => $"page:club:{clubId}";

    /// <summary>
    /// Метки, которые несёт КАЖДАЯ запись страницы клуба (<c>ClubsPublicController</c>): все клубы и
    /// этот, по id клуба-приёмника (склеенный клуб отдаёт страницы приёмника).
    /// </summary>
    public static string[] ClubPageTags(int clubId) => [ClubPages, ClubPage(clubId)];
}

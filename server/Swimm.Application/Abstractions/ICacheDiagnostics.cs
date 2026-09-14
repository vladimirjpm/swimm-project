namespace Swimm.Application.Abstractions;

/// <summary>
/// Что сейчас лежит в кэше и с какими метками — для страницы /Admin/Cache.
///
/// Нужна, чтобы метки можно было проверить глазами: запись без меток таблиц сбрасывается только
/// общим сбросом, а запись в базу с К4 сбрасывает лишь метки своих таблиц — такая запись врала бы
/// до конца TTL. Реализация на Redis отдаст то же сканом ключей — это админская диагностика, не
/// горячий путь.
/// </summary>
public interface ICacheDiagnostics
{
    IReadOnlyList<CacheEntryInfo> Snapshot();

    /// <summary>
    /// Журнал сбросов (docs/plans/cache-row-precision-plan.md, К4б.1): кто что сбросил и какие
    /// записи это выкинуло, плюс счётчики «кого выкидывают» с запуска процесса. Им меряют,
    /// окупается ли точность сброса, и принимают её вживую. У каждого экземпляра сервера свой.
    /// </summary>
    CacheJournal Journal();
}

/// <summary>Запись кэша: ключ, метки (без неявной <c>all</c>), тип значения и срок жизни.</summary>
public sealed record CacheEntryInfo(
    string Key,
    IReadOnlyList<string> Tags,
    string ValueType,
    DateTimeOffset StoredAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Журнал сбросов. <paramref name="Events"/> — последние сбросы, выкинувшие хоть одну запись,
/// свежие первыми; пустые (метку никто не носил) не пишутся, их только считают
/// (<paramref name="EmptyCount"/>). <paramref name="Drops"/> — сколько записей каждого вида
/// выкинуто с <paramref name="Since"/>, по убыванию. <paramref name="HitChecks"/> — сверка на
/// попадании.
/// </summary>
public sealed record CacheJournal(
    DateTimeOffset Since,
    IReadOnlyList<CacheInvalidationEvent> Events,
    long EmptyCount,
    IReadOnlyList<CacheDropStats> Drops,
    CacheHitChecks HitChecks);

/// <summary>
/// Сверка на попадании (docs/plans/cache-row-precision-plan.md §4-6): сколько попаданий в записи,
/// суженные до строк, построено заново мимо кэша (<paramref name="Checked"/>) и сколько из них не
/// совпало с кэшем (<paramref name="Mismatched"/>). Расхождение — «подозрение на недосброс»:
/// запись врала, её выкинули. <paramref name="Mismatches"/> — последние, свежие первыми.
/// </summary>
public sealed record CacheHitChecks(long Checked, long Mismatched, IReadOnlyList<CacheHitMismatch> Mismatches);

/// <summary>Одно расхождение: ключ, метки записи (строки первыми) и где JSON из кэша разошёлся с собранным заново.</summary>
public sealed record CacheHitMismatch(DateTimeOffset At, string Key, IReadOnlyList<string> Tags, string Difference);

/// <summary>
/// Один сброс: когда, кто и почему (<paramref name="Reason"/>), какие метки, сколько живых
/// записей выкинуто и первые из их ключей. <paramref name="All"/> — общий сброс (метка all).
/// </summary>
public sealed record CacheInvalidationEvent(
    DateTimeOffset At,
    string Reason,
    IReadOnlyList<string> Tags,
    bool All,
    int DroppedCount,
    IReadOnlyList<string> DroppedKeys);

/// <summary>
/// Вид записи кэша (<c>http:swimmer</c>, <c>swimmer-profile</c>…) и сколько раз записи этого вида
/// выкидывал сброс по меткам и общий сброс. Отвечает на вопрос плана К4б.7: часто ли страницы
/// пловца падают НЕ от импорта.
/// </summary>
public sealed record CacheDropStats(string Kind, long ByTags, long ByAll);

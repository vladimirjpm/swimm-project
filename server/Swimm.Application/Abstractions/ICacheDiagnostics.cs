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
/// выкинуто с <paramref name="Since"/>, по убыванию.
/// </summary>
public sealed record CacheJournal(
    DateTimeOffset Since,
    IReadOnlyList<CacheInvalidationEvent> Events,
    long EmptyCount,
    IReadOnlyList<CacheDropStats> Drops);

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

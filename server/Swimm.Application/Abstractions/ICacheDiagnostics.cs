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
}

/// <summary>Запись кэша: ключ, метки (без неявной <c>all</c>), тип значения и срок жизни.</summary>
public sealed record CacheEntryInfo(
    string Key,
    IReadOnlyList<string> Tags,
    string ValueType,
    DateTimeOffset StoredAt,
    DateTimeOffset ExpiresAt);

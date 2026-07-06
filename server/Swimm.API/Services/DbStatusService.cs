namespace Swimm.API.Services;

/// <summary>
/// Singleton-сервис, хранящий актуальный статус доступности БД.
/// Обновляется из <see cref="BackgroundServices.DbPingBackgroundService"/> (каждые 30 сек)
/// и из <see cref="Security.CookieSecurityStampValidator"/> (при каждом auth-запросе).
/// </summary>
public sealed class DbStatusService
{
    // volatile — читается из любого потока без блокировки
    private volatile bool _isAvailable = true;

    /// <summary>true — БД доступна, false — недоступна.</summary>
    public bool IsAvailable => _isAvailable;

    public void MarkAvailable()   => _isAvailable = true;
    public void MarkUnavailable() => _isAvailable = false;
}

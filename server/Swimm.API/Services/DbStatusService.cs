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
    private DbPingState? _lastPing;

    /// <summary>true — БД доступна, false — недоступна.</summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>Последний пинг фоновой службы (/Admin/Jobs); null — ещё не пинговали.</summary>
    public DbPingState? LastPing => Volatile.Read(ref _lastPing);

    public void MarkAvailable()   => _isAvailable = true;
    public void MarkUnavailable() => _isAvailable = false;

    /// <summary>Итог пинга <see cref="BackgroundServices.DbPingBackgroundService"/>: флаг и запись для страницы.</summary>
    public void RecordPing(bool ok, string? error = null)
    {
        if (ok) MarkAvailable(); else MarkUnavailable();
        var prev = LastPing;
        var now = DateTimeOffset.UtcNow;
        Volatile.Write(ref _lastPing, new DbPingState(
            now, ok,
            ok ? now : prev?.LastOkAt,
            ok ? prev?.LastError : error ?? "CanConnect вернул false",
            ok ? prev?.LastErrorAt : now,
            (prev?.Pings ?? 0) + 1,
            (prev?.Failures ?? 0) + (ok ? 0 : 1)));
    }
}

/// <summary>Пинг БД: последний, последний удачный, последняя ошибка и счётчики с запуска процесса.</summary>
public sealed record DbPingState(
    DateTimeOffset At, bool Ok, DateTimeOffset? LastOkAt, string? LastError, DateTimeOffset? LastErrorAt,
    long Pings, long Failures);

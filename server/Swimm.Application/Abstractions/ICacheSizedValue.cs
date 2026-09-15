namespace Swimm.Application.Abstractions;

/// <summary>
/// Значение кэша, которое знает свой размер в JSON без сериализации. Так ответ API
/// (<c>CachedJson</c>) отдаёт длину уже готовой строки: мерить её заново — лишняя работа
/// (<see cref="ICacheDiagnostics.Snapshot"/>).
///
/// Реализовывать ЯВНО (<c>long ICacheSizedValue.SizeBytes =&gt; …</c>): открытое свойство
/// попало бы в JSON значения — в Redis лишнее поле.
/// </summary>
public interface ICacheSizedValue
{
    /// <summary>Байты JSON значения — сколько оно заняло бы в Redis.</summary>
    long SizeBytes { get; }
}

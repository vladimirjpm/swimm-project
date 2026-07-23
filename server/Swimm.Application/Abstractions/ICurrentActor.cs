namespace Swimm.Application.Abstractions;

/// <summary>
/// Кто выполняет текущее действие. Абстрагирует HTTP-контекст от слоёв Application/Infrastructure
/// (реализация с IHttpContextAccessor живёт в Swimm.API). Вне HTTP (CLI/фон) —
/// <see cref="UserId"/> null, <see cref="Name"/> = "cli".
/// </summary>
public interface ICurrentActor
{
    /// <summary>Id пользователя (claim NameIdentifier); null вне HTTP.</summary>
    int? UserId { get; }

    /// <summary>Снимок email/имени актора ("cli" вне HTTP).</summary>
    string Name { get; }

    /// <summary>IP актора, если доступен.</summary>
    string? IpAddress { get; }
}

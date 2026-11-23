using System.Security.Claims;
using Swimm.Application.Abstractions;

namespace Swimm.API.Services;

/// <summary>
/// Реализация <see cref="ICurrentActor"/> поверх HTTP-контекста. Живёт в API-слое,
/// т.к. знает про ASP.NET. Вне запроса (нет HttpContext) отдаёт «cli».
/// </summary>
public sealed class HttpCurrentActor(IHttpContextAccessor http) : ICurrentActor
{
    public int? UserId =>
        int.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public string Name =>
        http.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? http.HttpContext?.User.Identity?.Name
        ?? BackgroundName
        ?? "cli";

    /// <summary>
    /// Имя актора вне HTTP для фоновой службы, которая создала свой scope (например
    /// <c>auto:import</c> — доклейка промежуточных после импорта). Без него аудит фона писался бы
    /// как «cli» и не отличался бы от консольной команды.
    /// </summary>
    public string? BackgroundName { get; set; }

    public string? IpAddress =>
        http.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

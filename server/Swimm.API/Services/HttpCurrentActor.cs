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
        ?? "cli";

    public string? IpAddress =>
        http.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

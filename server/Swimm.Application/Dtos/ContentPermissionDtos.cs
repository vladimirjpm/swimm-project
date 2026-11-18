using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Действующие права пользователя на контент (<c>ContentPermissionRules</c>). Уходит в
/// <c>GET /auth/me</c> как <c>permissions</c> — клиент прячет кнопки «Add video» / «Publish»,
/// а не показывает их, чтобы потом отказать.
/// </summary>
public sealed class ContentPermissionsDto
{
    [JsonPropertyName("addPhotos")]
    public bool AddPhotos { get; init; }

    [JsonPropertyName("addVideos")]
    public bool AddVideos { get; init; }

    [JsonPropertyName("publish")]
    public bool Publish { get; init; }
}

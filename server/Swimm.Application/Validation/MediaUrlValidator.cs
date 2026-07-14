using System.Text.RegularExpressions;

namespace Swimm.Application.Validation;

/// <summary>
/// Валидация ссылок личного медиа (2A): статический хелпер, тестируется без контроллера.
/// Правила зафиксированы в docs/tasks/user-media-2a-sonnet.md п.4.
/// </summary>
public static class MediaUrlValidator
{
    private static readonly Regex YoutubeRx = new(
        @"(?:youtube\.com/(?:watch\?v=|shorts/|embed/)|youtu\.be/)([a-zA-Z0-9_-]+)",
        RegexOptions.Compiled);

    private static readonly Regex VimeoRx = new(
        @"vimeo\.com/(\d+)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ValidSourceTypes = new() { "youtube", "vimeo", "other" };
    private static readonly HashSet<string> ValidMediaTypes = new() { "image", "video" };

    /// <summary>true + error=null — валидно; false + error — причина отказа (для 400).</summary>
    public static bool TryValidate(string? mediaType, string? sourceType, string? url, out string? error)
    {
        if (string.IsNullOrWhiteSpace(mediaType) || !ValidMediaTypes.Contains(mediaType))
        {
            error = "media_type must be 'image' or 'video'";
            return false;
        }

        if (string.IsNullOrWhiteSpace(sourceType) || !ValidSourceTypes.Contains(sourceType))
        {
            error = "source_type must be 'youtube', 'vimeo' or 'other'";
            return false;
        }

        if (mediaType == "image" && sourceType != "other")
        {
            error = "media_type 'image' is only allowed with source_type 'other'";
            return false;
        }

        if (string.IsNullOrWhiteSpace(url) || url.Length > 1000)
        {
            error = "url is required and must be at most 1000 characters";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        {
            error = "url must be an absolute https URL";
            return false;
        }

        if (sourceType == "youtube" && !YoutubeRx.IsMatch(url))
        {
            error = "url does not look like a valid YouTube link";
            return false;
        }

        if (sourceType == "vimeo" && !VimeoRx.IsMatch(url))
        {
            error = "url does not look like a valid Vimeo link";
            return false;
        }

        error = null;
        return true;
    }
}

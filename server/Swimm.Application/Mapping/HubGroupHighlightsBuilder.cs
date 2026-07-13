using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Сборка ленты хайлайтов шапки группы (design_handoff_group_header) из уже
/// посчитанных агрегатов DTO: bests → record, standings → medals, gallery → video/photo.
/// Чистая функция над DTO — вызывается ПОСЛЕ заполнения Gallery (контроллер).
/// Состав и порядок задаёт сервер: клиент рендерит массив как есть.
/// </summary>
public static class HubGroupHighlightsBuilder
{
    public static List<HubGroupHighlightDto> Build(HubGroupDetailsDto dto)
    {
        var list = new List<HubGroupHighlightDto>();
        var overviewUrl = $"./groups.html?group={Uri.EscapeDataString(dto.Slug)}";

        // 1. Рекорд группы — лучший FINA-балл среди «рекордов группы» (bests).
        var best = dto.Bests
            .Where(b => b.Points > 0)
            .OrderByDescending(b => b.Points)
            .ThenBy(b => b.Date)
            .FirstOrDefault();
        if (best != null)
        {
            var swimmer = string.IsNullOrWhiteSpace(best.SwimmerNameEn) ? best.SwimmerName : best.SwimmerNameEn;
            list.Add(new HubGroupHighlightDto
            {
                Type = "record",
                Badge = "Group best",
                Title = $"{swimmer} · {best.Distance}m {best.StyleName}",
                Detail = $"{best.TimeOriginal} · {best.Points} FINA",
                Url = overviewUrl + "#records"
            });
        }

        // 2. Медали сезона — суммы по зачёту; карточка скрыта, если сезон без медалей.
        var gold = dto.Standings.Sum(s => s.Golds);
        var silver = dto.Standings.Sum(s => s.Silvers);
        var bronze = dto.Standings.Sum(s => s.Bronzes);
        if (gold + silver + bronze > 0)
        {
            list.Add(new HubGroupHighlightDto
            {
                Type = "medals",
                Badge = $"Season {dto.SeasonLabel}",
                Place = dto.Standings.Sum(s => s.ClubPoints).ToString(),
                PlaceLabel = "club points",
                Gold = gold,
                Silver = silver,
                Bronze = bronze,
                Url = overviewUrl + "#standings"
            });
        }

        // 3. Последнее видео галереи (галерея отсортирована сервисом: свежие сверху).
        var video = dto.Gallery.FirstOrDefault(m => m.MediaType == "video");
        if (video != null)
        {
            list.Add(new HubGroupHighlightDto
            {
                Type = "video",
                Label = string.IsNullOrWhiteSpace(video.Caption) ? "Latest video" : video.Caption,
                ThumbUrl = YoutubeThumb(video.Url),
                Url = video.Url
            });
        }

        // 4. Фото-превью + «вся галерея +N» (N — остальные элементы галереи).
        var photo = dto.Gallery.FirstOrDefault(m => m.MediaType == "image");
        if (photo != null)
        {
            var restCount = dto.Gallery.Count - 1;
            list.Add(new HubGroupHighlightDto
            {
                Type = "photo",
                Label = string.IsNullOrWhiteSpace(photo.Caption) ? "Photo gallery" : photo.Caption,
                Extra = restCount > 0 ? $"+{restCount}" : null,
                ThumbUrl = photo.Url,
                Url = overviewUrl + "#gallery"
            });
        }

        return list;
    }

    /// <summary>
    /// Превью YouTube по id ролика (медиа хранится ссылками, своих превью нет).
    /// Для прочих источников превью нет — карточка рендерит плейсхолдер.
    /// </summary>
    internal static string? YoutubeThumb(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        string? id = null;
        if (host is "youtu.be")
            id = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault();
        else if (host.EndsWith("youtube.com"))
        {
            if (uri.AbsolutePath == "/watch")
                id = uri.Query.TrimStart('?').Split('&')
                    .Select(p => p.Split('=', 2))
                    .Where(kv => kv.Length == 2 && kv[0] == "v")
                    .Select(kv => Uri.UnescapeDataString(kv[1]))
                    .FirstOrDefault();
            else if (uri.AbsolutePath.StartsWith("/shorts/") || uri.AbsolutePath.StartsWith("/embed/"))
                id = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault();
        }
        return string.IsNullOrWhiteSpace(id) ? null : $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
    }
}

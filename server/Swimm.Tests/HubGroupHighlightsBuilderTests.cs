using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="HubGroupHighlightsBuilder"/> — сборка ленты хайлайтов шапки группы
/// (design_handoff_group_header): выбор рекорда по FINA, суммирование медалей,
/// video/photo из галереи, порядок карточек и пустая лента (обратная совместимость).
/// </summary>
public class HubGroupHighlightsBuilderTests
{
    private static HubGroupDetailsDto EmptyGroup() => new() { Slug = "dolphins" };

    private static HubGroupBestDto Best(int points, string swimmerEn = "Cohen A", string time = "1:00.00") => new()
    {
        StyleName = "Freestyle",
        Distance = "100",
        SwimmerName = "Коэн А",
        SwimmerNameEn = swimmerEn,
        TimeOriginal = time,
        Points = points,
        Date = "01/10/2025",
    };

    [Fact]
    public void EmptyGroup_ProducesEmptyList()
    {
        Assert.Empty(HubGroupHighlightsBuilder.Build(EmptyGroup()));
    }

    [Fact]
    public void Record_PicksHighestFinaAndFormatsCard()
    {
        var dto = EmptyGroup();
        dto.Bests.Add(Best(points: 512));
        dto.Bests.Add(Best(points: 730, swimmerEn: "Levi B", time: "0:55.10"));
        dto.Bests.Add(Best(points: 0)); // без баллов — не участвует

        var card = Assert.Single(HubGroupHighlightsBuilder.Build(dto));
        Assert.Equal("record", card.Type);
        Assert.Equal("Levi B · 100m Freestyle", card.Title);
        Assert.Equal("0:55.10 · 730 FINA", card.Detail);
        Assert.Equal("./groups.html?group=dolphins#records", card.Url);
    }

    [Fact]
    public void Record_FallsBackToHebrewNameWhenNoEnglish()
    {
        var dto = EmptyGroup();
        dto.Bests.Add(Best(points: 600, swimmerEn: ""));

        var card = Assert.Single(HubGroupHighlightsBuilder.Build(dto));
        Assert.StartsWith("Коэн А ·", card.Title);
    }

    [Fact]
    public void Medals_SumsStandingsAcrossSwimmers()
    {
        var dto = EmptyGroup();
        dto.SeasonLabel = "2025/26";
        dto.Standings.Add(new HubGroupStandingDto { Golds = 2, Silvers = 1, Bronzes = 0, ClubPoints = 40 });
        dto.Standings.Add(new HubGroupStandingDto { Golds = 0, Silvers = 3, Bronzes = 5, ClubPoints = 25 });

        var card = Assert.Single(HubGroupHighlightsBuilder.Build(dto));
        Assert.Equal("medals", card.Type);
        Assert.Equal("Season 2025/26", card.Badge);
        Assert.Equal(2, card.Gold);
        Assert.Equal(4, card.Silver);
        Assert.Equal(5, card.Bronze);
        Assert.Equal("65", card.Place);
        Assert.Equal("club points", card.PlaceLabel);
    }

    [Fact]
    public void Medals_HiddenWhenSeasonHasNoMedals()
    {
        var dto = EmptyGroup();
        dto.Standings.Add(new HubGroupStandingDto { Swims = 10, ClubPoints = 0 });

        Assert.Empty(HubGroupHighlightsBuilder.Build(dto));
    }

    [Fact]
    public void VideoAndPhoto_ComeFromGalleryWithExtraCount()
    {
        var dto = EmptyGroup();
        dto.Gallery.Add(new HubGroupMediaDto
        {
            MediaType = "video",
            Url = "https://www.youtube.com/watch?v=abc123XYZ_-",
            Caption = "Relay final",
        });
        dto.Gallery.Add(new HubGroupMediaDto { MediaType = "image", Url = "https://example.com/1.jpg" });
        dto.Gallery.Add(new HubGroupMediaDto { MediaType = "image", Url = "https://example.com/2.jpg" });

        var cards = HubGroupHighlightsBuilder.Build(dto);
        Assert.Equal(2, cards.Count);

        var video = cards[0];
        Assert.Equal("video", video.Type);
        Assert.Equal("Relay final", video.Label);
        Assert.Equal("https://img.youtube.com/vi/abc123XYZ_-/hqdefault.jpg", video.ThumbUrl);
        Assert.Equal("https://www.youtube.com/watch?v=abc123XYZ_-", video.Url);

        var photo = cards[1];
        Assert.Equal("photo", photo.Type);
        Assert.Equal("Photo gallery", photo.Label); // без caption — дефолтная подпись
        Assert.Equal("+2", photo.Extra);            // остальная галерея сверх превью
        Assert.Equal("https://example.com/1.jpg", photo.ThumbUrl);
        Assert.Equal("./groups.html?group=dolphins#gallery", photo.Url);
    }

    [Fact]
    public void SinglePhoto_HasNoExtraChip()
    {
        var dto = EmptyGroup();
        dto.Gallery.Add(new HubGroupMediaDto { MediaType = "image", Url = "https://example.com/1.jpg" });

        var card = Assert.Single(HubGroupHighlightsBuilder.Build(dto));
        Assert.Null(card.Extra);
    }

    [Fact]
    public void FullFeed_KeepsServerOrder_RecordMedalsVideoPhoto()
    {
        var dto = EmptyGroup();
        dto.Bests.Add(Best(points: 700));
        dto.Standings.Add(new HubGroupStandingDto { Golds = 1 });
        dto.Gallery.Add(new HubGroupMediaDto { MediaType = "video", Url = "https://youtu.be/abc123XYZ_-" });
        dto.Gallery.Add(new HubGroupMediaDto { MediaType = "image", Url = "https://example.com/1.jpg" });

        Assert.Equal(
            new[] { "record", "medals", "video", "photo" },
            HubGroupHighlightsBuilder.Build(dto).Select(h => h.Type).ToArray());
    }

    [Theory]
    [InlineData("https://youtu.be/abc123XYZ_-", "https://img.youtube.com/vi/abc123XYZ_-/hqdefault.jpg")]
    [InlineData("https://www.youtube.com/shorts/abc123XYZ_-", "https://img.youtube.com/vi/abc123XYZ_-/hqdefault.jpg")]
    [InlineData("https://www.youtube.com/embed/abc123XYZ_-", "https://img.youtube.com/vi/abc123XYZ_-/hqdefault.jpg")]
    [InlineData("https://vimeo.com/12345", null)]  // не-YouTube — превью нет, плейсхолдер на клиенте
    [InlineData("not a url", null)]
    public void YoutubeThumb_DerivedOnlyFromYoutubeUrls(string url, string? expected)
    {
        Assert.Equal(expected, HubGroupHighlightsBuilder.YoutubeThumb(url));
    }
}

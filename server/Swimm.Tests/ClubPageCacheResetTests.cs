using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Constants;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Ручной сброс кэша страниц клуба из админки: «этот клуб» (таб Admin страницы клуба) и «все
/// клубы» (/Admin/Cache). Каждая запись страницы клуба несёт <see cref="CacheTags.ClubPageTags"/>
/// (<c>ClubsPublicController</c>), кнопки сбрасывают <see cref="CacheTags.ClubPage"/> и
/// <see cref="CacheTags.ClubPages"/> (<c>ClubsAdminController</c>).
///
/// Контроллеры тесты не собирают (Swimm.API не в зависимостях — чтобы сборка тестов не упиралась
/// в запущенный API); их подключение проверено вживую. Здесь — метки и то, что сбрасывает каждая.
/// </summary>
public class ClubPageCacheResetTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private sealed record Payload(string Value);

    private static MemoryCacheService Cache() => new(new MemoryCache(new MemoryCacheOptions()));

    /// <summary>Запись как у контроллера: табличная метка из «SQL» и явные метки страницы.</summary>
    private static Task Page(MemoryCacheService cache, string key, params string[] tags) =>
        cache.GetOrCreateAsync(key, () =>
        {
            CacheBuildScope.Current!.TouchTable("Results");
            return Task.FromResult(new Payload(key));
        }, Ttl, tags);

    /// <summary>Страницы двух клубов, страница группы и season-best страны.</summary>
    private static async Task BuildAsync(MemoryCacheService cache)
    {
        foreach (var club in new[] { 438, 482 })
        foreach (var page in new[] { "overview", "roster", "season-best", "record-wall" })
            await Page(cache, $"http:clubs:{club}:{page}", CacheTags.ClubPageTags(club));
        await Page(cache, "http:hub-groups:group:g24");
        await Page(cache, "http:season-best:table:cur");
    }

    private static string[] Live(MemoryCacheService cache) =>
        [.. cache.Snapshot().Select(e => e.Key).Order(StringComparer.Ordinal)];

    [Fact]
    public void ClubPageTags_AreAllClubsAndThisClub()
    {
        Assert.Equal(["page:clubs", "page:club:438"], CacheTags.ClubPageTags(438));
        Assert.Equal(CacheTags.ClubPages, CacheTags.ClubPageTags(438)[0]);
        Assert.Equal(CacheTags.ClubPage(438), CacheTags.ClubPageTags(438)[1]);
    }

    [Fact]
    public async Task ThisClub_DropsOnlyItsPages()
    {
        var cache = Cache();
        await BuildAsync(cache);

        await cache.InvalidateTagsAsync([CacheTags.ClubPage(438)], "кнопка «Сбросить кэш клуба» #438");

        Assert.Equal(
            [
                "http:clubs:482:overview", "http:clubs:482:record-wall", "http:clubs:482:roster",
                "http:clubs:482:season-best", "http:hub-groups:group:g24", "http:season-best:table:cur",
            ],
            Live(cache));
    }

    [Fact]
    public async Task AllClubs_DropsEveryClubPage_TheRestLives()
    {
        var cache = Cache();
        await BuildAsync(cache);

        await cache.InvalidateTagsAsync([CacheTags.ClubPages], "кнопка «Сбросить кэш всех клубов»");

        // Группы и season-best страны — не страницы клубов, общий сброс для этого не нужен.
        Assert.Equal(["http:hub-groups:group:g24", "http:season-best:table:cur"], Live(cache));
    }

    [Fact]
    public async Task PageTags_AreExtra_TheDataTagsStillDropThePage()
    {
        var cache = Cache();
        await BuildAsync(cache);

        // Правка через сайт по-прежнему сбрасывает страницы клуба своими метками данных.
        await cache.InvalidateTagsAsync(CacheTags.Table("Results"));

        Assert.Empty(Live(cache));
    }

    [Fact]
    public async Task Journal_SignsTheButton()
    {
        var cache = Cache();
        await BuildAsync(cache);

        await cache.InvalidateTagsAsync([CacheTags.ClubPage(482)], "кнопка «Сбросить кэш клуба» #482");

        var e = Assert.Single(cache.Journal().Events);
        Assert.Equal("кнопка «Сбросить кэш клуба» #482", e.Reason);
        Assert.Equal(4, e.DroppedCount);
        Assert.All(e.DroppedKeys, k => Assert.StartsWith("http:clubs:482:", k));
    }
}

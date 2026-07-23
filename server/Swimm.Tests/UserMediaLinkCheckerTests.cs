using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// On-demand проверка живости ссылок UserMedia (фаза 7.5). Сеть не задействуем — фейковый
/// HttpMessageHandler (см. SerperCandidateSearchProviderTests) отдаёт заданный ответ/ошибку.
/// </summary>
public class UserMediaLinkCheckerTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Отдаёт код по функции запроса; может бросать исключение (сетевая ошибка).</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ThrowingFakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Connection refused");
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static UserMedia Seed(SwimmDbContext db, string url, string mediaType, string sourceType)
    {
        var user = new AppUser { Email = "owner@test.com", DisplayName = "Owner" };
        var swimmer = new Swimmer { LastName = "Cohen", FirstName = "Dan" };
        var media = new UserMedia
        {
            User = user, Swimmer = swimmer, Url = url,
            MediaType = mediaType, SourceType = sourceType, Level = "swimmer",
        };
        db.AddRange(user, swimmer, media);
        db.SaveChanges();
        return media;
    }

    [Fact]
    public async Task CheckAllAsync_OtherLink_200_MarksOkWithStatusCode()
    {
        await using var db = CreateDb(nameof(CheckAllAsync_OtherLink_200_MarksOkWithStatusCode));
        var media = Seed(db, "https://example.com/photo.jpg", "image", "other");

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var checker = new UserMediaLinkChecker(db, new StubHttpClientFactory(handler), NullLogger<UserMediaLinkChecker>.Instance);

        var report = await checker.CheckAllAsync();

        Assert.Equal((Checked: 1, Ok: 1, Broken: 0), (report.Checked, report.Ok, report.Broken));
        var updated = await db.UserMedia.FindAsync(media.Id);
        Assert.True(updated!.LinkOk);
        Assert.Equal(200, updated.LinkStatusCode);
        Assert.Null(updated.LinkError);
        Assert.NotNull(updated.LinkCheckedAt);
    }

    [Fact]
    public async Task CheckAllAsync_OtherLink_404_MarksBroken()
    {
        await using var db = CreateDb(nameof(CheckAllAsync_OtherLink_404_MarksBroken));
        var media = Seed(db, "https://example.com/gone.jpg", "image", "other");

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var checker = new UserMediaLinkChecker(db, new StubHttpClientFactory(handler), NullLogger<UserMediaLinkChecker>.Instance);

        var report = await checker.CheckAllAsync();

        Assert.Equal(1, report.Broken);
        var updated = await db.UserMedia.FindAsync(media.Id);
        Assert.False(updated!.LinkOk);
        Assert.Equal(404, updated.LinkStatusCode);
    }

    [Fact]
    public async Task CheckAllAsync_NetworkError_MarksBrokenWithErrorAndTimestamp()
    {
        await using var db = CreateDb(nameof(CheckAllAsync_NetworkError_MarksBrokenWithErrorAndTimestamp));
        var media = Seed(db, "https://example.com/unreachable.jpg", "image", "other");

        var checker = new UserMediaLinkChecker(db, new StubHttpClientFactory(new ThrowingFakeHandler()), NullLogger<UserMediaLinkChecker>.Instance);

        var report = await checker.CheckAllAsync();

        Assert.Equal(1, report.Broken);
        var updated = await db.UserMedia.FindAsync(media.Id);
        Assert.False(updated!.LinkOk);
        Assert.Null(updated.LinkStatusCode);
        Assert.NotNull(updated.LinkError);
        Assert.NotNull(updated.LinkCheckedAt);
    }

    [Fact]
    public async Task CheckAllAsync_YoutubeLink_CallsOembedHost_200IsAlive()
    {
        await using var db = CreateDb(nameof(CheckAllAsync_YoutubeLink_CallsOembedHost_200IsAlive));
        var media = Seed(db, "https://www.youtube.com/watch?v=abc123", "video", "youtube");

        var handler = new FakeHandler(req =>
        {
            Assert.Equal("www.youtube.com", req.RequestUri!.Host);
            Assert.StartsWith("/oembed", req.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var checker = new UserMediaLinkChecker(db, new StubHttpClientFactory(handler), NullLogger<UserMediaLinkChecker>.Instance);

        var report = await checker.CheckAllAsync();

        Assert.Equal(1, report.Ok);
        var updated = await db.UserMedia.FindAsync(media.Id);
        Assert.True(updated!.LinkOk);
    }

    [Fact]
    public async Task GetBrokenAsync_OnlyReturnsBrokenRows_WithOwnerAndSwimmer()
    {
        await using var db = CreateDb(nameof(GetBrokenAsync_OnlyReturnsBrokenRows_WithOwnerAndSwimmer));
        var okUser = new AppUser { Email = "ok@test.com", DisplayName = "Ok" };
        var okSwimmer = new Swimmer { LastName = "Levi", FirstName = "Noa" };
        var okMedia = new UserMedia
        {
            User = okUser, Swimmer = okSwimmer, Url = "https://example.com/ok.jpg",
            MediaType = "image", SourceType = "other", Level = "swimmer",
            LinkOk = true, LinkCheckedAt = DateTime.UtcNow, LinkStatusCode = 200,
        };

        var brokenUser = new AppUser { Email = "broken@test.com", DisplayName = "Broken" };
        var brokenSwimmer = new Swimmer { LastName = "Cohen", FirstName = "Dan" };
        var brokenMedia = new UserMedia
        {
            User = brokenUser, Swimmer = brokenSwimmer, Url = "https://example.com/broken.jpg",
            MediaType = "image", SourceType = "other", Level = "swimmer",
            LinkOk = false, LinkCheckedAt = DateTime.UtcNow, LinkStatusCode = 404, LinkError = "HTTP 404",
        };

        db.AddRange(okUser, okSwimmer, okMedia, brokenUser, brokenSwimmer, brokenMedia);
        await db.SaveChangesAsync();

        var checker = new UserMediaLinkChecker(db, new StubHttpClientFactory(new ThrowingFakeHandler()), NullLogger<UserMediaLinkChecker>.Instance);

        var broken = await checker.GetBrokenAsync();

        var row = Assert.Single(broken);
        Assert.Equal(brokenMedia.Id, row.Id);
        Assert.Equal("broken@test.com", row.OwnerEmail);
        Assert.Equal("Cohen Dan", row.SwimmerName);
        Assert.Equal(404, row.LinkStatusCode);
        Assert.Equal("HTTP 404", row.LinkError);
    }
}

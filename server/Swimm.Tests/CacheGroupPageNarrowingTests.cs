using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Страница группы на блоках сужения (docs/plans/cache-row-precision-plan.md §3.1, К4б.4): метки
/// настоящей страницы (§4-2) и сценарии (§4-3) — правка группы B не роняет страницу A, правка A
/// роняет A, и так по каждому месту сужения страницы.
///
/// Страницы собираются из живого Postgres (только чтение; нет базы — пропуск) теми же шагами, что
/// фабрика <c>HubGroupsController.GetGroup</c>. Записи идут в InMemory с настоящей моделью: его
/// строки — зеркала строк базы с теми же id, и перехватчик сохранения сбрасывает их метки в тот же
/// кэш, где лежат страницы, — ровно те <c>row:</c>, что сбросила бы такая запись в базу. Живую базу
/// тесты не пишут.
///
/// ⚠ Без базы тесты молча зеленеют — новое утверждение проверяй мутацией (сломать сужение → тест
/// падает → вернуть).
/// </summary>
public class CacheGroupPageNarrowingTests
{
    private const string PgConn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>Клуб, которого нет в базе: от его строки не зависит ни одна страница.</summary>
    private const int NoSuchClub = 900_000_001;

    /// <summary>Группа из базы — то, что о ней нужно сценариям.</summary>
    private sealed record LiveGroup(int Id, string Slug, int? ClubId, int? FollowedClubId, IReadOnlyList<int> PublishedMediaIds)
    {
        public string Key => $"http:hub-groups:group:{Slug}";
    }

    /// <summary>
    /// Живая база (только чтение) и кэш с настоящими страницами всех групп и списком групп;
    /// записи — через <see cref="Mirror"/>. <see cref="OpenAsync"/> даёт null, если базы нет.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        private const string ListKey = "http:hub-groups:list";
        private static readonly CacheDependencyInterceptor Reads = new();

        private readonly SwimmReadDbContext _read;
        private readonly SwimmDbContext _rw;
        private readonly HubGroupPublicRepository _groups;
        private readonly HubGroupMediaService _media;
        private readonly UserMediaPublicationService _publications;
        private Dictionary<int, int> _mediaSwimmers = [];

        private World(SwimmReadDbContext read, SwimmDbContext rw, CacheSettingsStub settings)
        {
            _read = read;
            _rw = rw;
            _groups = new HubGroupPublicRepository(read, rw, settings);
            _media = new HubGroupMediaService(rw);
            _publications = new UserMediaPublicationService(rw);
            Cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()), settings);
        }

        public MemoryCacheService Cache { get; }
        public IReadOnlyList<LiveGroup> Groups { get; private set; } = [];

        /// <summary>Медиа без одобренной публикации ни в одной группе — ни одна страница его не читала.</summary>
        public int SpareMediaId { get; private set; }

        public static async Task<World?> OpenAsync(bool rowPrecision = true, int hitVerifyPercent = 0)
        {
            var read = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
                .UseNpgsql(PgConn)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .AddInterceptors(Reads)
                .Options);
            try
            {
                if (!await read.Database.CanConnectAsync()) { await read.DisposeAsync(); return null; }
            }
            catch { await read.DisposeAsync(); return null; } // нет базы — пропуск

            var rw = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
                .UseNpgsql(PgConn).AddInterceptors(Reads).Options);
            var world = new World(read, rw, new CacheSettingsStub(rowPrecision, hitVerifyPercent));
            await world.LoadAsync();
            return world;
        }

        private async Task LoadAsync()
        {
            // Вне сборки записи кэша — эти запросы ничьих меток не ставят.
            var groups = await _rw.HubGroups.AsNoTracking().OrderBy(g => g.Id)
                .Select(g => new { g.Id, g.Slug, g.ClubId })
                .ToListAsync();
            var follows = await _rw.HubGroupClubSubscriptions.AsNoTracking()
                .ToDictionaryAsync(s => s.HubGroupId, s => s.ClubId);
            var published = await _rw.UserMediaPublications.AsNoTracking()
                .Where(p => p.HubGroupId != null
                            && p.Status == UserMediaPublicationStatus.Approved
                            && p.Level == UserMediaPublicationLevel.Public)
                .Select(p => new { GroupId = p.HubGroupId!.Value, p.UserMediaId, p.Media!.SwimmerId })
                .ToListAsync();

            Groups = groups
                .Select(g => new LiveGroup(g.Id, g.Slug, g.ClubId, follows.TryGetValue(g.Id, out var club) ? club : null,
                    published.Where(p => p.GroupId == g.Id).Select(p => p.UserMediaId).ToList()))
                .ToList();
            _mediaSwimmers = published.DistinctBy(p => p.UserMediaId).ToDictionary(p => p.UserMediaId, p => p.SwimmerId);
            SpareMediaId = (published.Count > 0 ? published.Max(p => p.UserMediaId) : 0) + 1;

            await BuildAsync();
        }

        /// <summary>Собрать недостающие страницы групп и список — после сброса сценарию нужны все.</summary>
        public async Task BuildAsync()
        {
            foreach (var g in Groups) await PageAsync(g);
            await Cache.GetOrCreateAsync(ListKey, () => _groups.GetGroupsAsync(), Ttl);
        }

        /// <summary>Страница группы теми же шагами, что фабрика HubGroupsController.GetGroup (шапка и хайлайты — в памяти).</summary>
        public Task<HubGroupDetailsDto> PageAsync(LiveGroup g) =>
            Cache.GetOrCreateAsync(g.Key, async () =>
            {
                var dto = await _groups.GetPageAsync(g.Id, g.Slug)
                          ?? throw new InvalidOperationException($"группа {g.Id} пропала из базы");
                dto.Gallery = await _media.GetGalleryAsync(dto.Id);
                var published = await _publications.GetApprovedForGroupAsync(dto.Id, UserMediaPublicationLevel.Public);
                dto.Gallery.AddRange(published.Select(p => new HubGroupMediaDto
                {
                    Id = -p.Id,
                    MediaType = p.MediaType,
                    SourceType = p.SourceType,
                    Url = p.Url,
                    Caption = p.ResultLabel,
                }));
                return dto;
            }, Ttl);

        public async Task<bool> Cached(LiveGroup g) => await Cache.GetAsync<HubGroupDetailsDto>(g.Key) is not null;

        public async Task<bool> ListCached() =>
            await Cache.GetAsync<IReadOnlyList<HubGroupListItemDto>>(ListKey) is not null;

        public IReadOnlyList<string> TagsOf(LiveGroup g) => Cache.Snapshot().Single(e => e.Key == g.Key).Tags;

        /// <summary>
        /// Две группы, не связанные клубом: ни одна не официальная группа клуба, на который подписана
        /// другая. Только при такой связи правка одной законно роняет страницу другой — та
        /// показывает официальную группу своего клуба. null — таких двух в базе нет.
        /// </summary>
        public (LiveGroup A, LiveGroup B)? Pair()
        {
            foreach (var a in Groups)
            foreach (var b in Groups)
                if (a.Id < b.Id && !Linked(a, b) && !Linked(b, a)) return (a, b);
            return null;

            static bool Linked(LiveGroup official, LiveGroup follower) =>
                official.ClubId is { } club && club == follower.FollowedClubId;
        }

        /// <summary>
        /// Зеркало строк базы в InMemory: группы и медиа их публикаций с теми же id, плюс медиа без
        /// публикации (<see cref="SpareMediaId"/>). <paramref name="seed"/> досевает своё. Сев — без
        /// перехватчика: в сбросы он не попадает. Перехватчик возвращённого контекста сбрасывает
        /// метки в <see cref="Cache"/>.
        /// </summary>
        public SwimmDbContext Mirror(Action<SwimmDbContext>? seed = null, [CallerMemberName] string name = "")
        {
            using (var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options))
            {
                db.HubGroups.AddRange(Groups.Select(g => new HubGroup
                {
                    Id = g.Id, Name = g.Slug, Slug = g.Slug, ClubId = g.ClubId, OwnerUserId = 1,
                }));
                db.UserMedia.AddRange(_mediaSwimmers.Select(m => Media(m.Key, m.Value)));
                db.UserMedia.Add(Media(SpareMediaId, 1));
                seed?.Invoke(db);
                db.SaveChanges();
            }
            return new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
                .UseInMemoryDatabase(name)
                .AddInterceptors(new CacheInvalidationInterceptor(Cache))
                .Options);

            static UserMedia Media(int id, int swimmerId) => new()
            {
                Id = id, UserId = 1, SwimmerId = swimmerId, Level = "swimmer",
                MediaType = "image", SourceType = "other", Url = $"https://example.test/{id}.jpg",
            };
        }

        public async ValueTask DisposeAsync()
        {
            await _read.DisposeAsync();
            await _rw.DisposeAsync();
        }
    }

    // ── Метки настоящей страницы (§4-2) ──────────────────────────────────────────────

    [Fact]
    public async Task RealPage_DependsOnItsRows_NotOnTheGroupAndMediaTables()
    {
        await using var world = await World.OpenAsync();
        if (world == null || world.Groups.Count == 0) return; // нет базы или групп — пропуск

        foreach (var g in world.Groups)
        {
            var tags = world.TagsOf(g);
            Assert.Contains(CacheTags.Row("HubGroups", g.Id), tags);
            // Ни одной таблицы групп и медиа целиком — и нет пользователей (К4б.0).
            foreach (var table in new[]
                     {
                         "HubGroups", "HubGroupMembers", "HubGroupClubSubscriptions", "Sys_HubGroupMedia",
                         "Sys_UserMediaPublications", "Sys_UserMedia", "Sys_AppUsers",
                     })
                Assert.DoesNotContain(CacheTags.Table(table), tags);
            // Агрегаты по составу — таблицей: их пишут импорт и админ, эстафеты тянут чужих пловцов.
            Assert.Contains(CacheTags.Table("Swimmers"), tags);
            if (g.FollowedClubId is { } club) Assert.Contains(CacheTags.Row("Clubs", club), tags);
            foreach (var media in g.PublishedMediaIds) Assert.Contains(CacheTags.Row("Sys_UserMedia", media), tags);
        }
    }

    [Fact]
    public async Task SwitchOff_RealPage_DependsOnTheTables()
    {
        await using var world = await World.OpenAsync(rowPrecision: false);
        if (world == null || world.Groups.Count == 0) return;

        foreach (var g in world.Groups)
        {
            var tags = world.TagsOf(g);
            Assert.Contains(CacheTags.Table("HubGroups"), tags);
            Assert.Contains(CacheTags.Table("HubGroupMembers"), tags);
            Assert.DoesNotContain(tags, CacheTags.IsRowLevel);
        }
    }

    /// <summary>
    /// Сверка на попадании (§4-6) на настоящих страницах: собранная заново мимо кэша совпадает с
    /// лежащей — сужение не спрятало от страницы ни одного её чтения, а сама страница
    /// детерминирована (иначе сверка сыпала бы ложными подозрениями при прокликивании).
    /// </summary>
    [Fact]
    public async Task HitVerification_OnRealPages_FindsNoMismatch()
    {
        await using var world = await World.OpenAsync(hitVerifyPercent: 100);
        if (world == null || world.Groups.Count == 0) return;

        foreach (var g in world.Groups) await world.PageAsync(g); // попадание → сверка

        var checks = world.Cache.Journal().HitChecks;
        Assert.Equal(world.Groups.Count, checks.Checked);
        Assert.True(checks.Mismatched == 0, string.Join(" | ", checks.Mismatches.Select(m => $"{m.Key}: {m.Difference}")));
    }

    // ── Сценарии (§4-3) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditOfAnotherGroup_KeepsThePage_EditOfItsOwn_DropsIt_AndTheList()
    {
        await using var world = await World.OpenAsync();
        if (world?.Pair() is not ({ } a, { } b)) return; // нет базы или двух несвязанных групп
        await using var db = world.Mirror();

        foreach (var (edited, other) in new[] { (a, b), (b, a) })
        {
            await world.BuildAsync();
            // Как правка расписания владельцем: строка группы, её клуб по FK.
            var group = await db.HubGroups.SingleAsync(g => g.Id == edited.Id);
            group.TrainingSchedule = $"{{\"place\":\"pool {edited.Id}\"}}";
            await db.SaveChangesAsync();

            Assert.False(await world.Cached(edited));
            Assert.True(await world.Cached(other));
            Assert.False(await world.ListCached()); // список читает группы таблицей — так и надо
        }
    }

    [Fact]
    public async Task NewGroup_KeepsEveryPage_DropsTheList()
    {
        await using var world = await World.OpenAsync();
        if (world == null || world.Groups.Count == 0) return;
        await using var db = world.Mirror();

        db.HubGroups.Add(new HubGroup { Name = "new", Slug = "new-group-k4b4", OwnerUserId = 1 });
        await db.SaveChangesAsync();

        // От строки, которой ещё не было, не зависит ни одна страница.
        foreach (var g in world.Groups) Assert.True(await world.Cached(g));
        Assert.False(await world.ListCached());
    }

    [Fact]
    public async Task NewMember_DropsOnlyItsGroup()
    {
        await using var world = await World.OpenAsync();
        if (world?.Pair() is not ({ } a, { } b)) return;
        await using var db = world.Mirror();

        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = a.Id, SwimmerId = 1 });
        await db.SaveChangesAsync();

        Assert.False(await world.Cached(a));
        Assert.True(await world.Cached(b));
    }

    [Fact]
    public async Task MemberMovedBetweenGroups_DropsBoth()
    {
        await using var world = await World.OpenAsync();
        if (world?.Pair() is not ({ } a, { } b)) return;
        const int memberId = 1;
        await using var db = world.Mirror(seed => seed.HubGroupMembers.Add(
            new HubGroupMember { Id = memberId, HubGroupId = a.Id, SwimmerId = 1 }));

        var member = await db.HubGroupMembers.SingleAsync(m => m.Id == memberId);
        member.HubGroupId = b.Id; // старое и новое значение FK
        await db.SaveChangesAsync();

        Assert.False(await world.Cached(a));
        Assert.False(await world.Cached(b));
    }

    [Fact]
    public async Task Subscription_DropsOnlyItsGroup()
    {
        await using var world = await World.OpenAsync();
        if (world?.Pair() is not ({ } a, { } b)) return;
        await using var db = world.Mirror();

        db.HubGroupClubSubscriptions.Add(new HubGroupClubSubscription { HubGroupId = b.Id, ClubId = NoSuchClub });
        await db.SaveChangesAsync();

        Assert.False(await world.Cached(b));
        Assert.True(await world.Cached(a));
    }

    /// <summary>
    /// Официальная группа клуба, на который подписана группа, — строка ДРУГОЙ группы (Q3b): её
    /// страница сужена по клубу. Другую группу одобрили официальной для этого клуба — страница
    /// подписчика падает (теперь она показывает, где «лицо клуба»).
    /// </summary>
    [Fact]
    public async Task AnotherGroupBecomesOfficialForTheFollowedClub_DropsTheFollower()
    {
        await using var world = await World.OpenAsync();
        var follower = world?.Groups.FirstOrDefault(g => g.FollowedClubId != null);
        var other = world?.Groups.FirstOrDefault(g => g != follower);
        if (world == null || follower == null || other == null) return; // нет подписчика или второй группы
        await using var db = world.Mirror();

        var group = await db.HubGroups.SingleAsync(g => g.Id == other.Id);
        group.ClubId = follower.FollowedClubId;
        group.IsOfficial = true;
        await db.SaveChangesAsync();

        Assert.False(await world.Cached(follower));
    }

    [Fact]
    public async Task GroupMedia_DropsOnlyItsGroup()
    {
        await using var world = await World.OpenAsync();
        if (world?.Pair() is not ({ } a, { } b)) return;
        await using var db = world.Mirror();

        db.HubGroupMedia.Add(new HubGroupMedia
        {
            HubGroupId = b.Id, MediaType = "image", SourceType = "other",
            Url = "https://example.test/gallery.jpg", CreatedByUserId = 1,
        });
        await db.SaveChangesAsync();

        Assert.False(await world.Cached(b));
        Assert.True(await world.Cached(a));
    }

    [Fact]
    public async Task PublicationApproved_DropsOnlyTheTargetGroup()
    {
        await using var world = await World.OpenAsync();
        if (world?.Pair() is not ({ } a, { } b)) return;
        await using var db = world.Mirror();

        db.UserMediaPublications.Add(new UserMediaPublication
        {
            UserMediaId = world.SpareMediaId, HubGroupId = a.Id, TargetType = UserMediaPublicationTarget.Group,
            Level = UserMediaPublicationLevel.Public, Status = UserMediaPublicationStatus.Approved,
        });
        await db.SaveChangesAsync();

        Assert.False(await world.Cached(a));
        Assert.True(await world.Cached(b));
    }

    [Fact]
    public async Task MediaAddedWithoutPublication_KeepsEveryPage()
    {
        await using var world = await World.OpenAsync();
        if (world == null || world.Groups.Count == 0) return;
        await using var db = world.Mirror();

        // My media: запись есть, публикации нет — её строк ни одна страница не читала.
        db.UserMedia.Add(new UserMedia
        {
            UserId = 1, SwimmerId = 1, Level = "swimmer", MediaType = "video", SourceType = "youtube",
            Url = "https://example.test/new-video",
        });
        await db.SaveChangesAsync();

        foreach (var g in world.Groups) Assert.True(await world.Cached(g));
    }

    [Fact]
    public async Task EditOfPublishedMedia_DropsTheGroupThatShowsIt_KeepsTheOthers()
    {
        await using var world = await World.OpenAsync();
        var shows = world?.Groups.FirstOrDefault(g => g.PublishedMediaIds.Count > 0);
        if (world == null || shows == null) return; // публикаций в базе нет — пропуск
        var mediaId = shows.PublishedMediaIds[0];
        await using var db = world.Mirror();

        // Как проверка ссылки или правка подписи: строка медиа, её пловец по FK.
        var media = await db.UserMedia.SingleAsync(m => m.Id == mediaId);
        media.Url += "?v=2";
        await db.SaveChangesAsync();

        foreach (var g in world.Groups)
            Assert.Equal(!g.PublishedMediaIds.Contains(mediaId), await world.Cached(g));
    }

    [Fact]
    public async Task BulkWriteOfMembers_DropsEveryPage()
    {
        await using var world = await World.OpenAsync();
        if (world == null || world.Groups.Count == 0) return;
        await using var db = world.Mirror();

        // ExecuteUpdate/ExecuteDelete мимо трекера: какие строки задеты — неизвестно.
        await db.InvalidateTableCacheAsync<HubGroupMember>();

        foreach (var g in world.Groups) Assert.False(await world.Cached(g));
    }

    /// <summary>
    /// Чего план не лечит (§6): удаление медиа каскадит в публикации, а чьи они — трекер не
    /// знает (anyrow). Падают страницы всех групп, даже если у медиа публикаций не было.
    /// </summary>
    [Fact]
    public async Task MediaDeleted_DropsEveryPage()
    {
        await using var world = await World.OpenAsync();
        if (world == null || world.Groups.Count == 0) return;
        await using var db = world.Mirror();

        db.UserMedia.Remove(await db.UserMedia.SingleAsync(m => m.Id == world.SpareMediaId));
        await db.SaveChangesAsync();

        foreach (var g in world.Groups) Assert.False(await world.Cached(g));
    }
}

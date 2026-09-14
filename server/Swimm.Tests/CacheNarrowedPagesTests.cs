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
/// Страницы на блоках сужения (docs/plans/cache-row-precision-plan.md): группа (§3.1, К4б.4), обзор и
/// состав клуба (§3.2, К4б.5). Метки настоящих страниц (§4-2) и сценарии (§4-3) — правка группы B
/// не роняет страницу A, правка A роняет A, и так по каждому месту сужения.
///
/// Страницы собираются из живого Postgres (только чтение; нет базы — пропуск) теми же шагами, что
/// фабрики контроллеров (<c>HubGroupsController.GetGroup</c>, <c>ClubsPublicController</c>). Записи
/// идут в InMemory с настоящей моделью: его строки — зеркала строк базы с теми же id, и перехватчик
/// сохранения сбрасывает их метки в тот же кэш, где лежат страницы, — ровно те <c>row:</c>, что
/// сбросила бы такая запись в базу. Живую базу тесты не пишут.
///
/// ⚠ Без базы тесты молча зеленеют — новое утверждение проверяй мутацией (сломать сужение → тест
/// падает → вернуть). В коллекции «Живая база»: тесты, которые пишут в неё свои временные строки,
/// идут не параллельно с этими (<see cref="LiveDbCollection"/>).
/// </summary>
[Collection(LiveDbCollection.Name)]
public class CacheNarrowedPagesTests
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

    /// <summary>Клуб из базы и один его пловец (для правок в зеркале).</summary>
    private sealed record LiveClub(int Id, int SwimmerId)
    {
        public string OverviewKey => $"http:clubs:{Id}:overview";
        public string RosterKey => $"http:clubs:{Id}:roster";
    }

    /// <summary>
    /// Живая база (только чтение) и кэш с настоящими страницами всех групп и списком групп, а по
    /// запросу — обзорами и составами двух клубов; записи — через <see cref="Mirror"/>.
    /// <see cref="OpenAsync"/> даёт null, если базы нет.
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
        private readonly ClubPublicRepository _clubRepo;
        private readonly ClubOverviewRepository _overview;
        private Dictionary<int, int> _mediaSwimmers = [];

        private World(SwimmReadDbContext read, SwimmDbContext rw, CacheSettingsStub settings)
        {
            _read = read;
            _rw = rw;
            _groups = new HubGroupPublicRepository(read, rw, settings);
            _media = new HubGroupMediaService(rw);
            _publications = new UserMediaPublicationService(rw);
            Cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()), settings);
            var showcase = new ShowcaseSeasonProvider(read, Cache);
            _clubRepo = new ClubPublicRepository(read, showcase);
            _overview = new ClubOverviewRepository(read, _clubRepo, showcase);
        }

        public MemoryCacheService Cache { get; }
        public IReadOnlyList<LiveGroup> Groups { get; private set; } = [];

        /// <summary>
        /// Два клуба (пусто, если не просили или в базе нет двух): первый — клуб официальной
        /// группы, если такая есть, второй — любой другой клуб с пловцами.
        /// </summary>
        public IReadOnlyList<LiveClub> Clubs { get; private set; } = [];

        /// <summary>Медиа без одобренной публикации ни в одной группе — ни одна страница его не читала.</summary>
        public int SpareMediaId { get; private set; }

        public static async Task<World?> OpenAsync(bool rowPrecision = true, int hitVerifyPercent = 0, bool clubs = false)
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
            await world.LoadAsync(clubs);
            return world;
        }

        private async Task LoadAsync(bool withClubs)
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

            if (withClubs) Clubs = await LiveClubsAsync();
            await BuildAsync();
        }

        private async Task<List<LiveClub>> LiveClubsAsync()
        {
            // Клубы, которые отдаёт страница клуба: не склеенные и не псевдоклубы (иначе резолв
            // увёл бы в другой клуб или в 404), и с пловцами — иначе составу нечего сужать.
            var withSwimmers = _rw.Clubs.AsNoTracking()
                .Where(c => c.MergedIntoId == null && !c.IsPseudo && _rw.Swimmers.Any(s => s.ClubId == c.Id));
            var officialClub = await _rw.HubGroups.AsNoTracking()
                .Where(g => g.IsOfficial && g.ClubId != null && withSwimmers.Any(c => c.Id == g.ClubId))
                .OrderBy(g => g.Id).Select(g => g.ClubId).FirstOrDefaultAsync();
            var ids = await withSwimmers.OrderBy(c => c.Id == officialClub ? 0 : 1).ThenBy(c => c.Id)
                .Select(c => c.Id).Take(2).ToListAsync();
            if (ids.Count < 2) return [];

            var clubs = new List<LiveClub>();
            foreach (var id in ids)
                clubs.Add(new LiveClub(id, await _rw.Swimmers.Where(s => s.ClubId == id).OrderBy(s => s.Id).Select(s => s.Id).FirstAsync()));
            return clubs;
        }

        /// <summary>Собрать недостающие страницы групп, список и клубы — после сброса сценарию нужны все.</summary>
        public async Task BuildAsync()
        {
            foreach (var g in Groups) await PageAsync(g);
            await Cache.GetOrCreateAsync(ListKey, () => _groups.GetGroupsAsync(), Ttl);
            foreach (var c in Clubs)
            {
                // Как ClubsPublicController: обзор по умолчанию и первая страница состава.
                await Cache.GetOrCreateAsync(c.OverviewKey,
                    () => _overview.GetOverviewAsync(c.Id, c.Id, null, null, 3, null), Ttl);
                await Cache.GetOrCreateAsync(c.RosterKey,
                    () => _clubRepo.GetRosterAsync(c.Id, 1, 50, null, null, null, null), Ttl);
            }
        }

        public async Task<bool> OverviewCached(LiveClub c) =>
            await Cache.GetAsync<ClubOverviewDto>(c.OverviewKey) is not null;

        public async Task<bool> RosterCached(LiveClub c) =>
            await Cache.GetAsync<ClubRosterPageDto>(c.RosterKey) is not null;

        public IReadOnlyList<string> TagsOf(string key) => Cache.Snapshot().Single(e => e.Key == key).Tags;

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
        /// Зеркало строк базы в InMemory: группы и медиа их публикаций с теми же id, пловцы клубов
        /// (<see cref="LiveClub.SwimmerId"/>), плюс медиа без публикации (<see cref="SpareMediaId"/>).
        /// <paramref name="seed"/> досевает своё. Сев — без перехватчика: в сбросы он не попадает.
        /// Перехватчик возвращённого контекста сбрасывает метки в <see cref="Cache"/>.
        /// </summary>
        public SwimmDbContext Mirror(Action<SwimmDbContext>? seed = null, [CallerMemberName] string name = "")
        {
            using (var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options))
            {
                db.HubGroups.AddRange(Groups.Select(g => new HubGroup
                {
                    Id = g.Id, Name = g.Slug, Slug = g.Slug, ClubId = g.ClubId, OwnerUserId = 1,
                }));
                db.Swimmers.AddRange(Clubs.Select(c => new Swimmer { Id = c.SwimmerId, LastName = "S", ClubId = c.Id }));
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

    /// <summary>
    /// Клуб (К4б.5): обзор сужает только официальную группу — остальное в нём таблицей; состав
    /// сужен целиком и не носит ни одной метки таблицы.
    /// </summary>
    [Fact]
    public async Task RealClubPages_OverviewNarrowsTheOfficialGroup_RosterDependsOnlyOnItsClub()
    {
        await using var world = await World.OpenAsync(clubs: true);
        if (world == null || world.Clubs.Count == 0) return; // нет базы или двух клубов — пропуск

        foreach (var c in world.Clubs)
        {
            var overview = world.TagsOf(c.OverviewKey);
            Assert.Contains(CacheTags.Row("Clubs", c.Id), overview);
            Assert.Contains(CacheTags.AnyRow("HubGroups"), overview);
            Assert.DoesNotContain(CacheTags.Table("HubGroups"), overview);
            // Рейтинг клубов, season-best страны, стена рекордов — таблицей.
            Assert.Contains(CacheTags.Table("Results"), overview);

            var roster = world.TagsOf(c.RosterKey);
            Assert.Contains(CacheTags.Row("Clubs", c.Id), roster);
            Assert.Contains(CacheTags.AnyRow("Swimmers"), roster);
            Assert.Contains(CacheTags.AnyRow("Results"), roster);
            // Ни одной таблицы — и нет ложной table:Competitions от псевдонима колонки.
            Assert.DoesNotContain(roster, t => t.StartsWith("table:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task SwitchOff_RealPages_DependOnTheTables()
    {
        await using var world = await World.OpenAsync(rowPrecision: false, clubs: true);
        if (world == null || world.Groups.Count == 0) return;

        foreach (var g in world.Groups)
        {
            var tags = world.TagsOf(g);
            Assert.Contains(CacheTags.Table("HubGroups"), tags);
            Assert.Contains(CacheTags.Table("HubGroupMembers"), tags);
            Assert.DoesNotContain(tags, CacheTags.IsRowLevel);
        }
        foreach (var c in world.Clubs)
        {
            Assert.Contains(CacheTags.Table("HubGroups"), world.TagsOf(c.OverviewKey));
            Assert.Contains(CacheTags.Table("Swimmers"), world.TagsOf(c.RosterKey));
            Assert.DoesNotContain(world.TagsOf(c.RosterKey), CacheTags.IsRowLevel);
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
        await using var world = await World.OpenAsync(hitVerifyPercent: 100, clubs: true);
        if (world == null || world.Groups.Count == 0) return;

        await world.BuildAsync(); // всё уже в кэше: попадание → сверка

        // Сверяются суженные записи: страницы групп, обзоры и составы клубов (список — таблицей).
        var checks = world.Cache.Journal().HitChecks;
        Assert.Equal(world.Groups.Count + 2 * world.Clubs.Count, checks.Checked);
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

    // ── Клуб: обзор и состав (§3.2, К4б.5) ──────────────────────────────────────────────

    [Fact]
    public async Task EditOfGroupWithoutClub_KeepsEveryClubOverview()
    {
        await using var world = await World.OpenAsync(clubs: true);
        var loose = world?.Groups.FirstOrDefault(g => g.ClubId == null);
        if (world == null || world.Clubs.Count == 0 || loose == null) return;
        await using var db = world.Mirror();

        var group = await db.HubGroups.SingleAsync(g => g.Id == loose.Id);
        group.TrainingSchedule = "{\"place\":\"pool k4b5\"}";
        await db.SaveChangesAsync();

        // До К4б.5 обзор читал группы таблицей и падал от правки ЛЮБОЙ группы.
        foreach (var c in world.Clubs) Assert.True(await world.OverviewCached(c));
    }

    [Fact]
    public async Task EditOfClubsGroup_DropsOnlyThatClubsOverview()
    {
        await using var world = await World.OpenAsync(clubs: true);
        if (world == null) return;
        var clubGroup = world.Groups.FirstOrDefault(g => world.Clubs.Any(c => c.Id == g.ClubId));
        if (clubGroup == null) return; // нет группы с клубом из World — пропуск
        var own = world.Clubs.Single(c => c.Id == clubGroup.ClubId);
        var other = world.Clubs.Single(c => c != own);
        await using var db = world.Mirror();

        var group = await db.HubGroups.SingleAsync(g => g.Id == clubGroup.Id);
        group.TrainingSchedule = "{\"place\":\"pool k4b5\"}";
        await db.SaveChangesAsync();

        Assert.False(await world.OverviewCached(own));
        Assert.True(await world.OverviewCached(other));
    }

    /// <summary>Группу без клуба одобрили официальной для клуба — его обзор теперь её показывает.</summary>
    [Fact]
    public async Task GroupBecomesOfficialForAClub_DropsThatClubsOverview()
    {
        await using var world = await World.OpenAsync(clubs: true);
        var loose = world?.Groups.FirstOrDefault(g => g.ClubId == null);
        if (world == null || world.Clubs.Count == 0 || loose == null) return;
        var (target, other) = (world.Clubs[1], world.Clubs[0]);
        await using var db = world.Mirror();

        var group = await db.HubGroups.SingleAsync(g => g.Id == loose.Id);
        group.ClubId = target.Id;
        group.IsOfficial = true;
        await db.SaveChangesAsync();

        Assert.False(await world.OverviewCached(target));
        Assert.True(await world.OverviewCached(other));
    }

    [Fact]
    public async Task EditOfSwimmer_DropsOnlyTheRosterOfTheirClub()
    {
        await using var world = await World.OpenAsync(clubs: true);
        if (world == null || world.Clubs.Count == 0) return;
        var (own, other) = (world.Clubs[0], world.Clubs[1]);
        await using var db = world.Mirror();

        var swimmer = await db.Swimmers.SingleAsync(s => s.Id == own.SwimmerId);
        swimmer.FirstName = "renamed";
        await db.SaveChangesAsync();

        Assert.False(await world.RosterCached(own));
        Assert.True(await world.RosterCached(other));
    }

    [Fact]
    public async Task SwimmerMovesBetweenClubs_DropsBothRosters()
    {
        await using var world = await World.OpenAsync(clubs: true);
        if (world == null || world.Clubs.Count == 0) return;
        var (from, to) = (world.Clubs[0], world.Clubs[1]);
        await using var db = world.Mirror();

        var swimmer = await db.Swimmers.SingleAsync(s => s.Id == from.SwimmerId);
        swimmer.ClubId = to.Id; // старое и новое значение FK
        await db.SaveChangesAsync();

        Assert.False(await world.RosterCached(from));
        Assert.False(await world.RosterCached(to));
    }

    [Fact]
    public async Task NewResult_DropsOnlyTheRosterOfItsClub()
    {
        await using var world = await World.OpenAsync(clubs: true);
        if (world == null || world.Clubs.Count == 0) return;
        var (own, other) = (world.Clubs[0], world.Clubs[1]);
        await using var db = world.Mirror();

        // Счётчики заплывов состава — по Results.ClubId этого клуба.
        db.Results.Add(new ResultRecord { CompetitionId = 1, StyleId = 1, SwimmerId = own.SwimmerId, ClubId = own.Id });
        await db.SaveChangesAsync();

        Assert.False(await world.RosterCached(own));
        Assert.True(await world.RosterCached(other));
    }

    [Fact]
    public async Task BulkWriteOfResults_DropsEveryRoster()
    {
        await using var world = await World.OpenAsync(clubs: true);
        if (world == null || world.Clubs.Count == 0) return;
        await using var db = world.Mirror();

        // Как стирание объединённых мест в пересчёте (ExecuteUpdate по Results).
        await db.InvalidateTableCacheAsync<ResultRecord>();

        foreach (var c in world.Clubs) Assert.False(await world.RosterCached(c));
    }
}

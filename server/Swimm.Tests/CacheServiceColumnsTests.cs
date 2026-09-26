using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Служебные колонки (docs/plans/cache-row-precision-plan.md §2.6, К4б.6; тесты §4-1/§4-2 для
/// колонок): правка, задевшая ТОЛЬКО колонки реестра <see cref="CacheServiceColumns"/>, сбрасывает
/// <c>col:T.C</c> и — у корня — метку своей строки, без <c>table:T</c> и без меток корней по FK;
/// чтение, назвавшее служебную колонку, носит <c>col:T.C</c>, кроме корня в его же блоке.
///
/// Запись — на настоящей модели <see cref="SwimmDbContext"/> в InMemory (перехватчику сохранения
/// SQL не нужен), чтение — касанием таблицы напрямую, как перехватчик SQL, и на живом Postgres
/// (только чтение; нет базы — пропуск).
/// </summary>
public class CacheServiceColumnsTests
{
    private const string PgConn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private const int Club438 = 438, Group24 = 24, Swimmer5 = 5, Swimmer6 = 6, User1 = 1;
    private const int Competition9 = 9, Result7 = 7;

    private static readonly string[] LogligColumns =
    [
        "LogligId", "LogligIdStatus", "LogligIdSource", "LogligIdSuggestedByUserId", "LogligIdSuggestedAt", "LogligIdVerifiedAt",
    ];

    private sealed class RecordingCache : ICacheService
    {
        public readonly List<string[]> Calls = [];

        /// <summary>Метки единственного сброса — сохранение без транзакции даёт ровно один.</summary>
        public string[] Single => Assert.Single(Calls);

        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;

        public Task InvalidateTagsAsync(IReadOnlyCollection<string> tags, string reason)
        {
            Calls.Add([.. tags]);
            return Task.CompletedTask;
        }
    }

    private static DbContextOptions<SwimmDbContext> Options(string db, CacheInvalidationInterceptor? interceptor = null)
    {
        var b = new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(db);
        if (interceptor is not null) b.AddInterceptors(interceptor);
        return b.Options;
    }

    /// <summary>
    /// Клуб 438; пловцы 5 (клуб 438) и 6; группа 24 клуба 438; пользователь 1 (пловец 5);
    /// соревнование 9 и результат 7 пловца 5. Сеет контекст без перехватчика.
    /// </summary>
    private static void Seed(string db)
    {
        using var seed = new SwimmDbContext(Options(db));
        seed.Clubs.Add(new Club { Id = Club438, Name = "438" });
        seed.Swimmers.AddRange(
            new Swimmer { Id = Swimmer5, LastName = "A", ClubId = Club438 },
            new Swimmer { Id = Swimmer6, LastName = "B" });
        seed.HubGroups.Add(new HubGroup { Id = Group24, Name = "24", Slug = "g24", ClubId = Club438, OwnerUserId = User1 });
        seed.AppUsers.Add(new AppUser { Id = User1, Email = "u@example.test", SwimmerId = Swimmer5 });
        seed.Competitions.Add(new Competition { Id = Competition9, Name = "c" });
        seed.Results.Add(new ResultRecord
        {
            Id = Result7, CompetitionId = Competition9, SwimmerId = Swimmer5, ClubId = Club438, StyleId = 1,
        });
        seed.SaveChanges();
    }

    /// <summary>Контекст под тест: засеянная база, перехватчик с выключателем точности по колонкам.</summary>
    private static SwimmDbContext Db(string db, RecordingCache cache, bool columnPrecision = true)
    {
        Seed(db);
        return new SwimmDbContext(Options(db, new CacheInvalidationInterceptor(cache, new CacheSettingsStub(columnPrecision: columnPrecision))));
    }

    /// <summary>Метки без учёта порядка: порядок таблиц зависит от обхода трекера.</summary>
    private static void AssertTags(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));

    /// <summary>Правка loglig-привязки — как <c>LogligSuggestionService</c>: все шесть колонок.</summary>
    private static void SuggestLoglig(Swimmer swimmer, int logligId)
    {
        swimmer.LogligId = logligId;
        swimmer.LogligIdStatus = "Suggested";
        swimmer.LogligIdSource = "user-claim";
        swimmer.LogligIdSuggestedByUserId = User1;
        swimmer.LogligIdSuggestedAt = DateTime.UtcNow;
        swimmer.LogligIdVerifiedAt = null; // было null — не изменена, в метки не идёт
    }

    // ── Запись: служебная правка ──────────────────────────────────────────────────────

    [Fact]
    public async Task LogligEdit_DropsItsColumnsAndItsOwnRow_NotTheTableNorItsClub()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(LogligEdit_DropsItsColumnsAndItsOwnRow_NotTheTableNorItsClub), cache);

        SuggestLoglig(await db.Swimmers.SingleAsync(s => s.Id == Swimmer5), 77);
        await db.SaveChangesAsync();

        // Ни table:Swimmers (season-best, ровесники, страницы групп живы), ни row:Clubs:438
        // (состав и обзор клуба пловца живы): кто читает привязку, носит col:, а страница
        // самого пловца в своём блоке — row:Swimmers:5.
        AssertTags(
            [
                CacheTags.Column("Swimmers", "LogligId"), CacheTags.Column("Swimmers", "LogligIdStatus"),
                CacheTags.Column("Swimmers", "LogligIdSource"), CacheTags.Column("Swimmers", "LogligIdSuggestedByUserId"),
                CacheTags.Column("Swimmers", "LogligIdSuggestedAt"), CacheTags.Row("Swimmers", Swimmer5),
            ],
            cache.Single);
    }

    [Fact]
    public async Task ServiceEditsOfNonRoots_GiveOnlyTheirColumns()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(ServiceEditsOfNonRoots_GiveOnlyTheirColumns), cache);

        // Объединённые места и штамп проверки качества одним сохранением, как в пересчёте.
        var result = await db.Results.SingleAsync(r => r.Id == Result7);
        result.CombinedPlace = 3;
        result.IsBestResult = true;
        result.BestTimeMs = 30_000;
        (await db.Competitions.SingleAsync(c => c.Id == Competition9)).QualityScannedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // У результата FK на пловца, клуб и эстафету — но метки корней служебной правке не нужны.
        AssertTags(
            [
                CacheTags.Column("Results", "BestTimeMs"), CacheTags.Column("Results", "CombinedPlace"),
                CacheTags.Column("Results", "IsBestResult"), CacheTags.Column("Competitions", "QualityScannedAt"),
            ],
            cache.Single);
    }

    [Fact]
    public async Task GroupTouch_DropsItsColumnAndItsOwnRow_NotItsClub()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(GroupTouch_DropsItsColumnAndItsOwnRow_NotItsClub), cache);

        // TouchGroupAsync после правки состава: только «обновлено».
        (await db.HubGroups.SingleAsync(g => g.Id == Group24)).UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // До К4б.6 — table:HubGroups и row:Clubs:438: обзор и состав клуба 438 падали.
        AssertTags([CacheTags.Column("HubGroups", "UpdatedAt"), CacheTags.Row("HubGroups", Group24)], cache.Single);
    }

    [Fact]
    public async Task LoginWithTheSameNameAndAvatar_DropsOnlyTheColumnAndTheUsersRow()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(LoginWithTheSameNameAndAvatar_DropsOnlyTheColumnAndTheUsersRow), cache);

        // Как AuthController: имя и аватар присваиваются теми же значениями — трекер их не отметит.
        var user = await db.AppUsers.SingleAsync(u => u.Id == User1);
        user.DisplayName = new string(user.DisplayName.ToCharArray()); // другая строка, то же значение
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Пользователь — корень только своей строки (26.09.2026): служебная правка корня даёт и
        // метку его строки. Падают лишь страницы групп, где он владелец или админ, — таблицы нет.
        AssertTags([CacheTags.Column("Sys_AppUsers", "UpdatedAt"), CacheTags.Row("Sys_AppUsers", User1)], cache.Single);
    }

    // ── Запись: всё прочее — как обычно ──────────────────────────────────────────────────

    [Fact]
    public async Task MixedEdit_IsAnOrdinaryEdit()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(MixedEdit_IsAnOrdinaryEdit), cache);

        var swimmer = await db.Swimmers.SingleAsync(s => s.Id == Swimmer5);
        swimmer.LogligId = 77;
        swimmer.FirstName = "renamed"; // обычная колонка — правка обычная
        await db.SaveChangesAsync();

        AssertTags(
            [CacheTags.Table("Swimmers"), CacheTags.Row("Swimmers", Swimmer5), CacheTags.Row("Clubs", Club438)],
            cache.Single);
    }

    [Fact]
    public async Task SwitchOff_ServiceEditIsAnOrdinaryEdit()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(SwitchOff_ServiceEditIsAnOrdinaryEdit), cache, columnPrecision: false);

        SuggestLoglig(await db.Swimmers.SingleAsync(s => s.Id == Swimmer5), 77);
        await db.SaveChangesAsync();

        AssertTags(
            [CacheTags.Table("Swimmers"), CacheTags.Row("Swimmers", Swimmer5), CacheTags.Row("Clubs", Club438)],
            cache.Single);
    }

    [Fact]
    public async Task WithoutSettings_ServiceEditIsAnOrdinaryEdit()
    {
        var cache = new RecordingCache();
        Seed(nameof(WithoutSettings_ServiceEditIsAnOrdinaryEdit));
        await using var db = new SwimmDbContext(Options(nameof(WithoutSettings_ServiceEditIsAnOrdinaryEdit),
            new CacheInvalidationInterceptor(cache)));

        (await db.Competitions.SingleAsync(c => c.Id == Competition9)).QualityScannedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Перехватчик без настроек (тесты до К4б.6) точности по колонкам не делает.
        AssertTags([CacheTags.Table("Competitions")], cache.Single);
    }

    [Fact]
    public async Task AddedAndDeletedRows_AreOrdinary_EvenWithServiceColumns()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(AddedAndDeletedRows_AreOrdinary_EvenWithServiceColumns), cache);

        db.Swimmers.Add(new Swimmer { Id = 8, LastName = "C", LogligId = 88 });
        await db.SaveChangesAsync();
        Assert.Contains(CacheTags.Table("Swimmers"), cache.Single);

        cache.Calls.Clear();
        db.Results.Remove(await db.Results.SingleAsync(r => r.Id == Result7));
        await db.SaveChangesAsync();
        Assert.Contains(CacheTags.Table("Results"), cache.Single);
        Assert.DoesNotContain(cache.Single, CacheTags.IsColumn);
    }

    [Fact]
    public async Task UpdateCall_MarksEveryColumn_SoTheEditIsOrdinary()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(UpdateCall_MarksEveryColumn_SoTheEditIsOrdinary), cache);

        var swimmer = await db.Swimmers.SingleAsync(s => s.Id == Swimmer5);
        swimmer.LogligId = 77;
        db.Swimmers.Update(swimmer); // Update() помечает изменёнными все колонки

        await db.SaveChangesAsync();

        Assert.Contains(CacheTags.Table("Swimmers"), cache.Single);
        Assert.DoesNotContain(cache.Single, CacheTags.IsColumn);
    }

    [Fact]
    public async Task ServiceAndOrdinaryRowsOfOneTable_KeepTheColumnTags()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(ServiceAndOrdinaryRowsOfOneTable_KeepTheColumnTags), cache);

        (await db.Swimmers.SingleAsync(s => s.Id == Swimmer5)).LogligId = 77;   // служебная
        (await db.Swimmers.SingleAsync(s => s.Id == Swimmer6)).FirstName = "b"; // обычная
        await db.SaveChangesAsync();

        // table:Swimmers суженных читателей не задевает (состав клуба 438 читает пловцов в блоке
        // клуба), а row:Clubs:438 служебная правка не дала — col: нужен и рядом с table:.
        AssertTags(
            [
                CacheTags.Table("Swimmers"), CacheTags.Row("Swimmers", Swimmer6),
                CacheTags.Row("Swimmers", Swimmer5), CacheTags.Column("Swimmers", "LogligId"),
            ],
            cache.Single);
    }

    [Fact]
    public async Task ServiceEditOfARowNotFromQuery_KeepsItsOwnRow()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(ServiceEditOfARowNotFromQuery_KeepsItsOwnRow), cache);

        // Заглушка: исходные значения FK не из базы — но служебной правке они не нужны, а ключ настоящий.
        var stub = new Swimmer { Id = Swimmer5 };
        db.Swimmers.Attach(stub);
        stub.LogligId = 77;
        await db.SaveChangesAsync();

        AssertTags([CacheTags.Column("Swimmers", "LogligId"), CacheTags.Row("Swimmers", Swimmer5)], cache.Single);
    }

    [Fact]
    public async Task OverMaxServiceRowsOfARoot_CompressToAnyRow_StillWithoutTheTable()
    {
        var max = CacheInvalidationInterceptor.MaxRowsPerTable;
        var name = nameof(OverMaxServiceRowsOfARoot_CompressToAnyRow_StillWithoutTheTable);
        Seed(name);
        using (var seed = new SwimmDbContext(Options(name)))
        {
            seed.Swimmers.AddRange(Enumerable.Range(1000, max + 1).Select(i => new Swimmer { Id = i, ClubId = Club438 }));
            seed.SaveChanges();
        }
        var cache = new RecordingCache();
        await using var db = new SwimmDbContext(Options(name,
            new CacheInvalidationInterceptor(cache, new CacheSettingsStub(columnPrecision: true))));

        // Суточное loglig-задание подтвердило сразу много привязок.
        foreach (var s in await db.Swimmers.Where(s => s.Id >= 1000).ToListAsync()) s.LogligIdStatus = "Verified";
        await db.SaveChangesAsync();

        AssertTags([CacheTags.AnyRow("Swimmers"), CacheTags.Column("Swimmers", "LogligIdStatus")], cache.Single);
    }

    [Fact]
    public async Task ManyServiceRowsOfANonRoot_StayColumnsOnly()
    {
        var max = CacheInvalidationInterceptor.MaxRowsPerTable;
        var name = nameof(ManyServiceRowsOfANonRoot_StayColumnsOnly);
        Seed(name);
        using (var seed = new SwimmDbContext(Options(name)))
        {
            seed.Results.AddRange(Enumerable.Range(1000, max + 1).Select(i => new ResultRecord
            {
                Id = i, CompetitionId = Competition9, SwimmerId = Swimmer5, ClubId = Club438, StyleId = 1,
            }));
            seed.SaveChanges();
        }
        var cache = new RecordingCache();
        await using var db = new SwimmDbContext(Options(name,
            new CacheInvalidationInterceptor(cache, new CacheSettingsStub(columnPrecision: true))));

        // Пересчёт объединённых мест события: тысячи строк — и ни anyrow:Results (состав клуба
        // жив), ни table:Results (season-best жив).
        foreach (var r in await db.Results.ToListAsync()) r.CombinedPlace = 1;
        await db.SaveChangesAsync();

        AssertTags([CacheTags.Column("Results", "CombinedPlace")], cache.Single);
    }

    [Fact]
    public void TransactionMerge_KeepsTheColumns_AndTheTableOnceAnyEditIsOrdinary()
    {
        var pending = new CacheInvalidationInterceptor.ChangeTags();
        var service = new CacheInvalidationInterceptor.ChangeTags();
        service.AddColumns("Swimmers", ["LogligId"]);
        service.AddRow("Swimmers", [CacheTags.Row("Swimmers", 5)], whole: false);
        service.MergeInto(pending);
        AssertTags([CacheTags.Row("Swimmers", 5), CacheTags.Column("Swimmers", "LogligId")], pending.ToTags());

        var ordinary = new CacheInvalidationInterceptor.ChangeTags();
        ordinary.AddRow("Swimmers", [CacheTags.Row("Swimmers", 6)]);
        ordinary.MergeInto(pending);

        AssertTags(
            [
                CacheTags.Table("Swimmers"), CacheTags.Row("Swimmers", 5), CacheTags.Row("Swimmers", 6),
                CacheTags.Column("Swimmers", "LogligId"),
            ],
            pending.ToTags());
    }

    // ── Массовая запись служебных колонок мимо трекера ─────────────────────────────────

    [Fact]
    public async Task BulkServiceWrite_DropsTheColumns_AndAnyRowOnlyOfARoot()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(BulkServiceWrite_DropsTheColumns_AndAnyRowOnlyOfARoot), cache);

        // Стирание объединённых мест в пересчёте (ExecuteUpdate по Results).
        await db.InvalidateColumnsCacheAsync<ResultRecord>(
            nameof(ResultRecord.CombinedPlace), nameof(ResultRecord.IsBestResult), nameof(ResultRecord.BestTimeMs));
        AssertTags(
            [
                CacheTags.Column("Results", "CombinedPlace"), CacheTags.Column("Results", "IsBestResult"),
                CacheTags.Column("Results", "BestTimeMs"),
            ],
            cache.Single);

        // У корня строки неизвестны, а в своём блоке он col: не носит — anyrow.
        cache.Calls.Clear();
        await db.InvalidateColumnsCacheAsync<Swimmer>(nameof(Swimmer.LogligIdStatus));
        AssertTags([CacheTags.AnyRow("Swimmers"), CacheTags.Column("Swimmers", "LogligIdStatus")], cache.Single);
    }

    [Fact]
    public async Task BulkServiceWrite_SwitchOff_IsAnOrdinaryBulkWrite()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(BulkServiceWrite_SwitchOff_IsAnOrdinaryBulkWrite), cache, columnPrecision: false);

        await db.InvalidateColumnsCacheAsync<ResultRecord>(nameof(ResultRecord.CombinedPlace));

        AssertTags([CacheTags.Table("Results"), CacheTags.AnyRow("Results")], cache.Single);
    }

    [Fact]
    public async Task BulkWriteOfAnOrdinaryColumn_Throws_WithOrWithoutTheInterceptor()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(BulkWriteOfAnOrdinaryColumn_Throws_WithOrWithoutTheInterceptor), cache, columnPrecision: false);
        await using var bare = new SwimmDbContext(Options(nameof(BulkWriteOfAnOrdinaryColumn_Throws_WithOrWithoutTheInterceptor)));

        // col:Results.TimeMillisecond не носит никто — такой сброс прошёл бы мимо всех.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.InvalidateColumnsCacheAsync<ResultRecord>(nameof(ResultRecord.TimeMillisecond)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bare.InvalidateColumnsCacheAsync<ResultRecord>(nameof(ResultRecord.TimeMillisecond)));
        Assert.Empty(cache.Calls);
    }

    // ── Реестр ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_IsExactlyThePlannedList_OnBothProviders()
    {
        using var inMemory = new SwimmDbContext(Options(nameof(Registry_IsExactlyThePlannedList_OnBothProviders)));
        using var npgsql = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql("Host=localhost;Database=none").Options);

        foreach (var model in new[] { inMemory.Model, npgsql.Model })
        {
            var byTable = CacheServiceColumns.Of(model).ByTable;
            // Новая колонка — решение по журналу сбросов (§2.6), а не строчка по месту.
            Assert.Equal(["Competitions", "HubGroups", "Results", "Swimmers", "Sys_AppUsers"], byTable.Keys.Order());
            Assert.Equal(LogligColumns, byTable["Swimmers"]);
            Assert.Equal(["QualityScannedAt"], byTable["Competitions"]);
            Assert.Equal(["CombinedPlace", "IsBestResult", "BestTimeMs"], byTable["Results"]);
            Assert.Equal(["UpdatedAt"], byTable["HubGroups"]);
            Assert.Equal(["UpdatedAt"], byTable["Sys_AppUsers"]);
        }
    }

    [Fact]
    public void Registry_RefusesKeys_RootForeignKeys_SelfReferencingRoots_MissingProperties()
    {
        using var db = new SwimmDbContext(Options(nameof(Registry_RefusesKeys_RootForeignKeys_SelfReferencingRoots_MissingProperties)));

        foreach (var bad in new (Type, string)[]
                 {
                     (typeof(Swimmer), nameof(Swimmer.Id)),         // ключ
                     (typeof(Swimmer), nameof(Swimmer.ClubId)),     // FK на корень Clubs
                     (typeof(Club), nameof(Club.Name)),             // у клуба MergedIntoId — FK на себя
                     (typeof(Swimmer), "NoSuchProperty"),
                 })
            Assert.Throws<InvalidOperationException>(() => CacheServiceColumns.Resolve(db.Model, [bad]));
    }

    private sealed class LowerColumnProbe
    {
        public int Id { get; set; }
        public DateTime? Stamp { get; set; }
    }

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b) =>
            b.Entity<LowerColumnProbe>().Property(p => p.Stamp).HasColumnName("stamp");
    }

    [Fact]
    public void Registry_RefusesAColumnNpgsqlWouldNotQuote_SkipsEntitiesOfAnotherModel()
    {
        using var probe = new ProbeContext(new DbContextOptionsBuilder<ProbeContext>().UseNpgsql("Host=localhost;Database=none").Options);

        // «stamp» без кавычек: чтение ищет колонки в кавычках и такую не увидело бы.
        Assert.Throws<InvalidOperationException>(() =>
            CacheServiceColumns.Resolve(probe.Model, [(typeof(LowerColumnProbe), nameof(LowerColumnProbe.Stamp))]));
        // Сущностей реестра в чужой модели нет — пусто, а не исключение.
        Assert.Empty(CacheServiceColumns.Of(probe.Model).ByTable);
    }

    // ── Чтение: кто носит col: ──────────────────────────────────────────────────────────

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private sealed record Payload(string Value);

    private static MemoryCacheService Cache(bool rowPrecision = true) =>
        new(new MemoryCache(new MemoryCacheOptions()), new CacheSettingsStub(rowPrecision));

    private static IReadOnlyList<string> TagsOf(MemoryCacheService cache, string key) =>
        cache.Snapshot().Single(e => e.Key == key).Tags;

    private static Task Build(MemoryCacheService cache, string key, Action<CacheBuildScope> read) =>
        cache.GetOrCreateAsync(key, () =>
        {
            read(CacheBuildScope.Current!);
            return Task.FromResult(new Payload(key));
        }, Ttl);

    [Fact]
    public async Task TableRead_NamingAServiceColumn_CarriesItsTag()
    {
        var cache = Cache();
        await Build(cache, "swimmer-card", s => s.TouchTable("Swimmers", ["LogligId"]));
        await Build(cache, "season-best", s => s.TouchTable("Swimmers", []));

        AssertTags([CacheTags.Table("Swimmers"), CacheTags.Column("Swimmers", "LogligId")], TagsOf(cache, "swimmer-card"));
        AssertTags([CacheTags.Table("Swimmers")], TagsOf(cache, "season-best"));
    }

    [Fact]
    public async Task NarrowedChild_CarriesTheColumnTag_TheRootInItsOwnBlockDoesNot()
    {
        var cache = Cache();
        // Состав клуба 438: пловцы — потомки клуба; row:Clubs:438 служебная правка пловца не даёт.
        await Build(cache, "roster", s =>
        {
            using (s.Narrow("Clubs", [Club438], new HashSet<string> { "Clubs", "Swimmers" }))
                s.TouchTable("Swimmers", ["LogligId"]);
        });
        // Страница группы 24 читает свою строку целиком: её отметку «обновлено» закрывает row:.
        await Build(cache, "group-24", s =>
        {
            using (s.Narrow("HubGroups", [Group24], new HashSet<string> { "HubGroups" }))
                s.TouchTable("HubGroups", ["UpdatedAt"]);
        });

        AssertTags(
            [CacheTags.AnyRow("Swimmers"), CacheTags.Row("Clubs", Club438), CacheTags.Column("Swimmers", "LogligId")],
            TagsOf(cache, "roster"));
        AssertTags([CacheTags.AnyRow("HubGroups"), CacheTags.Row("HubGroups", Group24)], TagsOf(cache, "group-24"));

        // Стык с записью: так сбрасывает loglig-правка пловца 5 и отметка «обновлено» группы 17.
        await cache.InvalidateTagsAsync(CacheTags.Column("Swimmers", "LogligId"), CacheTags.Row("Swimmers", Swimmer5));
        await cache.InvalidateTagsAsync(CacheTags.Column("HubGroups", "UpdatedAt"), CacheTags.Row("HubGroups", 17));
        Assert.DoesNotContain(cache.Snapshot(), e => e.Key == "roster");  // читал привязку — упал
        Assert.Contains(cache.Snapshot(), e => e.Key == "group-24");      // чужая отметка — жив
    }

    [Fact]
    public async Task RowPrecisionOff_TheRootIsReadAsATable_AndCarriesTheColumnTag()
    {
        var cache = Cache(rowPrecision: false);
        await Build(cache, "group-24", s =>
        {
            using (s.Narrow("HubGroups", [Group24], new HashSet<string> { "HubGroups" }))
                s.TouchTable("HubGroups", ["UpdatedAt"]);
        });

        // Блок пустой — строки группы 24 никто не обещал; служебную правку ловит col:.
        AssertTags([CacheTags.Table("HubGroups"), CacheTags.Column("HubGroups", "UpdatedAt")], TagsOf(cache, "group-24"));
    }

    // ── Чтение: настоящий SQL (живой Postgres, только чтение) ──────────────────────────────

    private static async Task<SwimmReadDbContext?> TryReadAsync()
    {
        var db = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseNpgsql(PgConn)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(new CacheDependencyInterceptor())
            .Options);
        try { if (await db.Database.CanConnectAsync()) return db; } catch { /* нет базы */ }
        await db.DisposeAsync();
        return null;
    }

    private static IEnumerable<string> ColumnTags(MemoryCacheService cache, string key) =>
        TagsOf(cache, key).Where(CacheTags.IsColumn);

    [Fact]
    public async Task RealSql_ColumnTagOnlyForTheServiceColumnsTheQueryNames()
    {
        await using var db = await TryReadAsync();
        if (db == null) return; // нет базы — пропуск
        var cache = Cache();

        await cache.GetOrCreateAsync("names", () => db.Swimmers
            .OrderBy(s => s.Id).Select(s => new { s.Id, s.LastName }).Take(3).ToListAsync(), Ttl);
        await cache.GetOrCreateAsync("loglig-id", () => db.Swimmers
            .OrderBy(s => s.Id).Select(s => new { s.Id, s.LogligId }).Take(3).ToListAsync(), Ttl);
        await cache.GetOrCreateAsync("whole", async () =>
            new Payload((await db.Swimmers.OrderBy(s => s.Id).Take(3).ToListAsync()).Count.ToString()), Ttl);
        await cache.GetOrCreateAsync("filter", () => db.Results
            .Where(r => r.IsBestResult == true).OrderBy(r => r.Id).Select(r => r.Id).Take(3).ToListAsync(), Ttl);

        Assert.Empty(ColumnTags(cache, "names"));
        Assert.Contains(CacheTags.Table("Swimmers"), TagsOf(cache, "names"));
        AssertTags([CacheTags.Column("Swimmers", "LogligId")], ColumnTags(cache, "loglig-id"));
        // Сущность целиком называет все свои колонки — и все служебные в том числе (§2.6: выгода только у проекций).
        AssertTags(LogligColumns.Select(c => CacheTags.Column("Swimmers", c)), ColumnTags(cache, "whole"));
        // WHERE — тоже чтение колонки.
        AssertTags([CacheTags.Column("Results", "IsBestResult")], ColumnTags(cache, "filter"));
    }

    [Fact]
    public async Task RealSql_SelectStar_ReadsEveryServiceColumn_CountStar_ReadsNone()
    {
        await using var db = await TryReadAsync();
        if (db == null) return;
        var cache = Cache();

        await cache.GetOrCreateAsync("star", () => db.Swimmers
            .FromSqlRaw("SELECT * FROM \"Swimmers\"").Select(s => s.Id).Take(1).ToListAsync(), Ttl);
        await cache.GetOrCreateAsync("count", async () => new Payload((await db.Swimmers.CountAsync()).ToString()), Ttl);

        AssertTags(LogligColumns.Select(c => CacheTags.Column("Swimmers", c)), ColumnTags(cache, "star"));
        Assert.Empty(ColumnTags(cache, "count"));
    }
}

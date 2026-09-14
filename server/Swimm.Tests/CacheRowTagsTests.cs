using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Метки строк при записи (docs/plans/cache-row-precision-plan.md §2.2, К4б.2; тесты §4-1):
/// сохранение сбрасывает, кроме <c>table:T</c>, метку строки корня (<c>row:HubGroups:24</c>) — у
/// своей строки корня и у строки с FK на корень, по старому и новому значению FK; где строки
/// неизвестны — <c>anyrow:T</c>.
///
/// Настоящая модель <see cref="SwimmDbContext"/> в InMemory: перехватчику сохранения SQL не нужен,
/// а FK, каскады и корни берутся из модели. Сброшенные метки пишет <see cref="RecordingCache"/>.
/// Временные ключи выдаёт только реляционный провайдер — их проверка идёт на Npgsql без
/// соединения (<see cref="CacheInvalidationInterceptor.PreviewTags"/>).
/// </summary>
public class CacheRowTagsTests
{
    private const int Group24 = 24, Group17 = 17, Club438 = 438, Club500 = 500;
    private const int Swimmer5 = 5, Swimmer6 = 6, Member100 = 100;

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
    /// Две группы (24 с клубом 438, 17 без клуба), пловцы 5 и 6, участник 100 = пловец 5 в группе
    /// 24. Сеет контекст без перехватчика — сев в сбросы не попадает.
    /// </summary>
    private static void Seed(string db)
    {
        using var seed = new SwimmDbContext(Options(db));
        seed.Clubs.AddRange(new Club { Id = Club438, Name = "438" }, new Club { Id = Club500, Name = "500" });
        seed.Swimmers.AddRange(
            new Swimmer { Id = Swimmer5, LastName = "A", ClubId = Club438 },
            new Swimmer { Id = Swimmer6, LastName = "B" });
        seed.HubGroups.AddRange(
            new HubGroup { Id = Group24, Name = "24", Slug = "g24", ClubId = Club438, OwnerUserId = 1 },
            new HubGroup { Id = Group17, Name = "17", Slug = "g17", OwnerUserId = 1 });
        seed.HubGroupMembers.Add(new HubGroupMember { Id = Member100, HubGroupId = Group24, SwimmerId = Swimmer5 });
        seed.SaveChanges();
    }

    /// <summary>Контекст под тест: засеянная база, перехватчик пишет сбросы в <paramref name="cache"/>.</summary>
    private static SwimmDbContext Db(string db, RecordingCache cache)
    {
        Seed(db);
        return new SwimmDbContext(Options(db, new CacheInvalidationInterceptor(cache)));
    }

    /// <summary>Метки без учёта порядка: порядок таблиц зависит от обхода трекера.</summary>
    private static void AssertTags(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));

    // ── Своя строка корня ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RootRow_Edit_DropsItsOwnRow_AndItsClub_NotTheOtherGroup()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(RootRow_Edit_DropsItsOwnRow_AndItsClub_NotTheOtherGroup), cache);

        var group = await db.HubGroups.SingleAsync(g => g.Id == Group24);
        group.TrainingSchedule = "{}";
        await db.SaveChangesAsync();

        // Метка своей строки и клуба по FK (обзор клуба 438 читает свою официальную группу) —
        // приёмка §10: у правки группы 24 в журнале row:HubGroups:24 и row:Clubs:438.
        AssertTags(
            [CacheTags.Table("HubGroups"), CacheTags.Row("HubGroups", Group24), CacheTags.Row("Clubs", Club438)],
            cache.Single);
    }

    [Fact]
    public async Task RootWithoutClub_Edit_GivesNoClubRow()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(RootWithoutClub_Edit_GivesNoClubRow), cache);

        var group = await db.HubGroups.SingleAsync(g => g.Id == Group17);
        group.Name = "renamed";
        await db.SaveChangesAsync();

        // FK null — связи нет, и метки нет.
        AssertTags(
            [CacheTags.Table("HubGroups"), CacheTags.Row("HubGroups", Group17)],
            cache.Single);
    }

    [Fact]
    public async Task RootForeignKey_SetFromNull_DropsOnlyTheNewParent()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(RootForeignKey_SetFromNull_DropsOnlyTheNewParent), cache);

        var group = await db.HubGroups.SingleAsync(g => g.Id == Group17);
        group.ClubId = Club500; // группу одобрили официальной для клуба 500
        await db.SaveChangesAsync();

        AssertTags(
            [CacheTags.Table("HubGroups"), CacheTags.Row("HubGroups", Group17), CacheTags.Row("Clubs", Club500)],
            cache.Single);
    }

    // ── Прямой потомок: FK на корень, старое и новое значение ───────────────────────

    [Fact]
    public async Task ChildRow_Added_DropsItsParents()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(ChildRow_Added_DropsItsParents), cache);

        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = Group17, SwimmerId = Swimmer6 });
        await db.SaveChangesAsync();

        // Своей метки у участника нет — HubGroupMembers не корень; корни — группа и пловец.
        AssertTags(
            [CacheTags.Table("HubGroupMembers"), CacheTags.Row("HubGroups", Group17), CacheTags.Row("Swimmers", Swimmer6)],
            cache.Single);
    }

    [Fact]
    public async Task ChildRow_MovedToAnotherParent_DropsOldAndNewParent()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(ChildRow_MovedToAnotherParent_DropsOldAndNewParent), cache);

        var member = await db.HubGroupMembers.SingleAsync(m => m.Id == Member100);
        member.HubGroupId = Group17; // участник ушёл из группы 24 в 17 — меняются обе страницы
        await db.SaveChangesAsync();

        var tags = cache.Single;
        Assert.Contains(CacheTags.Row("HubGroups", Group24), tags);
        Assert.Contains(CacheTags.Row("HubGroups", Group17), tags);
        Assert.Contains(CacheTags.Row("Swimmers", Swimmer5), tags);
        Assert.DoesNotContain(CacheTags.AnyRow("HubGroupMembers"), tags);
    }

    [Fact]
    public async Task ChildRow_Deleted_DropsParentsByTheValuesInTheDatabase()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(ChildRow_Deleted_DropsParentsByTheValuesInTheDatabase), cache);

        db.HubGroupMembers.Remove(await db.HubGroupMembers.SingleAsync(m => m.Id == Member100));
        await db.SaveChangesAsync();

        AssertTags(
            [CacheTags.Table("HubGroupMembers"), CacheTags.Row("HubGroups", Group24), CacheTags.Row("Swimmers", Swimmer5)],
            cache.Single);
    }

    [Fact]
    public async Task SwimmerMovedToAnotherClub_DropsItsRow_AndBothClubs()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(SwimmerMovedToAnotherClub_DropsItsRow_AndBothClubs), cache);

        var swimmer = await db.Swimmers.SingleAsync(s => s.Id == Swimmer5);
        swimmer.ClubId = Club500; // корень с FK на другой корень: и своя строка, и оба клуба
        await db.SaveChangesAsync();

        AssertTags(
            [
                CacheTags.Table("Swimmers"), CacheTags.Row("Swimmers", Swimmer5),
                CacheTags.Row("Clubs", Club500), CacheTags.Row("Clubs", Club438),
            ],
            cache.Single);
    }

    // ── Только корни из реестра ───────────────────────────────────────────────────

    [Fact]
    public async Task NotARoot_AndNoForeignKeyToRoots_GivesOnlyTheTable()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(NotARoot_AndNoForeignKeyToRoots_GivesOnlyTheTable), cache);

        db.Competitions.Add(new Competition { Id = 9, Name = "c" });
        await db.SaveChangesAsync();

        // Competitions не корень: свой id у неё никто не сужает — метка строки лишь раздула бы сброс.
        AssertTags([CacheTags.Table("Competitions")], cache.Single);
    }

    [Fact]
    public void RootRegistry_IsExactlyThePlannedFive()
    {
        using var db = new SwimmDbContext(Options(nameof(RootRegistry_IsExactlyThePlannedFive)));
        var roots = db.Model.GetEntityTypes().Where(CacheRowRoots.IsRoot).Select(t => t.GetTableName()).Order();

        // Новый корень — решение плана (§2.1), а не строчка по месту: вместе с ним приходят
        // сужение (К4б.3) и сценарные тесты.
        Assert.Equal(["Clubs", "HubGroups", "Relays", "Swimmers", "Sys_UserMedia"], roots);
    }

    // ── Каскад и сжатие ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RootDeleted_DropsItsRow_AndAnyRowOfEveryCascadeTable()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(RootDeleted_DropsItsRow_AndAnyRowOfEveryCascadeTable), cache);

        db.HubGroups.Remove(await db.HubGroups.SingleAsync(g => g.Id == Group24));
        await db.SaveChangesAsync();

        var tags = cache.Single;
        Assert.Contains(CacheTags.Row("HubGroups", Group24), tags);
        Assert.Contains(CacheTags.Row("Clubs", Club438), tags);
        // Строки каскада трекер не загружал: какие и чьи они — неизвестно (§2.2 п. 5).
        Assert.Contains(CacheTags.Table("HubGroupMembers"), tags);
        Assert.Contains(CacheTags.AnyRow("HubGroupMembers"), tags);
        Assert.Contains(CacheTags.AnyRow("Sys_TrainingResults"), tags); // цепочка: группа → тренировка → результат
        Assert.DoesNotContain(CacheTags.AnyRow("HubGroups"), tags);     // своя строка известна
    }

    [Fact]
    public async Task MediaDeleted_DropsAnyRowOfPublications_ThatAreNarrowedByAnotherRoot()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(MediaDeleted_DropsAnyRowOfPublications_ThatAreNarrowedByAnotherRoot), cache);
        db.UserMedia.Add(new UserMedia { Id = 7, UserId = 1, SwimmerId = Swimmer5, Url = "u" });
        await db.SaveChangesAsync();
        cache.Calls.Clear();

        db.UserMedia.Remove(await db.UserMedia.SingleAsync(m => m.Id == 7));
        await db.SaveChangesAsync();

        // Удалили медиа — каскадом ушли его публикации, а страница группы сужена по группе,
        // не по медиа: row:Sys_UserMedia:7 её не заденет, anyrow публикаций — заденет.
        var tags = cache.Single;
        Assert.Contains(CacheTags.Row("Sys_UserMedia", 7), tags);
        Assert.Contains(CacheTags.AnyRow("Sys_UserMediaPublications"), tags);
    }

    [Fact]
    public async Task ExactlyMaxRows_KeepRowTags_OneMore_CompressesToAnyRow()
    {
        var max = CacheInvalidationInterceptor.MaxRowsPerTable;

        var cache = new RecordingCache();
        await using (var db = Db(nameof(ExactlyMaxRows_KeepRowTags_OneMore_CompressesToAnyRow) + "-max", cache))
        {
            db.Swimmers.AddRange(Enumerable.Range(1000, max).Select(i => new Swimmer { Id = i, ClubId = Club500 }));
            await db.SaveChangesAsync();
        }
        Assert.Contains(CacheTags.Row("Swimmers", 1000), cache.Single);
        Assert.DoesNotContain(CacheTags.AnyRow("Swimmers"), cache.Single);

        cache.Calls.Clear();
        await using (var db = Db(nameof(ExactlyMaxRows_KeepRowTags_OneMore_CompressesToAnyRow) + "-over", cache))
        {
            db.Swimmers.AddRange(Enumerable.Range(1000, max + 1).Select(i => new Swimmer { Id = i, ClubId = Club500 }));
            await db.SaveChangesAsync();
        }
        // Слиянию клубов и пересчёту мест метки на тысячи строк ни к чему (§2.2 п. 4) — и ни
        // одной метки строки, даже клуба по FK: anyrow:Swimmers закрывает всех, кто сузил пловцов.
        AssertTags([CacheTags.Table("Swimmers"), CacheTags.AnyRow("Swimmers")], cache.Single);
    }

    // ── Строка не из запроса: исходным значениям FK верить нельзя (§2.2 п. 7) ──────────

    [Fact]
    public async Task StubRemove_GivesAnyRow_NotTheParentOfTheEmptyForeignKey()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(StubRemove_GivesAnyRow_NotTheParentOfTheEmptyForeignKey), cache);

        // Заглушка: в базе участник 100 — в группе 24, а у заглушки HubGroupId = 0. Метка
        // row:HubGroups:0 прошла бы мимо страницы группы 24 — недосброс.
        db.HubGroupMembers.Remove(new HubGroupMember { Id = Member100 });
        await db.SaveChangesAsync();

        AssertTags(
            [CacheTags.Table("HubGroupMembers"), CacheTags.AnyRow("HubGroupMembers")],
            cache.Single);
    }

    [Fact]
    public async Task UpdateOfADetachedObject_GivesAnyRow()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(UpdateOfADetachedObject_GivesAnyRow), cache);

        // «Пришло с формы»: старая группа (24) трекеру неизвестна — исходное значение = новое.
        db.HubGroupMembers.Update(new HubGroupMember { Id = Member100, HubGroupId = Group17, SwimmerId = Swimmer5 });
        await db.SaveChangesAsync();

        Assert.Contains(CacheTags.AnyRow("HubGroupMembers"), cache.Single);
    }

    [Fact]
    public async Task ReattachedAfterClear_GivesAnyRow()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(ReattachedAfterClear_GivesAnyRow), cache);
        var member = await db.HubGroupMembers.SingleAsync(m => m.Id == Member100);
        db.ChangeTracker.Clear(); // событий Clear не шлёт — строка «из запроса» тут не отмечается

        member.HubGroupId = Group17;
        db.HubGroupMembers.Attach(member);
        db.Entry(member).Property(m => m.HubGroupId).IsModified = true;
        await db.SaveChangesAsync();

        Assert.Contains(CacheTags.AnyRow("HubGroupMembers"), cache.Single);
    }

    [Fact]
    public async Task UpdateCallOnARowFromQuery_StaysPrecise()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(UpdateCallOnARowFromQuery_StaysPrecise), cache);
        var member = await db.HubGroupMembers.SingleAsync(m => m.Id == Member100);

        member.HubGroupId = Group17;
        db.HubGroupMembers.Update(member); // строка уже отслеживается — исходные значения из базы
        await db.SaveChangesAsync();

        var tags = cache.Single;
        Assert.Contains(CacheTags.Row("HubGroups", Group24), tags);
        Assert.Contains(CacheTags.Row("HubGroups", Group17), tags);
        Assert.DoesNotContain(CacheTags.AnyRow("HubGroupMembers"), tags);
    }

    [Fact]
    public async Task StubOfARootWithoutParentRoots_KeepsItsOwnRow()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(StubOfARootWithoutParentRoots_KeepsItsOwnRow), cache);
        db.Relays.Add(new Relay { Id = 3 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        cache.Calls.Clear();

        // У эстафеты FK на корни нет — сомневаться не в чем: ключ заглушки и есть ключ строки.
        db.Relays.Remove(new Relay { Id = 3 });
        await db.SaveChangesAsync();

        Assert.Contains(CacheTags.Row("Relays", 3), cache.Single);
        Assert.DoesNotContain(CacheTags.AnyRow("Relays"), cache.Single);
    }

    // ── Временные ключи (Npgsql без соединения) ──────────────────────────────────────

    [Fact]
    public void TemporaryKeys_OfANewGroupAndItsMember_AreSkipped()
    {
        var interceptor = new CacheInvalidationInterceptor(new RecordingCache());
        using var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql("Host=localhost;Database=none")
            .AddInterceptors(interceptor)
            .Options);

        // Группа и её участник одним сохранением: ключ группы выдаст база. От строки, которой
        // ещё нет, не зависит ни одна запись кэша — её метка не нужна (§2.2 п. 3).
        var group = new HubGroup { Name = "new", Slug = "new", OwnerUserId = 1, ClubId = Club438 };
        db.HubGroups.Add(group);
        db.HubGroupMembers.Add(new HubGroupMember { HubGroup = group, SwimmerId = Swimmer5 });
        Assert.True(db.Entry(group).Property(g => g.Id).IsTemporary); // иначе тест ничего не проверяет

        var tags = interceptor.PreviewTags(db);

        AssertTags(
            [
                CacheTags.Table("HubGroups"), CacheTags.Row("Clubs", Club438),
                CacheTags.Table("HubGroupMembers"), CacheTags.Row("Swimmers", Swimmer5),
            ],
            tags);
    }

    // ── Массовая запись мимо трекера ────────────────────────────────────────────────

    [Fact]
    public async Task BulkWrite_DropsTheTableAndAnyRow()
    {
        var cache = new RecordingCache();
        await using var db = Db(nameof(BulkWrite_DropsTheTableAndAnyRow), cache);

        await db.InvalidateTableCacheAsync<HubGroupMember>();

        // Одна table: прошла бы мимо записей, сузивших таблицу до своих строк (§2.2 п. 6).
        AssertTags(
            [CacheTags.Table("HubGroupMembers"), CacheTags.AnyRow("HubGroupMembers")],
            cache.Single);
    }

    // ── Слияние сохранений одной транзакции ─────────────────────────────────────────

    [Fact]
    public void TransactionMerge_KeepsRowsOfEverySave()
    {
        var first = new CacheInvalidationInterceptor.ChangeTags();
        first.AddRow("HubGroupMembers", [CacheTags.Row("HubGroups", 24)]);
        var second = new CacheInvalidationInterceptor.ChangeTags();
        second.AddRow("HubGroupMembers", [CacheTags.Row("HubGroups", 17)]);
        var pending = new CacheInvalidationInterceptor.ChangeTags();

        first.MergeInto(pending);
        second.MergeInto(pending);

        AssertTags(
            [CacheTags.Table("HubGroupMembers"), CacheTags.Row("HubGroups", 24), CacheTags.Row("HubGroups", 17)],
            pending.ToTags());
    }

    [Fact]
    public void TransactionMerge_OverMaxRowsInTotal_CompressesToAnyRow()
    {
        var pending = new CacheInvalidationInterceptor.ChangeTags();
        var half = CacheInvalidationInterceptor.MaxRowsPerTable / 2 + 1;
        for (var save = 0; save < 2; save++)
        {
            var tags = new CacheInvalidationInterceptor.ChangeTags();
            for (var i = 0; i < half; i++) tags.AddRow("Results", [CacheTags.Row("Swimmers", save * 1000 + i)]);
            tags.MergeInto(pending);
        }

        AssertTags([CacheTags.Table("Results"), CacheTags.AnyRow("Results")], pending.ToTags());
    }

    [Fact]
    public void TransactionMerge_UnknownRowsStayUnknown()
    {
        var pending = new CacheInvalidationInterceptor.ChangeTags();
        var bulk = new CacheInvalidationInterceptor.ChangeTags();
        bulk.AddUnknown("Results");
        bulk.MergeInto(pending);
        var save = new CacheInvalidationInterceptor.ChangeTags();
        save.AddRow("Results", [CacheTags.Row("Swimmers", 5)]);
        save.MergeInto(pending);

        AssertTags([CacheTags.Table("Results"), CacheTags.AnyRow("Results")], pending.ToTags());
    }
}

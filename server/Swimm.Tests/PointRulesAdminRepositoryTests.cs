using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Админский CRUD правил очков (Э3, /Admin/PointsRules): шкала текстом, гарды удаления,
/// уникальность версии, независимость двух видов правил.
/// </summary>
public class PointRulesAdminRepositoryTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static PointRulesAdminRepository Repo(SwimmDbContext db) => new(db, new NullCache());

    private static PointRuleInputDto Input(string version, params int[] points) => new()
    {
        Version = version,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Scope = "all",
        Entries = points.Select((p, i) => new PointRuleEntryDto { Place = i + 1, Points = p }).ToList()
    };

    /// <summary>Соревнование (день) с привязкой к клубному правилу.</summary>
    private static Competition Comp(int id, string name, string date, int? clubsRuleId, int? eventId = null) => new()
    {
        Id = id, Name = name, Date = date, PoolType = "50m",
        EventId = eventId, PointRuleClubsId = clubsRuleId
    };

    // ── панель «Соревнования правила» ─────────────────────────────────────────

    [Fact]
    public async Task Competitions_ListsOnlyExplicitlyBound_AndFoldsEventIntoOneRow()
    {
        await using var db = CreateDb(nameof(Competitions_ListsOnlyExplicitlyBound_AndFoldsEventIntoOneRow));
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "Winter champs" });
        db.Competitions.AddRange(
            Comp(1, "day 1", "10/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(2, "day 2", "11/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(3, "Single meet", "05/02/2026", clubsRuleId: 1),
            Comp(4, "Other rule", "07/02/2026", clubsRuleId: 2),
            Comp(5, "No rule at all", "08/02/2026", clubsRuleId: null));
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetCompetitionsAsync(PointRuleKind.Clubs, 1);

        Assert.Equal(2, rows.Count);
        var single = rows.Single(r => r.Id == 3);
        Assert.Equal("Single meet", single.Name);
        Assert.Equal(1, single.DayCount);

        // Многодневка — одна строка: имя события, «голова» = первый день, счётчик дней = 2.
        var multi = rows.Single(r => r.EventId == 7);
        Assert.Equal("Winter champs", multi.Name);
        Assert.Equal(1, multi.Id);
        Assert.Equal(2, multi.DayCount);
    }

    [Fact]
    public async Task Reassign_AppliesToEveryDayOfEvent()
    {
        await using var db = CreateDb(nameof(Reassign_AppliesToEveryDayOfEvent));
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "Winter champs" });
        db.Competitions.AddRange(
            Comp(1, "day 1", "10/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(2, "day 2", "11/01/2026", clubsRuleId: 1, eventId: 7));
        db.PointRulesClubs.Add(new PointRuleClubs { Id = 9, Version = "target", Scope = "all" });
        await db.SaveChangesAsync();

        var res = await Repo(db).ReassignCompetitionsAsync(
            PointRuleKind.Clubs, [new PointRuleReassignItem(1, 9)]);

        Assert.True(res.Success);
        Assert.Equal(1, res.Id); // одно логическое соревнование
        Assert.All(await db.Competitions.ToListAsync(), c => Assert.Equal(9, c.PointRuleClubsId));
    }

    [Fact]
    public async Task Reassign_NullRule_DropsBindingToAuto()
    {
        await using var db = CreateDb(nameof(Reassign_NullRule_DropsBindingToAuto));
        db.Competitions.Add(Comp(1, "Meet", "10/01/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();

        var res = await Repo(db).ReassignCompetitionsAsync(
            PointRuleKind.Clubs, [new PointRuleReassignItem(1, null)]);

        Assert.True(res.Success);
        Assert.Null((await db.Competitions.FindAsync(1))!.PointRuleClubsId);
    }

    [Fact]
    public async Task Reassign_ReportsZero_WhenNothingChanged()
    {
        await using var db = CreateDb(nameof(Reassign_ReportsZero_WhenNothingChanged));
        db.Competitions.Add(Comp(1, "Meet", "10/01/2026", clubsRuleId: 1));
        db.PointRulesClubs.Add(new PointRuleClubs { Id = 1, Version = "same", Scope = "all" });
        await db.SaveChangesAsync();

        var res = await Repo(db).ReassignCompetitionsAsync(
            PointRuleKind.Clubs, [new PointRuleReassignItem(1, 1)]);

        Assert.True(res.Success);
        Assert.Equal(0, res.Id);
    }

    [Fact]
    public async Task Reassign_RejectsUnknownRule_WithoutTouchingData()
    {
        await using var db = CreateDb(nameof(Reassign_RejectsUnknownRule_WithoutTouchingData));
        db.Competitions.Add(Comp(1, "Meet", "10/01/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();

        var res = await Repo(db).ReassignCompetitionsAsync(
            PointRuleKind.Clubs, [new PointRuleReassignItem(1, 404)]);

        Assert.False(res.Success);
        Assert.Contains("404", res.Error);
        Assert.Equal(1, (await db.Competitions.FindAsync(1))!.PointRuleClubsId);
    }

    [Fact]
    public async Task Reassign_ClubsKind_DoesNotTouchSwimmersBinding()
    {
        await using var db = CreateDb(nameof(Reassign_ClubsKind_DoesNotTouchSwimmersBinding));
        var comp = Comp(1, "Meet", "10/01/2026", clubsRuleId: 1);
        comp.PointRuleSwimmersId = 5;
        db.Competitions.Add(comp);
        db.PointRulesClubs.Add(new PointRuleClubs { Id = 9, Version = "target", Scope = "all" });
        await db.SaveChangesAsync();

        await Repo(db).ReassignCompetitionsAsync(PointRuleKind.Clubs, [new PointRuleReassignItem(1, 9)]);

        var saved = await db.Competitions.FindAsync(1);
        Assert.Equal(9, saved!.PointRuleClubsId);
        Assert.Equal(5, saved.PointRuleSwimmersId);
    }

    [Fact]
    public async Task CompetitionCount_CountsEventAsOne_NotPerDay()
    {
        await using var db = CreateDb(nameof(CompetitionCount_CountsEventAsOne_NotPerDay));
        db.PointRulesClubs.Add(new PointRuleClubs { Id = 1, Version = "v1", Scope = "all" });
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "Champs" });
        db.Competitions.AddRange(
            Comp(1, "day 1", "10/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(2, "day 2", "11/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(3, "Single", "05/02/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();

        var rules = await Repo(db).GetAllAsync(PointRuleKind.Clubs);

        // 3 строки-дня, но 2 логических соревнования — столько же, сколько строк в панели.
        Assert.Equal(2, rules.Single(r => r.Id == 1).CompetitionCount);
    }

    // ── ручная сверка очков ───────────────────────────────────────────────────

    [Fact]
    public async Task ToggleVerified_MarksEveryDayOfEvent_AndFlipsBack()
    {
        await using var db = CreateDb(nameof(ToggleVerified_MarksEveryDayOfEvent_AndFlipsBack));
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "Champs" });
        db.Competitions.AddRange(
            Comp(1, "day 1", "10/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(2, "day 2", "11/01/2026", clubsRuleId: 1, eventId: 7));
        await db.SaveChangesAsync();

        var on = await Repo(db).ToggleVerifiedAsync(
            PointRuleKind.Clubs, 1, PointsVerifiedKinds.Official, "vlad");
        Assert.True(on.Success);
        Assert.Equal(1, on.Id);
        Assert.All(await db.Competitions.ToListAsync(), c =>
        {
            Assert.NotNull(c.ClubPointsVerifiedAt);
            Assert.Equal("vlad", c.ClubPointsVerifiedBy);
            Assert.Equal(PointsVerifiedKinds.Official, c.ClubPointsVerifiedKind);
        });

        // Повторный клик по тому же итогу снимает отметку — со всех дней сразу.
        var off = await Repo(db).ToggleVerifiedAsync(
            PointRuleKind.Clubs, 2, PointsVerifiedKinds.Official, "vlad");
        Assert.Equal(0, off.Id);
        Assert.All(await db.Competitions.ToListAsync(), c =>
        {
            Assert.Null(c.ClubPointsVerifiedAt);
            Assert.Null(c.ClubPointsVerifiedBy);
            Assert.Null(c.ClubPointsVerifiedKind);
        });
    }

    [Fact]
    public async Task ToggleVerified_ClubsAndHighPoint_AreIndependent()
    {
        await using var db = CreateDb(nameof(ToggleVerified_ClubsAndHighPoint_AreIndependent));
        db.Competitions.Add(Comp(1, "Meet", "10/01/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();

        await Repo(db).ToggleVerifiedAsync(PointRuleKind.Clubs, 1, PointsVerifiedKinds.Official, "vlad");

        var saved = await db.Competitions.FindAsync(1);
        Assert.NotNull(saved!.ClubPointsVerifiedAt);
        Assert.Null(saved.SwimmersPointsVerifiedAt);
    }

    [Fact]
    public async Task VerifiedCount_CountsEventOnce_AndOnlyVerified()
    {
        await using var db = CreateDb(nameof(VerifiedCount_CountsEventOnce_AndOnlyVerified));
        db.PointRulesClubs.Add(new PointRuleClubs { Id = 1, Version = "v1", Scope = "all" });
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "Champs" });
        db.Competitions.AddRange(
            Comp(1, "day 1", "10/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(2, "day 2", "11/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(3, "Single", "05/02/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();

        await Repo(db).ToggleVerifiedAsync(PointRuleKind.Clubs, 1, PointsVerifiedKinds.Official, "vlad");
        await Repo(db).ToggleVerifiedAsync(PointRuleKind.Clubs, 3, PointsVerifiedKinds.Accepted, "vlad");

        var rule = (await Repo(db).GetAllAsync(PointRuleKind.Clubs)).Single(r => r.Id == 1);
        Assert.Equal(2, rule.CompetitionCount);
        Assert.Equal(1, rule.VerifiedCount);
        Assert.Equal(1, rule.AcceptedCount);

        var rows = await Repo(db).GetCompetitionsAsync(PointRuleKind.Clubs, 1);
        Assert.Equal(PointsVerifiedKinds.Official, rows.Single(r => r.EventId == 7).VerifiedKind);
        Assert.Equal(PointsVerifiedKinds.Accepted, rows.Single(r => r.Id == 3).VerifiedKind);
    }

    [Fact]
    public async Task ToggleVerified_SwitchesBetweenKinds_InsteadOfStacking()
    {
        await using var db = CreateDb(nameof(ToggleVerified_SwitchesBetweenKinds_InsteadOfStacking));
        db.Competitions.Add(Comp(1, "Meet", "10/01/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();

        await Repo(db).ToggleVerifiedAsync(PointRuleKind.Clubs, 1, PointsVerifiedKinds.Official, "vlad");
        var switched = await Repo(db).ToggleVerifiedAsync(
            PointRuleKind.Clubs, 1, PointsVerifiedKinds.Accepted, "vlad");

        // Не «две галочки», а переключение: отметка одна и теперь другая.
        Assert.Equal(1, switched.Id);
        var saved = await db.Competitions.FindAsync(1);
        Assert.Equal(PointsVerifiedKinds.Accepted, saved!.ClubPointsVerifiedKind);
        Assert.NotNull(saved.ClubPointsVerifiedAt);
    }

    [Fact]
    public async Task ToggleVerified_Mismatch_IsItsOwnState_AndCounted()
    {
        await using var db = CreateDb(nameof(ToggleVerified_Mismatch_IsItsOwnState_AndCounted));
        db.PointRulesClubs.Add(new PointRuleClubs { Id = 1, Version = "v1", Scope = "all" });
        db.Competitions.AddRange(
            Comp(1, "Meet A", "10/01/2026", clubsRuleId: 1),
            Comp(2, "Meet B", "11/01/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();

        await Repo(db).ToggleVerifiedAsync(PointRuleKind.Clubs, 1, PointsVerifiedKinds.Mismatch, "vlad");
        await Repo(db).ToggleVerifiedAsync(PointRuleKind.Clubs, 2, PointsVerifiedKinds.Official, "vlad");

        var rule = (await Repo(db).GetAllAsync(PointRuleKind.Clubs)).Single(r => r.Id == 1);
        Assert.Equal(1, rule.MismatchCount);
        Assert.Equal(1, rule.VerifiedCount);
        Assert.Equal(0, rule.AcceptedCount);

        var rows = await Repo(db).GetCompetitionsAsync(PointRuleKind.Clubs, 1);
        Assert.Equal(PointsVerifiedKinds.Mismatch, rows.Single(r => r.Id == 1).VerifiedKind);
    }

    [Fact]
    public async Task ToggleVerified_RejectsUnknownKind()
    {
        await using var db = CreateDb(nameof(ToggleVerified_RejectsUnknownKind));
        db.Competitions.Add(Comp(1, "Meet", "10/01/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();

        var res = await Repo(db).ToggleVerifiedAsync(PointRuleKind.Clubs, 1, "whatever", "vlad");

        Assert.False(res.Success);
        Assert.Null((await db.Competitions.FindAsync(1))!.ClubPointsVerifiedKind);
    }

    // ── шкала текстом ─────────────────────────────────────────────────────────

    [Fact]
    public void ScaleText_ParsesCommaList_AsPlacesFromOne()
    {
        Assert.True(PointRuleScaleText.TryParse("30, 28,26", out var entries, out var error));
        Assert.Null(error);
        Assert.Equal([(1, 30), (2, 28), (3, 26)], entries.Select(e => (e.Place, e.Points)));
    }

    [Fact]
    public void ScaleText_ParsesExplicitPlaces_AndSorts()
    {
        Assert.True(PointRuleScaleText.TryParse("5 = 10\n1: 30", out var entries, out _));
        Assert.Equal([(1, 30), (5, 10)], entries.Select(e => (e.Place, e.Points)));
    }

    [Fact]
    public void ScaleText_ContinuesNumberingAfterExplicitPlace()
    {
        Assert.True(PointRuleScaleText.TryParse("3=10, 9, 8", out var entries, out _));
        Assert.Equal([(3, 10), (4, 9), (5, 8)], entries.Select(e => (e.Place, e.Points)));
    }

    [Fact]
    public void ScaleText_RejectsDuplicatePlace()
    {
        Assert.False(PointRuleScaleText.TryParse("1=30, 1=28", out _, out var error));
        Assert.Contains("дважды", error);
    }

    [Fact]
    public void ScaleText_RejectsGarbage()
    {
        Assert.False(PointRuleScaleText.TryParse("тридцать", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ScaleText_EmptyInput_IsEmptyScale()
    {
        Assert.True(PointRuleScaleText.TryParse("  ", out var entries, out _));
        Assert.Empty(entries);
    }

    [Fact]
    public void ScaleText_FormatsContiguousAsList_AndSparseAsLines()
    {
        var contiguous = new List<PointRuleEntryDto>
        {
            new() { Place = 1, Points = 30 }, new() { Place = 2, Points = 28 }
        };
        Assert.Equal("30, 28", PointRuleScaleText.Format(contiguous));

        var sparse = new List<PointRuleEntryDto>
        {
            new() { Place = 1, Points = 30 }, new() { Place = 7, Points = 5 }
        };
        Assert.Equal("1 = 30\n7 = 5", PointRuleScaleText.Format(sparse));
    }

    [Fact]
    public void ScaleText_RoundTrips()
    {
        Assert.True(PointRuleScaleText.TryParse("30, 28, 26", out var entries, out _));
        Assert.Equal("30, 28, 26", PointRuleScaleText.Format(entries));
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_StoresRuleWithScale()
    {
        await using var db = CreateDb(nameof(Create_StoresRuleWithScale));

        var res = await Repo(db).CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30, 28, 26));
        Assert.True(res.Success);

        var saved = await Repo(db).GetByIdAsync(PointRuleKind.Clubs, res.Id);
        Assert.NotNull(saved);
        Assert.Equal("2026.01", saved!.Version);
        Assert.Equal(3, saved.Entries.Count);
        Assert.Equal(30, saved.Entries[0].Points);
    }

    [Fact]
    public async Task Create_RejectsDuplicateVersion_WithinSameKind()
    {
        await using var db = CreateDb(nameof(Create_RejectsDuplicateVersion_WithinSameKind));
        var repo = Repo(db);

        Assert.True((await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30))).Success);
        var dup = await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30));

        Assert.False(dup.Success);
        Assert.Contains("занята", dup.Error);
    }

    [Fact]
    public async Task Create_AllowsSameVersion_InOtherKind()
    {
        await using var db = CreateDb(nameof(Create_AllowsSameVersion_InOtherKind));
        var repo = Repo(db);

        Assert.True((await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30))).Success);
        Assert.True((await repo.CreateAsync(PointRuleKind.Swimmers, Input("2026.01", 13))).Success);
    }

    [Fact]
    public async Task Create_RejectsBadScope()
    {
        await using var db = CreateDb(nameof(Create_RejectsBadScope));
        var input = Input("2026.01", 30);
        input.Scope = "everyone";

        var res = await Repo(db).CreateAsync(PointRuleKind.Clubs, input);
        Assert.False(res.Success);
        Assert.Contains("scope", res.Error);
    }

    [Fact]
    public async Task Create_Swimmers_RejectsBadPointsSourceAndGroupBy()
    {
        await using var db = CreateDb(nameof(Create_Swimmers_RejectsBadPointsSourceAndGroupBy));
        var repo = Repo(db);

        var badSource = Input("2026.01", 13);
        badSource.PointsSource = "magic";
        Assert.False((await repo.CreateAsync(PointRuleKind.Swimmers, badSource)).Success);

        var badGroup = Input("2026.02", 13);
        badGroup.GroupBy = "club";
        Assert.False((await repo.CreateAsync(PointRuleKind.Swimmers, badGroup)).Success);
    }

    [Fact]
    public async Task Update_RewritesScaleCompletely()
    {
        await using var db = CreateDb(nameof(Update_RewritesScaleCompletely));
        var repo = Repo(db);

        var created = await repo.CreateAsync(PointRuleKind.Swimmers, Input("2026.01", 13, 11, 10));
        var res = await repo.UpdateAsync(PointRuleKind.Swimmers, created.Id, Input("2026.01", 20, 18));

        Assert.True(res.Success);
        var saved = await repo.GetByIdAsync(PointRuleKind.Swimmers, created.Id);
        Assert.Equal([(1, 20), (2, 18)], saved!.Entries.Select(e => (e.Place, e.Points)));
    }

    [Fact]
    public async Task Delete_BlockedWhileCompetitionsReferenceRule()
    {
        await using var db = CreateDb(nameof(Delete_BlockedWhileCompetitionsReferenceRule));
        var repo = Repo(db);

        var created = await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30));
        db.Competitions.Add(new Competition
        {
            Name = "Тест",
            Date = "01/02/2026",
            PointRuleClubsId = created.Id
        });
        await db.SaveChangesAsync();

        var res = await repo.DeleteAsync(PointRuleKind.Clubs, created.Id);

        Assert.False(res.Success);
        Assert.Contains("ссылаются соревнования", res.Error);
        Assert.NotNull(await repo.GetByIdAsync(PointRuleKind.Clubs, created.Id));
    }

    [Fact]
    public async Task Delete_RemovesUnusedRule()
    {
        await using var db = CreateDb(nameof(Delete_RemovesUnusedRule));
        var repo = Repo(db);

        var created = await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30));
        var res = await repo.DeleteAsync(PointRuleKind.Clubs, created.Id);

        Assert.True(res.Success);
        Assert.Null(await repo.GetByIdAsync(PointRuleKind.Clubs, created.Id));
    }

    [Fact]
    public async Task GetAll_CountsEntriesAndBoundCompetitions()
    {
        await using var db = CreateDb(nameof(GetAll_CountsEntriesAndBoundCompetitions));
        var repo = Repo(db);

        var created = await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30, 28));
        db.Competitions.Add(new Competition { Name = "A", Date = "01/02/2026", PointRuleClubsId = created.Id });
        db.Competitions.Add(new Competition { Name = "B", Date = "01/03/2026", PointRuleClubsId = created.Id });
        db.Competitions.Add(new Competition { Name = "C", Date = "01/04/2026" });
        await db.SaveChangesAsync();

        var row = Assert.Single(await repo.GetAllAsync(PointRuleKind.Clubs));
        Assert.Equal(2, row.EntryCount);
        Assert.Equal(2, row.CompetitionCount);
    }

    // ── пояснение к расхождению ───────────────────────────────────────────────

    [Fact]
    public async Task MismatchNote_SavedForEveryDayOfEvent()
    {
        // Расхождение — свойство соревнования, а не дня: попап на витрине один на все дни.
        await using var db = CreateDb(nameof(MismatchNote_SavedForEveryDayOfEvent));
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "Champs" });
        db.Competitions.AddRange(
            Comp(1, "day 1", "10/01/2026", clubsRuleId: 1, eventId: 7),
            Comp(2, "day 2", "11/01/2026", clubsRuleId: 1, eventId: 7));
        await db.SaveChangesAsync();

        var res = await Repo(db).SetClubMismatchNoteAsync(1, "  Places 21-22 were scored 6 and 5.  ");

        Assert.True(res.Success);
        Assert.All(await db.Competitions.ToListAsync(),
            c => Assert.Equal("Places 21-22 were scored 6 and 5.", c.ClubPointsVerifiedNote));
    }

    [Fact]
    public async Task MismatchNote_EmptyText_ClearsIt()
    {
        await using var db = CreateDb(nameof(MismatchNote_EmptyText_ClearsIt));
        var comp = Comp(1, "Meet", "10/01/2026", clubsRuleId: 1);
        comp.ClubPointsVerifiedNote = "old text";
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        var res = await Repo(db).SetClubMismatchNoteAsync(1, "   ");

        Assert.True(res.Success);
        Assert.Equal(0, res.Id); // 0 = стёрто, 1 = записано
        Assert.Null((await db.Competitions.FindAsync(1))!.ClubPointsVerifiedNote);
    }

    [Fact]
    public async Task MismatchNote_UnknownCompetition_Fails()
    {
        await using var db = CreateDb(nameof(MismatchNote_UnknownCompetition_Fails));

        var res = await Repo(db).SetClubMismatchNoteAsync(404, "note");

        Assert.False(res.Success);
        Assert.Contains("404", res.Error);
    }

    [Fact]
    public async Task Competitions_PanelShowsCurrentNote()
    {
        await using var db = CreateDb(nameof(Competitions_PanelShowsCurrentNote));
        var comp = Comp(1, "Meet", "10/01/2026", clubsRuleId: 1);
        comp.ClubPointsVerifiedKind = PointsVerifiedKinds.Mismatch;
        comp.ClubPointsVerifiedNote = "Official table used a different tail.";
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetCompetitionsAsync(PointRuleKind.Clubs, 1);

        Assert.Equal("Official table used a different tail.", rows.Single().MismatchNote);
    }
}

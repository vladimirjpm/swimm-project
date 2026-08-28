using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Забор стартового протокола (docs/plans/start-list-plan.md, шаг С4): программа дня + заплывы
/// → заявки. Провайдер подменён, сети нет.
/// </summary>
public class StartListPullServiceTests
{
    private const int OrgCompId = 16786;
    private const int LogligId = 14208;

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>Провайдер-заглушка: отдаёт заранее заданные сетку и стартовые протоколы.</summary>
    private sealed class FakeProvider(
        IReadOnlyList<LogligDisciplineGridRowDto> grid,
        Dictionary<int, LogligStartListDto> startLists,
        HashSet<int>? failing = null) : ICompetitionDiscoveryProvider
    {
        public Task<IReadOnlyList<LogligDisciplineGridRowDto>> FetchDisciplineGridAsync(
            int logligId, CancellationToken ct = default) => Task.FromResult(grid);

        public Task<LogligStartListDto> FetchStartListAsync(int disciplineId, CancellationToken ct = default)
        {
            if (failing?.Contains(disciplineId) == true)
                throw new InvalidOperationException("сеть отвалилась");
            return Task.FromResult(startLists[disciplineId]);
        }

        public Task<IReadOnlyList<DiscoveredListItem>> FetchListAsync(bool finished, int? year = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<DiscoveredDetails> FetchDetailsAsync(int orgCompId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<byte[]> FetchResultsPdfAsync(int logligId, string culture = "he-IL", CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<int>> FetchEventIdsAsync(int logligId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<LogligEventResultsDto> FetchEventResultsAsync(int eventId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static LogligDisciplineGridRowDto Event(
        int disciplineId, int number = 1, string style = "freestyle", string distance = "50",
        string gender = "female", string ageBand = "10", bool relay = false,
        DateTime? startAt = null) =>
        new(disciplineId, number, $"{distance} стиль", $"категория {ageBand}", style, distance,
            gender, ageBand, relay, null, startAt ?? new DateTime(2026, 2, 19, 10, 6, 0), 0, 0);

    private static LogligStartListRowDto Row(
        int heat, int lane, int? logligId, string name, int? year = 2016,
        string club = "Клуб", string? seed = "01:42.72", string? heatStart = "10:06") =>
        new(heat, lane, logligId, name, year, club, seed, "timed-final", heatStart);

    private static LogligStartListDto StartList(params LogligStartListRowDto[] rows) =>
        new("Соревнование", "19/02/2026", "50 стиль", "freestyle", "50", false, rows);

    private static async Task<SwimmDbContext> SeedAsync(string name, Action<SwimmDbContext>? extra = null)
    {
        var db = CreateDb(name);
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = OrgCompId,
            LogligId = LogligId,
            Name = "Чемпионат",
            DateStart = new DateTime(2026, 2, 19),
            DateEnd = new DateTime(2026, 2, 19)
        });
        db.Clubs.Add(new Club { Name = "Клуб", NameEn = "Club" });
        db.Styles.Add(new Style { Name = "freestyle" });
        extra?.Invoke(db);
        await db.SaveChangesAsync();
        return db;
    }

    private static StartListPullService Service(SwimmDbContext db, ICompetitionDiscoveryProvider provider) =>
        new(db, provider, NullLogger<StartListPullService>.Instance);

    // ── Основной путь ────────────────────────────────────────────────────────

    /// <summary>
    /// Регрессия: у заплыва не назначено время старта → дата дня берётся из «Входящих»,
    /// а там DateStart приходит из timestamptz с Kind=Utc. Колонка CompDate календарная,
    /// и Npgsql на такой паре бросал ArgumentException — забор падал целиком (пример из
    /// жизни: compID 16787, соседние округа того же чемпионата затянулись нормально).
    /// InMemory-провайдер исключения не бросает, поэтому проверяем сам Kind.
    /// </summary>
    [Fact]
    public async Task Pull_EventWithoutStartTime_CompDateIsCalendarKind()
    {
        await using var db = await SeedAsync(nameof(Pull_EventWithoutStartTime_CompDateIsCalendarKind));
        db.DiscoveredCompetitions.Single().DateStart =
            DateTime.SpecifyKind(new DateTime(2026, 2, 16), DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var provider = new FakeProvider(
            [Event(76321, startAt: null)],
            new() { [76321] = StartList(Row(2, 5, 297591, "אביגייל יבסייב", heatStart: null)) });

        var report = await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(StartListPullStatus.Ok, report.Status);
        Assert.Equal(DateTimeKind.Unspecified, (await db.CompetitionEntries.SingleAsync()).CompDate.Kind);
    }

    [Fact]
    public async Task Pull_WritesEntries_AndKeepsCompetitionsUntouched()
    {
        await using var db = await SeedAsync(nameof(Pull_WritesEntries_AndKeepsCompetitionsUntouched));
        var provider = new FakeProvider(
            [Event(76321)],
            new() { [76321] = StartList(Row(2, 5, 297591, "אביגייל יבסייב")) });

        var report = await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(StartListPullStatus.Ok, report.Status);
        Assert.Equal(1, report.Added);

        var entry = await db.CompetitionEntries.SingleAsync();
        Assert.Equal(OrgCompId, entry.OrgCompId);
        Assert.Equal(76321, entry.OrgDisciplineId);
        Assert.Equal(2, entry.Heat);
        Assert.Equal(5, entry.Lane);
        Assert.Equal("50", entry.Distance);
        Assert.Equal("female", entry.Gender);
        Assert.Equal(CompetitionEntryStatus.Entered, entry.Status);

        // Главное решение схемы: справочник соревнований до старта не трогаем, иначе
        // соревнование выпадет из автозабора результатов (BulkPullService).
        Assert.Null(entry.CompetitionId);
        Assert.Equal(0, await db.Competitions.CountAsync());
    }

    [Fact]
    public async Task Pull_ConvertsHeatTimeFromIsraelLocalToUtc()
    {
        await using var db = await SeedAsync(nameof(Pull_ConvertsHeatTimeFromIsraelLocalToUtc));
        var provider = new FakeProvider(
            [Event(76321, startAt: new DateTime(2026, 2, 19, 10, 0, 0))],
            new() { [76321] = StartList(Row(2, 5, 1, "Пловец", heatStart: "10:09")) });

        await Service(db, provider).PullAsync(OrgCompId);

        // Февраль — зимнее время, Израиль UTC+2: 10:09 местного = 08:09 UTC.
        var entry = await db.CompetitionEntries.SingleAsync();
        Assert.Equal(new DateTime(2026, 2, 19, 8, 9, 0, DateTimeKind.Utc), entry.HeatStartAt);
    }

    [Fact]
    public async Task Pull_TakesDayFromEventStart_NotFromEventRange()
    {
        // У многодневки день заплыва зашит в его собственное время старта — это точнее,
        // чем дата начала всего события.
        await using var db = await SeedAsync(nameof(Pull_TakesDayFromEventStart_NotFromEventRange));
        var provider = new FakeProvider(
            [Event(1, startAt: new DateTime(2026, 2, 21, 9, 0, 0))],
            new() { [1] = StartList(Row(1, 1, 1, "Пловец", heatStart: "09:00")) });

        await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(new DateTime(2026, 2, 21), (await db.CompetitionEntries.SingleAsync()).CompDate);
    }

    // ── Пловцы ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pull_MatchesSwimmerByLogligId_NotByName()
    {
        // Имя в источнике только на иврите и печатается иначе, чем у нас, — держаться надо
        // за loglig-id со ссылки.
        await using var db = await SeedAsync(nameof(Pull_MatchesSwimmerByLogligId_NotByName), d =>
            d.Swimmers.Add(new Swimmer
            {
                LastName = "Совсем", FirstName = "Другое", BirthYear = 2016, LogligId = 297591
            }));

        var provider = new FakeProvider(
            [Event(76321)],
            new() { [76321] = StartList(Row(2, 5, 297591, "אביגייל יבסייב")) });

        var report = await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(0, report.SwimmersCreated);
        var swimmer = await db.Swimmers.SingleAsync();
        Assert.Equal((await db.CompetitionEntries.SingleAsync()).SwimmerId, swimmer.Id);
    }

    [Fact]
    public async Task Pull_StampsLogligIdOnKnownSwimmerThatLacksIt()
    {
        // Побочная выгода забора: у 755 пловцов loglig-id нет, а в заявке он есть всегда.
        await using var db = await SeedAsync(nameof(Pull_StampsLogligIdOnKnownSwimmerThatLacksIt), d =>
            d.Swimmers.Add(new Swimmer { LastName = "כהן", FirstName = "עלמה", BirthYear = 2016 }));

        var provider = new FakeProvider(
            [Event(76321)],
            new() { [76321] = StartList(Row(2, 3, 424242, "עלמה כהן")) });

        var report = await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(0, report.SwimmersCreated);
        Assert.Equal(1, report.SwimmersStamped);
        var swimmer = await db.Swimmers.SingleAsync();
        Assert.Equal(424242, swimmer.LogligId);
        Assert.Equal("startlist", swimmer.LogligIdSource);
    }

    [Fact]
    public async Task Pull_CreatesNewcomerOnce_EvenAcrossSeveralEvents()
    {
        // Ребёнок записан в три заплыва. Без общей корзины новичков он завёлся бы трижды —
        // и получил бы три карточки вместо одной.
        await using var db = await SeedAsync(nameof(Pull_CreatesNewcomerOnce_EvenAcrossSeveralEvents));
        var provider = new FakeProvider(
            [Event(1, number: 1), Event(2, number: 2), Event(3, number: 3)],
            new()
            {
                [1] = StartList(Row(1, 1, null, "ניב בשן")),
                [2] = StartList(Row(1, 2, null, "ניב בשן")),
                [3] = StartList(Row(1, 3, null, "ניב בשן"))
            });

        var report = await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(1, report.SwimmersCreated);
        Assert.Equal(1, await db.Swimmers.CountAsync());
        Assert.Equal(3, await db.CompetitionEntries.CountAsync());
    }

    // ── Клубы ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pull_UnknownClub_GoesToNoClub_AndIsCounted()
    {
        // Клубы из стартового протокола НЕ заводятся: матчинг-по-имени с созданием — это
        // ровно то, что плодило клубы-дубли (инцидент И-13).
        await using var db = await SeedAsync(nameof(Pull_UnknownClub_GoesToNoClub_AndIsCounted));
        var provider = new FakeProvider(
            [Event(76321)],
            new() { [76321] = StartList(Row(1, 1, 1, "Пловец", club: "Клуб, которого у нас нет")) });

        var report = await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(1, report.ClubsUnmatched);
        Assert.DoesNotContain(await db.Clubs.ToListAsync(), c => c.Name == "Клуб, которого у нас нет");

        var entry = await db.CompetitionEntries.Include(e => e.Club).SingleAsync();
        Assert.Equal("No club", entry.Club.Name);
    }

    // ── Перезабор ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Repull_LaneChange_KeepsEntryIdAndCountsAsMove()
    {
        await using var db = await SeedAsync(nameof(Repull_LaneChange_KeepsEntryIdAndCountsAsMove), d =>
            d.Swimmers.Add(new Swimmer { LastName = "Ф", FirstName = "И", BirthYear = 2016, LogligId = 1 }));

        var before = new FakeProvider([Event(76321)], new() { [76321] = StartList(Row(1, 3, 1, "Пловец")) });
        await Service(db, before).PullAsync(OrgCompId);
        var originalId = (await db.CompetitionEntries.SingleAsync()).Id;

        var after = new FakeProvider([Event(76321)], new() { [76321] = StartList(Row(2, 5, 1, "Пловец")) });
        var report = await Service(db, after).PullAsync(OrgCompId);

        Assert.Equal(1, report.Moved);
        Assert.Equal(0, report.Added);
        Assert.Equal(0, report.Removed);

        var entry = await db.CompetitionEntries.SingleAsync();
        Assert.Equal(originalId, entry.Id);   // заявка не потеряла себя при пересеве
        Assert.Equal(2, entry.Heat);
        Assert.Equal(5, entry.Lane);
    }

    [Fact]
    public async Task Repull_Scratch_RemovesOnlyThatEntry()
    {
        await using var db = await SeedAsync(nameof(Repull_Scratch_RemovesOnlyThatEntry), d =>
        {
            d.Swimmers.Add(new Swimmer { LastName = "А", FirstName = "А", BirthYear = 2016, LogligId = 1 });
            d.Swimmers.Add(new Swimmer { LastName = "Б", FirstName = "Б", BirthYear = 2016, LogligId = 2 });
        });

        var before = new FakeProvider([Event(76321)],
            new() { [76321] = StartList(Row(1, 3, 1, "Первый"), Row(1, 4, 2, "Второй")) });
        await Service(db, before).PullAsync(OrgCompId);

        var after = new FakeProvider([Event(76321)], new() { [76321] = StartList(Row(1, 3, 1, "Первый")) });
        var report = await Service(db, after).PullAsync(OrgCompId);

        Assert.Equal(1, report.Removed);
        Assert.Equal(1, report.Unchanged);
        var entry = await db.CompetitionEntries.SingleAsync();
        Assert.Equal(3, entry.Lane);
    }

    [Fact]
    public async Task Repull_IsIdempotent()
    {
        await using var db = await SeedAsync(nameof(Repull_IsIdempotent), d =>
            d.Swimmers.Add(new Swimmer { LastName = "Ф", FirstName = "И", BirthYear = 2016, LogligId = 1 }));

        var provider = new FakeProvider([Event(76321)], new() { [76321] = StartList(Row(1, 3, 1, "Пловец")) });
        await Service(db, provider).PullAsync(OrgCompId);
        var second = await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Removed);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(1, await db.CompetitionEntries.CountAsync());
    }

    [Fact]
    public async Task Repull_UnreadDiscipline_IsNotWipedOut()
    {
        // Оборванная сеть на середине не должна выглядеть как «все снялись»: сверка идёт
        // ТОЛЬКО по успешно прочитанным заплывам.
        await using var db = await SeedAsync(nameof(Repull_UnreadDiscipline_IsNotWipedOut), d =>
            d.Swimmers.Add(new Swimmer { LastName = "Ф", FirstName = "И", BirthYear = 2016, LogligId = 1 }));

        var lists = new Dictionary<int, LogligStartListDto>
        {
            [1] = StartList(Row(1, 3, 1, "Пловец")),
            [2] = StartList(Row(1, 4, 1, "Пловец"))
        };
        await Service(db, new FakeProvider([Event(1), Event(2, number: 2)], lists)).PullAsync(OrgCompId);
        Assert.Equal(2, await db.CompetitionEntries.CountAsync());

        var flaky = new FakeProvider([Event(1), Event(2, number: 2)], lists, failing: [2]);
        var report = await Service(db, flaky).PullAsync(OrgCompId);

        Assert.Equal(StartListPullStatus.Partial, report.Status);
        Assert.Equal(0, report.Removed);
        Assert.Equal(2, await db.CompetitionEntries.CountAsync());
    }

    // ── Ожидаемые состояния источника ────────────────────────────────────────

    [Fact]
    public async Task Pull_WithoutLogligId_IsEmptyNotError()
    {
        // Риск №1 плана: у предстоящего старта loglig-id может ещё не быть. Это «тянуть
        // пока нечего», а не сбой — иначе админка краснеет всю неделю до соревнования.
        await using var db = CreateDb(nameof(Pull_WithoutLogligId_IsEmptyNotError));
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = OrgCompId, Name = "Будущий старт",
            DateStart = new DateTime(2026, 10, 9), DateEnd = new DateTime(2026, 10, 9)
        });
        await db.SaveChangesAsync();

        var report = await Service(db, new FakeProvider([], new())).PullAsync(OrgCompId);

        Assert.Equal(StartListPullStatus.Empty, report.Status);
        Assert.Equal(0, await db.CompetitionEntries.CountAsync());
    }

    [Fact]
    public async Task Pull_ProgrammePublishedButNotSeeded_IsEmptyNotError()
    {
        await using var db = await SeedAsync(nameof(Pull_ProgrammePublishedButNotSeeded_IsEmptyNotError));
        var provider = new FakeProvider([Event(76321)], new() { [76321] = StartList() });

        var report = await Service(db, provider).PullAsync(OrgCompId);

        Assert.Equal(StartListPullStatus.Empty, report.Status);
        Assert.Equal(1, report.Events);
    }

    [Fact]
    public async Task Pull_UnknownCompetition_IsError()
    {
        await using var db = CreateDb(nameof(Pull_UnknownCompetition_IsError));

        var report = await Service(db, new FakeProvider([], new())).PullAsync(999);

        Assert.Equal(StartListPullStatus.Error, report.Status);
        Assert.NotNull(report.Error);
    }

    // ── Журнал ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryPull_WritesJournalRow_EvenWhenNothingCame()
    {
        // «Почему вчера ничего не приехало» иначе не разобрать — та же роль, что у
        // ImportReconciliation для импорта результатов.
        await using var db = CreateDb(nameof(EveryPull_WritesJournalRow_EvenWhenNothingCame));

        await Service(db, new FakeProvider([], new())).PullAsync(777);

        var pull = await db.StartListPulls.SingleAsync();
        Assert.Equal(777, pull.OrgCompId);
        Assert.Equal(StartListPullStatus.Error, pull.Status);
        Assert.NotNull(pull.Error);
    }

    [Fact]
    public async Task SuccessfulPull_JournalCarriesTheCounters()
    {
        await using var db = await SeedAsync(nameof(SuccessfulPull_JournalCarriesTheCounters), d =>
            d.Swimmers.Add(new Swimmer { LastName = "Ф", FirstName = "И", BirthYear = 2016, LogligId = 1 }));

        var provider = new FakeProvider([Event(76321)], new() { [76321] = StartList(Row(1, 3, 1, "Пловец")) });
        await Service(db, provider).PullAsync(OrgCompId);

        var pull = await db.StartListPulls.SingleAsync();
        Assert.Equal(StartListPullStatus.Ok, pull.Status);
        Assert.Equal(1, pull.Events);
        Assert.Equal(1, pull.Entries);
        Assert.Equal(1, pull.Added);
        Assert.Null(pull.Error);
    }
}

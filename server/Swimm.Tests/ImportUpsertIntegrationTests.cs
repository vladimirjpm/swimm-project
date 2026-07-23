using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Upsert-переимпорт (docs/plans/import-upsert-plan.md, шаг 5) — InMemory, по образцу
/// <see cref="SwimmerImportMatchingTests"/>.
/// </summary>
public class ImportUpsertIntegrationTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static object Item(string lastName, string firstName, int birthYear, string time = "00:30.00",
        int lane = 1, int heat = 1, string competition = "Comp", string date = "01/06/2026",
        string? note = null) => new
        {
            country = "ISR",
            competition,
            date,
            event_style_name = "Freestyle",
            event_style_len = "50",
            event_style_gender = "male",
            pool_type = "25m",
            position = 1,
            heat,
            lane,
            last_name = lastName,
            first_name = firstName,
            birth_year = birthYear,
            club = "Club",
            time,
            note
        };

    private static object AnonymousRelayItem(int lane, int heat = 1, string competition = "Comp",
        string date = "01/06/2026") => new
        {
            country = "ISR",
            competition,
            date,
            event_style_name = "Freestyle",
            event_style_len = "4x50",
            event_style_gender = "male",
            pool_type = "25m",
            position = 1,
            heat,
            lane,
            last_name = "",
            first_name = "",
            birth_year = (int?)null,
            club = "Israel",
            time = "02:00.00",
            is_relay = true,
            relay_team_name = "Israel",
            relay_swimmers_name = ""
        };

    private static object NamedRelayItem(int lane, string lastName, string firstName, int birthYear,
        int heat = 1, string competition = "Comp", string date = "01/06/2026") => new
        {
            country = "ISR",
            competition,
            date,
            event_style_name = "Freestyle",
            event_style_len = "4x50",
            event_style_gender = "male",
            pool_type = "25m",
            position = 1,
            heat,
            lane,
            last_name = lastName,
            first_name = firstName,
            birth_year = birthYear,
            club = "Israel",
            time = "02:00.00",
            is_relay = true,
            relay_team_name = "Israel",
            relay_swimmers_name = $"{firstName} {lastName}"
        };

    private static Stream ToStream(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private static ImportEventOptions Overwrite => new(EventId: null, NewEventName: null, OverwriteExisting: true);
    private static ImportEventOptions OverwriteWithDelete => new(EventId: null, NewEventName: null, OverwriteExisting: true, DeleteMissing: true);

    [Fact]
    public async Task Import_WithOrgCompId_StampsItOnCompetition()
    {
        await using var db = CreateDb(nameof(Import_WithOrgCompId_StampsItOnCompetition));
        var svc = new JsonImportService(db, new NullCacheService());

        var result = await svc.ImportAsync(ToStream(new[] { Item("Cohen", "Tal", 2005, lane: 1) }),
            orgCompId: 16745);

        Assert.Empty(result.ErrorMessages);
        var comp = await db.Competitions.SingleAsync();
        Assert.Equal(16745, comp.OrgCompId);
    }

    [Fact]
    public async Task ReimportIdenticalData_WithFlag_ZeroInsertNUpdateZeroDelete()
    {
        await using var db = CreateDb(nameof(ReimportIdenticalData_WithFlag_ZeroInsertNUpdateZeroDelete));
        var svc = new JsonImportService(db, new NullCacheService());

        var first = await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1),
            Item("Levi", "Dan", 2006, lane: 2)
        }));
        Assert.Empty(first.ErrorMessages);
        Assert.Equal(2, await db.Results.CountAsync());

        var second = await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1),
            Item("Levi", "Dan", 2006, lane: 2)
        }), eventOptions: Overwrite);

        Assert.Empty(second.ErrorMessages);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(2, second.Updated);
        Assert.Equal(0, second.Deleted);
        Assert.Equal(2, await db.Results.CountAsync());
    }

    [Fact]
    public async Task ChangedTime_UpdatesExistingRow_PreservingId()
    {
        await using var db = CreateDb(nameof(ChangedTime_UpdatesExistingRow_PreservingId));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[] { Item("Cohen", "Tal", 2005, time: "00:30.00", lane: 1) }));
        var original = await db.Results.SingleAsync();
        var originalId = original.Id;

        var result = await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", "Tal", 2005, time: "00:29.50", lane: 1) }),
            eventOptions: Overwrite);

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, result.Updated);
        var updated = await db.Results.SingleAsync();
        Assert.Equal(originalId, updated.Id);
        Assert.Equal(29500, updated.TimeMillisecond);
    }

    [Fact]
    public async Task MediaSurvivesReimport()
    {
        await using var db = CreateDb(nameof(MediaSurvivesReimport));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[] { Item("Cohen", "Tal", 2005, lane: 1) }));
        var result = await db.Results.SingleAsync();
        var swimmer = await db.Swimmers.SingleAsync();

        var user = new AppUser { Email = "a@b.com", DisplayName = "A" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var media = new UserMedia
        {
            UserId = user.Id,
            SwimmerId = swimmer.Id,
            ResultId = result.Id,
            Level = "result",
            MediaType = "video",
            SourceType = "youtube",
            Url = "https://youtube.com/watch?v=x"
        };
        db.UserMedia.Add(media);
        await db.SaveChangesAsync();
        var mediaId = media.Id;
        var resultId = result.Id;

        var reimport = await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", "Tal", 2005, time: "00:28.00", lane: 1) }),
            eventOptions: Overwrite);

        Assert.Empty(reimport.ErrorMessages);
        Assert.Equal(1, reimport.Updated);

        var survivingResult = await db.Results.SingleAsync();
        Assert.Equal(resultId, survivingResult.Id);

        var survivingMedia = await db.UserMedia.SingleAsync();
        Assert.Equal(mediaId, survivingMedia.Id);
        Assert.Equal(resultId, survivingMedia.ResultId);
    }

    [Fact]
    public async Task HubGroupMedia_ResultDisappears_NotDeleted_SkippedWithMedia()
    {
        await using var db = CreateDb(nameof(HubGroupMedia_ResultDisappears_NotDeleted_SkippedWithMedia));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1),
            Item("Levi", "Dan", 2006, lane: 2)
        }));

        var laneTwoResult = await db.Results.SingleAsync(r => r.Lane == 2);
        var swimmer = await db.Swimmers.SingleAsync(s => s.LastName == "Levi");

        var group = new HubGroup { Name = "G", Slug = "g" };
        db.HubGroups.Add(group);
        var user = new AppUser { Email = "coach@b.com", DisplayName = "Coach" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        db.HubGroupMedia.Add(new HubGroupMedia
        {
            HubGroupId = group.Id,
            Visibility = HubGroupMediaVisibility.Members,
            SwimmerId = swimmer.Id,
            ResultId = laneTwoResult.Id,
            MediaType = "video",
            SourceType = "youtube",
            Url = "https://youtube.com/watch?v=y",
            CreatedByUserId = user.Id
        });
        await db.SaveChangesAsync();
        var laneTwoResultId = laneTwoResult.Id;

        // Переимпорт без lane=2 — этот результат "исчез из файла". DeleteMissing=true, иначе
        // (новый дефолт) удаление вообще не пытается запуститься и skippedWithMedia не считается.
        var reimport = await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", "Tal", 2005, lane: 1) }),
            eventOptions: OverwriteWithDelete);

        Assert.Empty(reimport.ErrorMessages);
        Assert.Equal(0, reimport.Deleted);
        Assert.Equal(1, reimport.SkippedWithMedia);

        // Результат с HubGroupMedia НЕ удалён, несмотря на исчезновение из файла.
        Assert.True(await db.Results.AnyAsync(r => r.Id == laneTwoResultId));
        Assert.Equal(2, await db.Results.CountAsync());
    }

    [Fact]
    public async Task PartialReimport_OverwriteWithoutDeleteMissing_KeepsMissingRows_ZeroDeleted()
    {
        await using var db = CreateDb(nameof(PartialReimport_OverwriteWithoutDeleteMissing_KeepsMissingRows_ZeroDeleted));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1, time: "00:30.00"),
            Item("Levi", "Dan", 2006, lane: 2)
        }));
        Assert.Equal(2, await db.Results.CountAsync());

        // Партиальный файл: lane=1 обновлён, lane=2 отсутствует. Инцидент 2026-07-20: раньше
        // OverwriteExisting без явного согласия удалял такие "исчезнувшие" строки молча.
        var reimport = await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", "Tal", 2005, lane: 1, time: "00:29.00") }),
            eventOptions: Overwrite);

        Assert.Empty(reimport.ErrorMessages);
        Assert.Equal(1, reimport.Updated);
        Assert.Equal(0, reimport.Deleted);
        Assert.Equal(0, reimport.SkippedWithMedia);
        Assert.Equal(2, await db.Results.CountAsync()); // lane=2 сохранился

        var laneTwo = await db.Results.SingleAsync(r => r.Lane == 2);
        var laneOne = await db.Results.SingleAsync(r => r.Lane == 1);
        Assert.Equal(29000, laneOne.TimeMillisecond); // сматченная строка всё же обновилась
        Assert.NotNull(laneTwo);
    }

    [Fact]
    public async Task PartialReimport_OverwriteWithDeleteMissing_DeletesMissingRows()
    {
        await using var db = CreateDb(nameof(PartialReimport_OverwriteWithDeleteMissing_DeletesMissingRows));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1),
            Item("Levi", "Dan", 2006, lane: 2)
        }));
        Assert.Equal(2, await db.Results.CountAsync());

        var reimport = await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", "Tal", 2005, lane: 1) }),
            eventOptions: OverwriteWithDelete);

        Assert.Empty(reimport.ErrorMessages);
        Assert.Equal(1, reimport.Updated);
        Assert.Equal(1, reimport.Deleted);
        Assert.Equal(0, reimport.SkippedWithMedia);
        Assert.Equal(1, await db.Results.CountAsync());
        Assert.False(await db.Results.AnyAsync(r => r.Lane == 2));
    }

    [Fact]
    public async Task AnonymousRelayLeg_ReplacedByNamedSwimmer_PreservesResultId_MaccabiahCase()
    {
        await using var db = CreateDb(nameof(AnonymousRelayLeg_ReplacedByNamedSwimmer_PreservesResultId_MaccabiahCase));
        var svc = new JsonImportService(db, new NullCacheService());

        var first = await svc.ImportAsync(ToStream(new[] { AnonymousRelayItem(lane: 1) }));
        Assert.Empty(first.ErrorMessages);
        var anonResult = await db.Results.SingleAsync();
        var anonResultId = anonResult.Id;
        var anonSwimmerId = anonResult.SwimmerId;

        // Фикс парсера (восстановление ног эстафет) — тот же заплыв/дорожка, но теперь пловец
        // именован (Маккабиада-кейс из плана).
        var second = await svc.ImportAsync(
            ToStream(new[] { NamedRelayItem(lane: 1, lastName: "Katz", firstName: "Or", birthYear: 2008) }),
            eventOptions: Overwrite);

        Assert.Empty(second.ErrorMessages);
        Assert.Equal(1, second.Updated);
        Assert.Equal(0, second.Inserted);

        var updatedResult = await db.Results.SingleAsync();
        Assert.Equal(anonResultId, updatedResult.Id); // Id заплыва сохранён
        Assert.NotEqual(anonSwimmerId, updatedResult.SwimmerId); // пловец сменился на именованного

        var namedSwimmer = await db.Swimmers.SingleAsync(s => s.Id == updatedResult.SwimmerId);
        Assert.Equal("Katz", namedSwimmer.LastName);

        // Старая анонимная заглушка осиротела и подчищена (Р5).
        Assert.False(await db.Swimmers.AnyAsync(s => s.Id == anonSwimmerId));
    }

    [Fact]
    public async Task RenamedByEventAttachment_ReimportMatchesBySubName_DoesNotDuplicate()
    {
        // Инцидент 2026-07-20 (Маккабиада): Competition.Id 1483/1484/1485 — Name «Maccabiah 2026»
        // (имя CompetitionEvent), SubName «Maccabiah 2025 -» (исходный заголовок файла). Превью
        // (FindExistingCompetitionsAsync) матчит по Name-ИЛИ-SubName и находит соревнование, но
        // раньше ИМПОРТ искал в своём кэше строго по Name и промахивался — создавал НОВОЕ
        // соревнование («Maccabiah 2025 -», Id 1488-1490) вместо апдейта существующего. Этот тест
        // гоняет реальный ImportAsync (не только превью) через тот же сценарий переименования.
        await using var db = CreateDb(nameof(RenamedByEventAttachment_ReimportMatchesBySubName_DoesNotDuplicate));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1, competition: "Maccabiah 2025 -", date: "01/06/2026"),
            Item("Levi", "Dan", 2006, lane: 2, competition: "Maccabiah 2025 -", date: "01/06/2026")
        }));
        var competition = await db.Competitions.SingleAsync();
        var originalCompetitionId = competition.Id;

        // Симулируем привязку дня к CompetitionEvent: Name становится именем события, исходный
        // заголовок файла уходит в SubName (как делает ветка targetEvent != null в ImportAsync).
        competition.Name = "Maccabiah 2026";
        competition.SubName = "Maccabiah 2025 -";
        await db.SaveChangesAsync();

        // Переимпорт того же файла БЕЗ EventId (обычный overwrite, ровно как в инциденте — второй
        // раз файл залили не через "дописать к событию", а как обычный upsert). Заголовок в файле
        // всё ещё "Maccabiah 2025 -" — теперь это SubName существующего соревнования, не Name.
        var reimport = await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1, time: "00:29.00", competition: "Maccabiah 2025 -", date: "01/06/2026"),
            Item("Levi", "Dan", 2006, lane: 2, competition: "Maccabiah 2025 -", date: "01/06/2026")
        }), eventOptions: Overwrite);

        Assert.Empty(reimport.ErrorMessages);
        Assert.DoesNotContain("Дубль", reimport.Message);
        Assert.Equal(2, reimport.Updated);
        Assert.Equal(0, reimport.Inserted);

        // Никакого нового соревнования не создано — тот же Id, Name/SubName не тронуты.
        Assert.Equal(1, await db.Competitions.CountAsync());
        var stillTheSame = await db.Competitions.SingleAsync();
        Assert.Equal(originalCompetitionId, stillTheSame.Id);
        Assert.Equal("Maccabiah 2026", stillTheSame.Name);
        Assert.Equal("Maccabiah 2025 -", stillTheSame.SubName);
        Assert.Equal(2, await db.Results.CountAsync());
    }

    [Fact]
    public async Task RenamedByEventAttachment_ReimportWithoutOverwrite_StillRaisesDuplicateError()
    {
        // Симметричный случай без флага OverwriteExisting: даже когда заголовок файла совпадает
        // только с SubName (не с Name), обычный (не-overwrite) повторный импорт обязан по-прежнему
        // отбиваться «Дубль», а не тихо создавать новое соревнование — фикс матчинга не должен
        // ослаблять защиту от случайного дубля для не-overwrite пути.
        await using var db = CreateDb(nameof(RenamedByEventAttachment_ReimportWithoutOverwrite_StillRaisesDuplicateError));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1, competition: "Maccabiah 2025 -", date: "01/06/2026")
        }));
        var competition = await db.Competitions.SingleAsync();
        competition.Name = "Maccabiah 2026";
        competition.SubName = "Maccabiah 2025 -";
        await db.SaveChangesAsync();

        var reimport = await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1, competition: "Maccabiah 2025 -", date: "01/06/2026")
        })); // без eventOptions — overwriteExisting=false

        Assert.Contains("Дубль", reimport.Message);
        Assert.Equal(1, await db.Competitions.CountAsync()); // не создано нового соревнования
        Assert.Equal(1, await db.Results.CountAsync());
    }

    [Fact]
    public async Task RelayLegKeyCollision_MembershipChangedAndReordered_UpdatesSurvivorsNotDuplicates()
    {
        // Инцидент 2026-07-20 (Маккабиада), вторая половина: relay-leg строки без RelayId делят
        // Heat/Lane/Style/Distance/Gender между собой (ключ Р2 коллизирует). Фикс парсера сменил
        // состав ног между переимпортами (одна нога пропала, оставшиеся переставились местами в
        // файле) — раньше чистый FIFO путал ноги местами / плодил вставки; SwimmerId-приоритет
        // обязан узнать переживших ног по SwimmerId независимо от позиции и обновить их на месте.
        await using var db = CreateDb(nameof(RelayLegKeyCollision_MembershipChangedAndReordered_UpdatesSurvivorsNotDuplicates));
        var svc = new JsonImportService(db, new NullCacheService());

        // Три "ноги" в одной группе коллизии: один и тот же heat/lane (как в реальных данных —
        // relay-leg-строки не несут собственный Lane, парсер проставляет им Lane команды).
        var first = await svc.ImportAsync(ToStream(new[]
        {
            Item("Rtzma", "Cooper", 2007, lane: 5, heat: 1, time: "00:37.32"),
            Item("Semenenko", "Yaroslav", 2008, lane: 5, heat: 1, time: "00:47.75"),
            Item("Iaich", "Itsik", 2006, lane: 5, heat: 1, time: "00:58.52")
        }));
        Assert.Empty(first.ErrorMessages);
        Assert.Equal(3, await db.Results.CountAsync());

        var oldRows = await db.Results.Include(r => r.Swimmer).Where(r => r.Lane == 5).ToListAsync();
        var cooperOldId = oldRows.Single(r => r.Swimmer.LastName == "Rtzma").Id;
        var semenenkoOldId = oldRows.Single(r => r.Swimmer.LastName == "Semenenko").Id;
        var iaichOldId = oldRows.Single(r => r.Swimmer.LastName == "Iaich").Id;

        // Переимпорт после фикса парсера: Iaich пропал из группы, Semenenko и Rtzma остались, но
        // переставлены местами в файле и получили новое время.
        var reimport = await svc.ImportAsync(ToStream(new[]
        {
            Item("Semenenko", "Yaroslav", 2008, lane: 5, heat: 1, time: "00:46.10"),
            Item("Rtzma", "Cooper", 2007, lane: 5, heat: 1, time: "00:36.90")
        }), eventOptions: Overwrite);

        Assert.Empty(reimport.ErrorMessages);
        Assert.Equal(2, reimport.Updated);
        Assert.Equal(0, reimport.Inserted);
        // DeleteMissing=false (default) — Iaich не удалён, просто не тронут.
        Assert.Equal(0, reimport.Deleted);
        Assert.Equal(3, await db.Results.CountAsync());

        var cooperNow = await db.Results.SingleAsync(r => r.Id == cooperOldId);
        var semenenkoNow = await db.Results.SingleAsync(r => r.Id == semenenkoOldId);
        var iaichNow = await db.Results.SingleAsync(r => r.Id == iaichOldId);

        Assert.Equal(36900, cooperNow.TimeMillisecond); // обновлён, Id сохранён
        Assert.Equal(46100, semenenkoNow.TimeMillisecond); // обновлён, Id сохранён
        Assert.Equal(58520, iaichNow.TimeMillisecond); // не тронут (DeleteMissing=false)
    }

    [Fact]
    public async Task ReimportWithoutFlag_StillFailsWithDuplicateError()
    {
        await using var db = CreateDb(nameof(ReimportWithoutFlag_StillFailsWithDuplicateError));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[] { Item("Cohen", "Tal", 2005, lane: 1) }));
        Assert.Equal(1, await db.Results.CountAsync());

        var second = await svc.ImportAsync(ToStream(new[] { Item("Cohen", "Tal", 2005, lane: 1) }));

        Assert.Contains("Дубль", second.Message);
        Assert.Equal(1, await db.Results.CountAsync()); // ничего не изменилось — откат
    }

    // ─── FindExistingCompetitionsAsync: SubName-матчинг + ExistingResultCount (гэп-2 из live-теста) ───
    // Кейс: день привязан к CompetitionEvent → Competition.Name становится именем события,
    // а исходно распарсенный заголовок дня уходит в SubName. Превью должно матчить по обоим полям.

    [Fact]
    public async Task FindExistingCompetitions_MatchesBySubName_WhenNameIsEventRenamed()
    {
        await using var db = CreateDb(nameof(FindExistingCompetitions_MatchesBySubName_WhenNameIsEventRenamed));
        var svc = new JsonImportService(db, new NullCacheService());

        // Импортируем обычное соревнование "Maccabiah 2025 -", затем симулируем переименование
        // при привязке дня к событию: Name -> имя события, SubName -> исходный заголовок.
        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1, competition: "Maccabiah 2025 -", date: "01/06/2026")
        }));
        var competition = await db.Competitions.SingleAsync();
        competition.Name = "Maccabiah 2026";
        competition.SubName = "Maccabiah 2025 -";
        await db.SaveChangesAsync();

        var matches = await svc.FindExistingCompetitionsAsync(new[]
        {
            new ParsedCompetitionSummary("Maccabiah 2025 -", "01/06/2026", 1, "25m")
        });

        var match = Assert.Single(matches);
        Assert.Equal(competition.Id, match.ExistingCompetitionId);
        Assert.Equal("Maccabiah 2026", match.ExistingCompetitionName);
    }

    [Fact]
    public async Task FindExistingCompetitions_FillsExistingResultCount_ForMatchedCompetition()
    {
        await using var db = CreateDb(nameof(FindExistingCompetitions_FillsExistingResultCount_ForMatchedCompetition));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1, competition: "Comp", date: "01/06/2026"),
            Item("Levi", "Dan", 2006, lane: 2, competition: "Comp", date: "01/06/2026")
        }));
        Assert.Equal(2, await db.Results.CountAsync());

        var matches = await svc.FindExistingCompetitionsAsync(new[]
        {
            new ParsedCompetitionSummary("Comp", "01/06/2026", 1, "25m")
        });

        var match = Assert.Single(matches);
        Assert.NotNull(match.ExistingCompetitionId);
        Assert.Equal(2, match.ExistingResultCount);
    }

    [Fact]
    public async Task FindExistingCompetitions_NoMatch_ReturnsNullIdAndNullResultCount()
    {
        await using var db = CreateDb(nameof(FindExistingCompetitions_NoMatch_ReturnsNullIdAndNullResultCount));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", "Tal", 2005, lane: 1, competition: "Comp", date: "01/06/2026")
        }));

        var matches = await svc.FindExistingCompetitionsAsync(new[]
        {
            new ParsedCompetitionSummary("Totally Different Comp", "01/06/2026", 1, "25m")
        });

        var match = Assert.Single(matches);
        Assert.Null(match.ExistingCompetitionId);
        Assert.Null(match.ExistingResultCount);
    }
}

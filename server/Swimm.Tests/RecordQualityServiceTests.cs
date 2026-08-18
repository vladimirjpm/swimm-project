using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка справочника рекордов с протоколами + реестр спорных записей
/// (docs/plans/records-quality-plan.md).
///
/// ⚠ Ключевое поведение, которое эти тесты и охраняют: «заплыв не найден» — НЕ ошибка
/// источника, а «не можем подтвердить» (протоколы загружены не за все годы). Поэтому
/// ненайденный рекорд не порождает запись в реестре сам собой.
/// </summary>
public class RecordQualityServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Swimm.Domain.Entities.Record Rec(
        string time, string style = "backstroke", string distance = "50m",
        string poolType = "50m", string gender = "female", string? date = "20/07/2025",
        string ageKey = "10") => new()
    {
        RegionType = "country",
        RegionCode = "ISR",
        Category = "age",
        AgeKey = ageKey,
        Gender = gender,
        PoolType = poolType,
        Style = style,
        Distance = distance,
        Time = time,
        HolderName = "Holder",
        RecordDate = date
    };

    private static ResultRecord Swim(
        Competition comp, Swimmer swimmer, int timeMs, DateTime date,
        string distance = "50", string gender = "female", int styleId = 100) => new()
    {
        Competition = comp,
        Swimmer = swimmer,
        StyleId = styleId,
        CompetitionDate = date,
        Distance = distance,
        Gender = gender,
        TimeMillisecond = timeMs,
        TimeOriginal = "x"
    };

    private static async Task<(Competition Comp, Swimmer Swimmer)> SeedBaseAsync(
        SwimmDbContext db, string poolType = "50m")
    {
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer
        {
            Club = club, LastName = "L", FirstName = "F",
            LastNameEn = "L", FirstNameEn = "F", BirthYear = 2015
        };
        var comp = new Competition { Name = "Meet", Date = "20/07/2025", PoolType = poolType };
        db.AddRange(club, swimmer, comp, new Style { Id = 100, Name = "backstroke" });
        await db.SaveChangesAsync();
        return (comp, swimmer);
    }

    [Fact]
    public async Task Verify_MarksFound_WhenSwimWithSameTimeExists()
    {
        using var db = CreateDb(nameof(Verify_MarksFound_WhenSwimWithSameTimeExists));
        var (comp, swimmer) = await SeedBaseAsync(db);
        db.Add(Rec("34.08"));
        db.Add(Swim(comp, swimmer, 34_080, new DateTime(2025, 7, 20)));
        await db.SaveChangesAsync();

        var result = await new RecordQualityService(db).VerifyAllAsync();

        Assert.Equal(1, result.Checked);
        Assert.Equal(1, result.Found);
        var row = Assert.Single(db.RecordVerifications);
        Assert.True(row.Found);
        Assert.Equal(swimmer.Id, row.SwimmerId);
        Assert.True(row.DateMatched);
    }

    [Fact]
    public async Task Verify_NotFound_IsNeutral_AndCreatesNoIssue()
    {
        using var db = CreateDb(nameof(Verify_NotFound_IsNeutral_AndCreatesNoIssue));
        var (comp, swimmer) = await SeedBaseAsync(db);
        db.Add(Rec("34.08"));
        // Заплыв есть, но время другое — рекорд не подтверждается.
        db.Add(Swim(comp, swimmer, 43_080, new DateTime(2025, 7, 20)));
        await db.SaveChangesAsync();

        var result = await new RecordQualityService(db).VerifyAllAsync();

        Assert.Equal(1, result.NotFound);
        Assert.False(Assert.Single(db.RecordVerifications).Found);
        // Главное: сверка НЕ заводит претензию. Ненайденное ≠ ошибка источника.
        Assert.Empty(db.RecordIssues);
    }

    [Fact]
    public async Task Verify_MatchesAcrossAxis_NotJustTime()
    {
        using var db = CreateDb(nameof(Verify_MatchesAcrossAxis_NotJustTime));
        var (comp, swimmer) = await SeedBaseAsync(db, poolType: "25m");
        // Рекорд длинной воды, а заплыв с тем же временем — из 25-метрового бассейна.
        db.Add(Rec("34.08", poolType: "50m"));
        db.Add(Swim(comp, swimmer, 34_080, new DateTime(2025, 7, 20)));
        await db.SaveChangesAsync();

        var result = await new RecordQualityService(db).VerifyAllAsync();

        Assert.Equal(1, result.NotFound);
    }

    [Fact]
    public async Task Verify_FlagsWrongDate_WhenTimeMatchesOnAnotherDay()
    {
        using var db = CreateDb(nameof(Verify_FlagsWrongDate_WhenTimeMatchesOnAnotherDay));
        var (comp, swimmer) = await SeedBaseAsync(db);
        db.Add(Rec("34.08", date: "20/07/2025"));
        db.Add(Swim(comp, swimmer, 34_080, new DateTime(2024, 5, 1)));
        await db.SaveChangesAsync();

        var result = await new RecordQualityService(db).VerifyAllAsync();

        Assert.Equal(1, result.Found);
        Assert.Equal(1, result.FoundWrongDate);
        Assert.False(Assert.Single(db.RecordVerifications).DateMatched);
    }

    [Fact]
    public async Task Verify_IsIdempotent_AndTrimsDistanceSuffix()
    {
        using var db = CreateDb(nameof(Verify_IsIdempotent_AndTrimsDistanceSuffix));
        var (comp, swimmer) = await SeedBaseAsync(db);
        // «100m» в Records против «100» в Results — суффикс дистанции должен сниматься.
        db.Add(Rec("01:15.30", distance: "100m"));
        db.Add(Swim(comp, swimmer, 75_300, new DateTime(2025, 7, 20), distance: "100"));
        await db.SaveChangesAsync();

        var service = new RecordQualityService(db);
        await service.VerifyAllAsync();
        var second = await service.VerifyAllAsync();

        Assert.Equal(1, second.Found);
        Assert.Single(db.RecordVerifications);
    }

    [Fact]
    public async Task CreateIssue_Twice_UpdatesInsteadOfDuplicating()
    {
        using var db = CreateDb(nameof(CreateIssue_Twice_UpdatesInsteadOfDuplicating));
        var service = new RecordQualityService(db);
        var input = new Swimm.Application.Dtos.RecordIssueInputDto(
            "country", "ISR", "age", "10", "female", "50m", "backstroke", "50m",
            "34.08", RecordIssueReasons.Manual, "первая версия");

        await service.CreateIssueAsync(input, "vlad");
        var second = await service.CreateIssueAsync(input with { Note = "уточнил обоснование" }, "vlad");

        Assert.Single(db.RecordIssues);
        Assert.Equal("уточнил обоснование", second.Note);
    }

    [Fact]
    public async Task Issue_KnowsWhetherRecordIsStillCurrent()
    {
        using var db = CreateDb(nameof(Issue_KnowsWhetherRecordIsStillCurrent));
        db.Add(Rec("34.08"));
        await db.SaveChangesAsync();

        var service = new RecordQualityService(db);
        var live = await service.CreateIssueAsync(new Swimm.Application.Dtos.RecordIssueInputDto(
            "country", "ISR", "age", "10", "female", "50m", "backstroke", "50m",
            "34.08", null, null), "vlad");
        // Та же ось, но время, которого в справочнике уже нет — претензия про историю.
        var historic = await service.CreateIssueAsync(new Swimm.Application.Dtos.RecordIssueInputDto(
            "country", "ISR", "age", "10", "female", "50m", "backstroke", "50m",
            "43.08", null, null), "vlad");

        Assert.True(live.RecordStillCurrent);
        Assert.False(historic.RecordStillCurrent);
    }

    [Fact]
    public async Task UpdateIssue_ChangesStatus_AndListFiltersByIt()
    {
        using var db = CreateDb(nameof(UpdateIssue_ChangesStatus_AndListFiltersByIt));
        var service = new RecordQualityService(db);
        var created = await service.CreateIssueAsync(new Swimm.Application.Dtos.RecordIssueInputDto(
            "country", "ISR", "age", "10", "female", "50m", "backstroke", "50m",
            "34.08", null, "why"), "vlad");

        var updated = await service.UpdateIssueAsync(created.Id,
            new Swimm.Application.Dtos.RecordIssueUpdateDto(RecordIssueStatuses.Reported, null, null));

        Assert.Equal(RecordIssueStatuses.Reported, updated!.Status);
        Assert.Empty((await service.ListIssuesAsync(RecordIssueStatuses.Open, 1, 50)).Items);
        Assert.Single((await service.ListIssuesAsync(RecordIssueStatuses.Reported, 1, 50)).Items);
        Assert.Single((await service.ListIssuesAsync(null, 1, 50)).Items);

        Assert.True(await service.DeleteIssueAsync(created.Id));
        Assert.False(await service.DeleteIssueAsync(created.Id));
    }

    [Fact]
    public async Task Summary_CountsIssuesAndVerification()
    {
        using var db = CreateDb(nameof(Summary_CountsIssuesAndVerification));
        var (comp, swimmer) = await SeedBaseAsync(db);
        db.Add(Rec("34.08"));
        db.Add(Rec("40.00", ageKey: "11"));
        db.Add(Swim(comp, swimmer, 34_080, new DateTime(2025, 7, 20)));
        db.Add(new RecordIssue
        {
            RegionType = "country", RegionCode = "ISR", Category = "age", AgeKey = "10",
            Gender = "female", PoolType = "50m", Style = "backstroke", Distance = "50m",
            FlaggedTime = "34.08", Reason = RecordIssueReasons.Manual,
            Status = RecordIssueStatuses.Open, Note = "RQ-1",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.Add(new RecordIssue
        {
            RegionType = "country", RegionCode = "ISR", Category = "age", AgeKey = "12",
            Gender = "female", PoolType = "50m", Style = "backstroke", Distance = "50m",
            FlaggedTime = "32.23", Reason = RecordIssueReasons.Manual,
            Status = RecordIssueStatuses.Rejected, Note = "разобрались",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new RecordQualityService(db);
        await service.VerifyAllAsync();
        var summary = await service.GetSummaryAsync();

        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Found);
        Assert.Equal(1, summary.NotFound);
        Assert.Equal(0, summary.NotChecked);
        Assert.Equal(1, summary.IssuesOpen);   // закрытая претензия в счётчик open не идёт
        Assert.Equal(2, summary.IssuesTotal);
        Assert.Equal("34.08", Assert.Single(summary.Issues).FlaggedTime);
        Assert.NotNull(summary.LastCheckedAt);
    }
}

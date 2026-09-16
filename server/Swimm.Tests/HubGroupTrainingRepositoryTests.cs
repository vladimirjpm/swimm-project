using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="HubGroupTrainingRepository"/>: пол тренировочного повтора берётся из КАРТОЧКИ
/// пловца (И14, data-integrity.md), а не из строки — исходник Дельфина писал пол построчно и
/// у רוני (женская карточка) 4 строки пришли «male». Пол строки — запасной, когда карточка пуста.
/// </summary>
public class HubGroupTrainingRepositoryTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<int> SeedAsync(SwimmDbContext db, string? cardGender, string rowGender)
    {
        var owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id, IsPublic = true };
        var swimmer = new Swimmer { FirstName = "רוני", BirthYear = 2000, Gender = cardGender, Origin = "local" };
        var style = new Style { Name = "freestyle" };
        db.AddRange(group, swimmer, style);
        await db.SaveChangesAsync();
        var session = new TrainingSession
        {
            HubGroupId = group.Id, ExternalTrainingId = "20251109", Date = new DateTime(2025, 11, 9, 0, 0, 0, DateTimeKind.Utc), PoolType = "25m",
        };
        db.TrainingSessions.Add(session);
        await db.SaveChangesAsync();
        db.TrainingResults.Add(new TrainingResult
        {
            SessionId = session.Id, SwimmerId = swimmer.Id, StyleId = style.Id,
            Distance = "25", Gender = rowGender, TimeOriginal = "27.1", TimeMillisecond = 27100, SetNo = 2, OrderNo = 1,
        });
        await db.SaveChangesAsync();
        return group.Id;
    }

    [Theory]
    [InlineData("F", "male", "female")]      // локальная карточка «F» бьёт кривую строку
    [InlineData("female", "male", "female")] // isr-карточка полным словом
    [InlineData("M", "female", "male")]
    public async Task GetTrainingsAsync_GenderFromSwimmerCard_NotFromRow(string cardGender, string rowGender, string expected)
    {
        await using var db = CreateDb($"{nameof(GetTrainingsAsync_GenderFromSwimmerCard_NotFromRow)}_{cardGender}_{rowGender}");
        var groupId = await SeedAsync(db, cardGender, rowGender);

        var dto = await new HubGroupTrainingRepository(db).GetTrainingsAsync(groupId);

        Assert.Equal(expected, Assert.Single(dto.Results).EventStyleGender);
    }

    [Fact]
    public async Task GetTrainingsAsync_MapsFinsAndSessionNote()
    {
        await using var db = CreateDb(nameof(GetTrainingsAsync_MapsFinsAndSessionNote));
        var groupId = await SeedAsync(db, "M", "male");
        var row = await db.TrainingResults.Include(r => r.Session).SingleAsync();
        row.IsFins = true;
        row.Session!.Note = "סנפירים: פיטר";
        await db.SaveChangesAsync();

        var dto = await new HubGroupTrainingRepository(db).GetTrainingsAsync(groupId);

        var training = Assert.Single(dto.Results).Training;
        Assert.True(training.IsFins);
        Assert.Equal("סנפירים: פיטר", training.Note);
    }

    [Fact]
    public async Task GetTrainingsAsync_NewestSessionFirst()
    {
        await using var db = CreateDb(nameof(GetTrainingsAsync_NewestSessionFirst));
        var groupId = await SeedAsync(db, "M", "male");
        var newer = new TrainingSession
        {
            HubGroupId = groupId, ExternalTrainingId = "20260915",
            Date = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), PoolType = "25m",
        };
        db.TrainingSessions.Add(newer);
        await db.SaveChangesAsync();
        var old = await db.TrainingResults.SingleAsync();
        db.TrainingResults.Add(new TrainingResult
        {
            SessionId = newer.Id, SwimmerId = old.SwimmerId, StyleId = old.StyleId,
            Distance = "100", Gender = "male", TimeOriginal = "1:14", TimeMillisecond = 74000, SetNo = 1, OrderNo = 1,
        });
        await db.SaveChangesAsync();

        var dto = await new HubGroupTrainingRepository(db).GetTrainingsAsync(groupId);

        // Последняя тренировка сверху: 15.09.2026 раньше в списке, чем 09.11.2025.
        Assert.Equal(new[] { "15/09/2026", "09/11/2025" }, dto.Results.Select(r => r.Date).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    public async Task GetTrainingsAsync_CardGenderUnknown_FallsBackToRow(string? cardGender)
    {
        await using var db = CreateDb($"{nameof(GetTrainingsAsync_CardGenderUnknown_FallsBackToRow)}_{cardGender ?? "null"}");
        var groupId = await SeedAsync(db, cardGender, "female");

        var dto = await new HubGroupTrainingRepository(db).GetTrainingsAsync(groupId);

        Assert.Equal("female", Assert.Single(dto.Results).EventStyleGender);
    }
}

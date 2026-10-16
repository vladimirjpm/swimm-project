using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Длинная ссылка на фото профиля сохраняется в НАСТОЯЩЕМ Postgres. InMemory длину колонки не
/// проверяет — а прежний varchar(1000) ронял создание аккаунта (16.10.2026: Google-аккаунт ребёнка
/// под Family Link, ссылка на фото длиннее 1000 символов → ошибка 500 при первом входе).
/// Пропуск, если PG недоступен. Тест заводит своего пользователя и удаляет его в finally.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class AppUserAvatarPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    [Fact]
    public async Task AvatarUrl_LongerThan1000_IsStoredWhole()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var avatar = "https://lh3.googleusercontent.com/a/" + new string('x', 4000) + "=s96-c";
        var user = new AppUser
        {
            Email = $"utest-avatar-{Guid.NewGuid():N}@example.com",
            DisplayName = "utest avatar",
            AvatarUrl = avatar,
        };
        db.AppUsers.Add(user);
        try
        {
            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();
            var stored = await db.AppUsers.AsNoTracking()
                .Where(u => u.Id == user.Id).Select(u => u.AvatarUrl).SingleAsync();
            Assert.Equal(avatar, stored);
        }
        finally
        {
            await db.AppUsers.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// PG-специфичная проверка «Аномалий»: группировка по составному ключу с вычисляемым полем
/// (<c>RelayId != null</c>) — на InMemory она проходит всегда, а вот в SQL переводится не
/// автоматически. Тест ловит именно непереводимый запрос: на InMemory-провайдере такая
/// поломка невидима (урок RelayMemberUpsertPgTests).
///
/// Пропускается, если Postgres недоступен.
/// </summary>
public class DataQualityPgTests
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
    public async Task ResultAnomalies_RunAgainstRealDatabase()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var result = await new DataQualityService(db).GetResultAnomaliesAsync();

        // Утверждаем не числа (база живая, они меняются), а сам факт выполнения запроса
        // и согласованность выдачи с её же счётчиком.
        Assert.True(result.ExactDuplicates.Total >= 0);
        Assert.True(result.ExactDuplicates.Items.Count <= result.ExactDuplicates.Total);
        Assert.All(result.ExactDuplicates.Items, r => Assert.True(r.Copies > 1));
    }
}

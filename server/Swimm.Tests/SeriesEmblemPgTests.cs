using Microsoft.EntityFrameworkCore;
using Npgsql;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Значок серии против НАСТОЯЩЕГО Postgres (docs/plans/series-emblem-plan.md): то, чего InMemory
/// не видит, — bytea туда-обратно, CHECK-ограничения таблицы, SET NULL у серии при удалении
/// значка, порядок «новый значок → перевесить серию → удалить старый» в одном сохранении и
/// чтение под публичной ролью swimm_ro. Пропуск, если PG недоступен.
///
/// Тест ходит в рабочую локальную базу: заводит свою серию и в finally удаляет её и значки.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class SeriesEmblemPgTests
{
    private const string Owner =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";
    private const string ReadOnly =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm_ro;Password=swimm_ro_local_dev";

    private static SwimmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql(Owner, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .Options);

    private static SwimmReadDbContext NewReadContext() =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseNpgsql(ReadOnly)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

    private static SwimmDbContext? TryCreate()
    {
        var db = NewContext();
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private static async Task<T> With<T>(Func<SeriesAdminRepository, Task<T>> act)
    {
        await using var db = NewContext();
        return await act(new SeriesAdminRepository(db));
    }

    [Fact]
    public async Task EmblemLifecycle_OnPostgres()
    {
        await using var probe = TryCreate();
        if (probe == null) return; // PG недоступен — пропуск

        var tag = Guid.NewGuid().ToString("N")[..8];
        var first = SeriesEmblemImageTests.Png(512, 384, padding: 5_000);
        var second = SeriesEmblemImageTests.WebpLossy(256, 256);
        var emblemIds = new List<int>();
        int? seriesId = null;

        try
        {
            var created = await With(r => r.CreateSeriesAsync(new SeriesInputDto($"PG emblem {tag}", null, null)));
            Assert.True(created.Success, created.Error);
            seriesId = created.Id;

            // Значок необязателен: серия без него — обычное состояние.
            var bare = await With(r => r.GetByIdAsync(created.Id));
            Assert.Null(bare!.Emblem);
            Assert.Null((await With(r => r.GetAllAsync())).Single(s => s.Id == created.Id).Emblem);
            Assert.False((await With(r => r.DeleteEmblemAsync(created.Id))).Success);

            // Не картинка — отказ, в базе ничего.
            var junk = await With(r => r.SetEmblemAsync(created.Id, "<svg onload=alert(1)>"u8.ToArray()));
            Assert.False(junk.Success);
            Assert.Null((await With(r => r.GetByIdAsync(created.Id)))!.Emblem);

            // Загрузка: тип и размеры — из самих байтов.
            Assert.True((await With(r => r.SetEmblemAsync(created.Id, first))).Success);
            var one = (await With(r => r.GetByIdAsync(created.Id)))!.Emblem!;
            emblemIds.Add(one.Id);
            Assert.Equal(("image/png", 512, 384, first.Length), (one.ContentType, one.Width, one.Height, one.Bytes));
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(first)), one.Sha256);

            // Список серий (/Admin/Series) несёт те же сведения для миниатюры; у серии без значка — null.
            var listed = (await With(r => r.GetAllAsync())).Single(s => s.Id == created.Id);
            Assert.Equal(one, listed.Emblem);
            Assert.Equal(one.Url, listed.Emblem!.Url);

            // Публичная выдача — под ролью swimm_ro (грант из миграции), bytea без искажений.
            await using (var read = NewReadContext())
            {
                var reader = new SeriesEmblemReader(read);
                var file = await reader.GetAsync(one.Id, one.Sha256);
                Assert.Equal(first, file!.Data);
                Assert.Equal("image/png", file.ContentType);
                Assert.Null(await reader.GetAsync(one.Id, new string('0', 64)));
                Assert.Null(await reader.GetAsync(one.Id + 1_000_000, one.Sha256));
            }

            // Тот же файл повторно — та же строка и тот же адрес.
            Assert.True((await With(r => r.SetEmblemAsync(created.Id, first))).Success);
            Assert.Equal(one.Id, (await With(r => r.GetByIdAsync(created.Id)))!.Emblem!.Id);

            // Замена: новая строка с новым адресом, старая удалена — её адрес больше не отвечает.
            Assert.True((await With(r => r.SetEmblemAsync(created.Id, second))).Success);
            var two = (await With(r => r.GetByIdAsync(created.Id)))!.Emblem!;
            emblemIds.Add(two.Id);
            Assert.NotEqual(one.Id, two.Id);
            Assert.Equal(("image/webp", 256, 256), (two.ContentType, two.Width, two.Height));
            await using (var check = NewContext())
                Assert.False(await check.SeriesEmblems.AnyAsync(e => e.Id == one.Id));

            // Удаление значка серию не ломает.
            Assert.True((await With(r => r.DeleteEmblemAsync(created.Id))).Success);
            await using (var check = NewContext())
            {
                Assert.Null((await check.CompetitionSeries.AsNoTracking().SingleAsync(s => s.Id == created.Id)).EmblemId);
                Assert.False(await check.SeriesEmblems.AnyAsync(e => e.Id == two.Id));
            }

            // То же — если строку значка удалили мимо репозитория (синхронизация, ручной SQL): SET NULL.
            Assert.True((await With(r => r.SetEmblemAsync(created.Id, first))).Success);
            var three = (await With(r => r.GetByIdAsync(created.Id)))!.Emblem!;
            emblemIds.Add(three.Id);
            await using (var raw = NewContext())
                await raw.SeriesEmblems.Where(e => e.Id == three.Id).ExecuteDeleteAsync();
            var after = await With(r => r.GetByIdAsync(created.Id));
            Assert.NotNull(after);
            Assert.Null(after.Emblem);

            // Удаление серии уносит и её значок — сирот не остаётся.
            Assert.True((await With(r => r.SetEmblemAsync(created.Id, second))).Success);
            var four = (await With(r => r.GetByIdAsync(created.Id)))!.Emblem!;
            emblemIds.Add(four.Id);
            Assert.True((await With(r => r.DeleteSeriesAsync(created.Id))).Success);
            await using (var check = NewContext())
            {
                Assert.False(await check.CompetitionSeries.AnyAsync(s => s.Id == created.Id));
                Assert.False(await check.SeriesEmblems.AnyAsync(e => e.Id == four.Id));
            }
            seriesId = null;
        }
        finally
        {
            await using var cleanup = NewContext();
            if (seriesId is int sid)
                await cleanup.CompetitionSeries.Where(s => s.Id == sid).ExecuteDeleteAsync();
            await cleanup.SeriesEmblems.Where(e => emblemIds.Contains(e.Id)).ExecuteDeleteAsync();
        }
    }

    public static TheoryData<string, string, int, int, int, string> BadRows() => new()
    {
        // что не так,            тип,            байт, ширина, высота, sha256
        { "тип не из списка",     "image/svg+xml", 10,   512,    512,    new string('a', 64) },
        { "пустые данные",        "image/png",     0,    512,    512,    new string('a', 64) },
        { "файл больше лимита",   "image/png",     307_201, 512, 512,    new string('a', 64) },
        { "сторона больше лимита", "image/png",    10,   1025,   512,    new string('a', 64) },
        { "нулевая сторона",      "image/png",     10,   512,    0,      new string('a', 64) },
        { "хэш не hex",           "image/png",     10,   512,    512,    new string('Z', 64) },
        { "хэш короткий",         "image/png",     10,   512,    512,    "abc" },
    };

    /// <summary>
    /// CHECK-ограничения держат таблицу и при записи мимо репозитория: строка, которую публичный
    /// адрес не должен отдавать, в неё не попадает.
    /// </summary>
    [Theory]
    [MemberData(nameof(BadRows))]
    public async Task CheckConstraints_RejectRowsWrittenAroundTheRepository(
        string what, string contentType, int bytes, int width, int height, string sha256)
    {
        await using var probe = TryCreate();
        if (probe == null) return;

        await using var db = NewContext();
        db.SeriesEmblems.Add(new SeriesEmblem
        {
            Data = new byte[bytes], ContentType = contentType, Width = width, Height = height,
            Sha256 = sha256, UploadedAt = DateTime.UtcNow
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.True(pg.SqlState == PostgresErrorCodes.CheckViolation, $"{what}: {pg.MessageText}");
    }
}

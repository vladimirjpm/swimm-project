using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Галочка «пометить заплыв сомнительным» прямо в превью импорта.
///
/// Зачем: между импортом и ручным прогоном «Проверить качество» ошибочный заплыв успевает
/// побыть национальным рекордом на витрине, а автоматика ловит такое не всегда. Живой
/// случай (comp 16769, 06.02.2026): 200 баттерфляй за 02:04.41 у пловчихи, у которой в том
/// же протоколе 200 комплекс 2:47.91 — быстрее рекорда взрослых на 5 с. Правило
/// `time_vs_distance` молчит (мировой рекорд 2:01.81 быстрее), `personal_outlier` тоже —
/// ему нужно 3 её старта в окне ±120 дней, а их два.
///
/// Адрес заплыва — порядковый номер строки в разобранном файле (Id в БД ещё нет).
/// </summary>
public class ImportSuspectFlagFromPreviewTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, System.TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static object Row(string lastName, string style, string time) => new
    {
        country = "ISR",
        competition = "ליגה מס 4",
        date = "06/02/2026",
        event_style_name = style,
        event_style_len = "200",
        event_style_gender = "female",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 4,
        last_name = lastName,
        first_name = "אליענה",
        birth_year = 2011,
        club = "מכבי אשדוד",
        time
    };

    private static string Json(params object[] rows) => JsonSerializer.Serialize(rows);

    [Fact]
    public void ApplySuspectFlags_MarksOnlyChosenRow_ByIndex()
    {
        var json = Json(Row("א", "butterfly", "02:04.41"), Row("ב", "freestyle", "02:30.00"));

        var patched = ImportPayloadSuspectFlags.Apply(json,
            [new ImportSuspectFlag(0, "быстрее рекорда")]);

        using var doc = JsonDocument.Parse(patched);
        var items = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal("быстрее рекорда", items[0].GetProperty("suspect_note").GetString());
        Assert.False(items[1].TryGetProperty("suspect_note", out _));
    }

    [Fact]
    public void ApplySuspectFlags_SupportsWrappedShape_AndSurvivesGarbage()
    {
        var wrapped = JsonSerializer.Serialize(new { results = new[] { Row("א", "butterfly", "02:04.41") } });

        var patched = ImportPayloadSuspectFlags.Apply(wrapped,
            [new ImportSuspectFlag(0, null)]);

        using var doc = JsonDocument.Parse(patched);
        var note = doc.RootElement.GetProperty("results")[0].GetProperty("suspect_note").GetString();
        Assert.False(string.IsNullOrWhiteSpace(note));

        // Индекс за границей и нечитаемый JSON не имеют права сорвать импорт.
        Assert.Equal(wrapped, ImportPayloadSuspectFlags.Apply(wrapped,
            [new ImportSuspectFlag(99, "x")]));
        Assert.Equal("не json", ImportPayloadSuspectFlags.Apply("не json",
            [new ImportSuspectFlag(0, "x")]));
    }

    [Fact]
    public async Task FlaggedRow_LandsAlreadyMarkedManual_AndCounted()
    {
        await using var db = CreateDb(nameof(FlaggedRow_LandsAlreadyMarkedManual_AndCounted));

        var json = ImportPayloadSuspectFlags.Apply(
            Json(Row("זלוטניקוב", "butterfly", "02:04.41"), Row("כהן", "freestyle", "02:30.00")),
            [new ImportSuspectFlag(0, "Помечено при импорте: 02:04.41 быстрее Open record 02:09.69")]);

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), "meet.json");

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, result.SuspectFlagged);

        var flagged = await db.Results.SingleAsync(r => r.SuspectReason != null);
        Assert.True(flagged.SuspectIsManual);          // ручная: переживёт скан и переимпорт
        Assert.Equal(SuspectReasons.Manual, flagged.SuspectReason);
        Assert.Contains("02:09.69", flagged.SuspectNote!);

        // Соседняя строка чистая — помечается ровно выбранный заплыв.
        Assert.Equal(1, await db.Results.CountAsync(r => r.SuspectReason == null));
    }

    [Fact]
    public async Task UnflaggedImport_LeavesEverythingClean()
    {
        await using var db = CreateDb(nameof(UnflaggedImport_LeavesEverythingClean));

        var result = await new JsonImportService(db, new NullCache()).ImportAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(Json(Row("זלוטניקוב", "butterfly", "02:04.41")))),
            "meet.json");

        Assert.Equal(0, result.SuspectFlagged);
        Assert.All(await db.Results.ToListAsync(), r =>
        {
            Assert.Null(r.SuspectReason);
            Assert.False(r.SuspectIsManual);
        });
    }
}

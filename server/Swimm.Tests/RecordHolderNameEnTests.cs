using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using Record = Swimm.Domain.Entities.Record;   // Xunit.Record — тоже Record, и без алиаса тут неоднозначность

namespace Swimm.Tests;

/// <summary>
/// `Records.HolderNameEn` — английское имя держателя для международных экранов
/// (решение Влада 16.09.2026).
///
/// Главное, что здесь проверяется, — **не «поле заполняется», а что оно СБРАСЫВАЕТСЯ, когда
/// рекорд сменился**. Без этого справочник подписал бы новый рекорд именем предыдущего
/// держателя, и заметить такое можно было бы только глазами: время и дата новые, имя на
/// иврите новое, а английское — старое.
///
/// Порядок прогона (И-13) устроен так, что поверх латиницы World Aquatics ложится иврит
/// федерации, поэтому оба случая — «тот же рекорд» и «уже другой» — встречаются на каждом
/// `--records-refresh`.
/// </summary>
public class RecordHolderNameEnTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ParsedRecordDto Parsed(string time, string holder) => new(
        RegionType: "country", RegionCode: "ISR", Category: "open", AgeKey: "",
        Gender: "male", PoolType: "50m", Style: "freestyle", Distance: "100m",
        Time: time, HolderName: holder, Club: null, HolderCountry: "ISR", RecordDate: "01/01/2020");

    private static Record Existing(string time, string holder, string? holderEn) => new()
    {
        RegionType = "country",
        RegionCode = "ISR",
        Category = "open",
        AgeKey = "",
        Gender = "male",
        PoolType = "50m",
        Style = "freestyle",
        Distance = "100m",
        Time = time,
        HolderName = holder,
        HolderNameEn = holderEn,
    };

    /// <summary>
    /// Прогоняет одну строку через настоящий Apply и возвращает, что легло в базу.
    /// Идём через сервис, а не через голое правило: важно именно то, что попадёт в БД.
    /// </summary>
    private static async Task<Record> ApplyAsync(string name, Record? seed, ParsedRecordDto incoming)
    {
        await using var db = CreateDb(name);
        if (seed != null) { db.Records.Add(seed); await db.SaveChangesAsync(); }

        var service = new RecordDiffService(db, new MemoryCache(new MemoryCacheOptions()));

        var diff = await service.BuildDiffAsync("worldrecords", [incoming]);
        await service.ApplyAsync(new RecordDiffApplyRequest(diff.DiffId, true, true));

        return await db.Records.SingleAsync();
    }

    /// <summary>Источник отдал латиницу — она и становится английским именем строки.</summary>
    [Fact]
    public async Task Apply_LatinHolder_FillsHolderNameEn()
    {
        var row = await ApplyAsync(nameof(Apply_LatinHolder_FillsHolderNameEn),
            seed: null, Parsed("48.18", "Tomer Frankel"));

        Assert.Equal("Tomer Frankel", row.HolderNameEn);
        Assert.Equal("Tomer Frankel", row.HolderName);
    }

    /// <summary>
    /// Иврит поверх той же строки с ТЕМ ЖЕ временем — рекорд не менялся, сменился язык
    /// записи. Английское имя обязано пережить это: ровно так и идёт каждый прогон, где
    /// федерация ложится поверх World Aquatics.
    /// </summary>
    [Fact]
    public async Task Apply_HebrewOverSameTime_KeepsHolderNameEn()
    {
        var row = await ApplyAsync(nameof(Apply_HebrewOverSameTime_KeepsHolderNameEn),
            Existing("48.18", "Tomer Frankel", "Tomer Frankel"),
            Parsed("48.18", "תומר פרנקל"));

        Assert.Equal("תומר פרנקל", row.HolderName);
        Assert.Equal("Tomer Frankel", row.HolderNameEn);
    }

    /// <summary>
    /// **Главный тест.** Время изменилось — это ДРУГОЙ рекорд, и английское имя прежнего
    /// держателя к нему не относится. Оставить его значило бы подписать новый рекорд чужим
    /// человеком.
    /// </summary>
    [Fact]
    public async Task Apply_NewTime_ClearsStaleHolderNameEn()
    {
        var row = await ApplyAsync(nameof(Apply_NewTime_ClearsStaleHolderNameEn),
            Existing("48.18", "Tomer Frankel", "Tomer Frankel"),
            Parsed("47.90", "שם אחר"));

        Assert.Equal("47.90", row.Time);
        Assert.Null(row.HolderNameEn);
    }

    /// <summary>
    /// Новый рекорд, и источник сам отдал латиницу — английское имя берётся НОВОЕ, а не
    /// остаётся прежним.
    /// </summary>
    [Fact]
    public async Task Apply_NewTimeWithLatinHolder_TakesTheNewName()
    {
        var row = await ApplyAsync(nameof(Apply_NewTimeWithLatinHolder_TakesTheNewName),
            Existing("48.18", "Tomer Frankel", "Tomer Frankel"),
            Parsed("47.90", "Denis Loktev"));

        Assert.Equal("Denis Loktev", row.HolderNameEn);
    }

    /// <summary>Новая строка с ивритским держателем английского имени не выдумывает.</summary>
    [Fact]
    public async Task Apply_NewHebrewRow_LeavesHolderNameEnNull()
    {
        var row = await ApplyAsync(nameof(Apply_NewHebrewRow_LeavesHolderNameEnNull),
            seed: null, Parsed("48.18", "תומר פרנקל"));

        Assert.Null(row.HolderNameEn);
    }
}

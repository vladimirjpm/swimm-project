using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Parsing.RecordSources;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// worldrecords на JSON-выдаче World Aquatics (16.11.2026, вместо XLSX-отчёта, который отдавал
/// 504 из кэша CloudFront). Главное — строки обязаны выйти В ТОЙ ЖЕ ФОРМЕ, что из XLSX: иначе
/// каждая запись справочника в диффе «изменится». Сверено вживую: на 181 ключе мировой оси
/// расхождения только в реально побитых рекордах.
/// </summary>
public class WorldAquaticsRecordsJsonTests
{
    private static Stream Json(string records, int? total = null) =>
        new MemoryStream(Encoding.UTF8.GetBytes(
            $$"""{"records":[{{records}}],"totalRowCount":{{total?.ToString() ?? "TOTAL"}}}"""
                .Replace("TOTAL", records.Length == 0 ? "0" : (records.Split("\"pool\"").Length - 1).ToString())));

    private const string Douglass = """
        {"pool":1,"disciplineGender":1,"timeFormatted":"22.75","nationalityCode":"USA",
         "fullName":"DOUGLASS Kate","preferredFirstName":"Kate","preferredLastName":"DOUGLASS",
         "disciplineDistance":50,"disciplineGroup":"Freestyle","date":"2026-10-29T00:00:00"}
        """;

    private const string WeberGale = """
        {"pool":0,"disciplineGender":0,"timeFormatted":"47.98","nationalityCode":"USA",
         "fullName":"WEBER-GALE Garrett","disciplineDistance":100,"disciplineGroup":"Freestyle",
         "date":"2008-08-11T00:00:00"}
        """;

    private const string DutchRelay = """
        {"pool":1,"disciplineGender":1,"timeFormatted":"01:32.50","nationalityCode":"NED",
         "fullName":"Netherlands","disciplineDistance":50,"disciplineGroup":"Freestyle Relay",
         "date":"2020-12-12T00:00:00","isRelay":true}
        """;

    private const string MixedRelay = """
        {"pool":0,"disciplineGender":2,"timeFormatted":"03:18.48","nationalityCode":"USA",
         "fullName":"United States of America","disciplineDistance":100,"disciplineGroup":"Freestyle Relay",
         "date":"2025-08-02T00:00:00"}
        """;

    private static readonly WaJsonAxes World = new(
        "world", "", "open", _ => "",
        row => Swimm.Parsing.Parsers.WorldRecords.WorldRecordsParser.FormatAthleteName(
            WorldAquaticsRecordsJson.Str(row, "fullName")),
        "worldrecords");

    private static readonly WaJsonAxes National = World with { RegionType = "country", RegionCode = null };

    [Fact]
    public void Individual_record_has_xlsx_shape()
    {
        var r = Assert.Single(WorldAquaticsRecordsJson.Parse(Json(Douglass), World).Records);

        Assert.Equal(("world", "", "open", "", "female", "25m"), (r.RegionType, r.RegionCode, r.Category, r.AgeKey, r.Gender, r.PoolType));
        Assert.Equal(("freestyle", "50m", "22.75"), (r.Style, r.Distance, r.Time));
        Assert.Equal("Kate Douglass", r.HolderName);
        Assert.Equal("USA", r.HolderCountry);
        Assert.Equal("29/10/2026", r.RecordDate);
        Assert.False(r.HolderIsTeam);
    }

    [Fact]
    public void Hyphenated_surname_is_formatted_like_the_xlsx_parser()
    {
        // XLSX-парсер пишет «Weber-gale» (вторая часть строчными) — так и лежит в базе.
        var r = Assert.Single(WorldAquaticsRecordsJson.Parse(Json(WeberGale), World).Records);
        Assert.Equal("Garrett Weber-gale", r.HolderName);
    }

    [Fact]
    public void Relay_holder_is_the_team_and_marked_so()
    {
        var rows = WorldAquaticsRecordsJson.Parse(Json(DutchRelay + "," + MixedRelay), World).Records;

        var dutch = rows.Single(r => r.Gender == "female");
        Assert.Equal(("freestyle", "4X50m", "Netherlands"), (dutch.Style, dutch.Distance, dutch.HolderName));
        Assert.True(dutch.HolderIsTeam);

        var mixed = rows.Single(r => r.Gender == "mixed");
        Assert.Equal(("50m", "4X100m"), (mixed.PoolType, mixed.Distance));
        Assert.True(mixed.HolderIsTeam);
    }

    [Fact]
    public void National_record_takes_region_from_nationality()
    {
        var r = Assert.Single(WorldAquaticsRecordsJson.Parse(Json(Douglass), National).Records);
        Assert.Equal(("country", "USA"), (r.RegionType, r.RegionCode));
    }

    [Fact]
    public void Paged_response_is_refused_not_half_written()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorldAquaticsRecordsJson.Parse(Json(Douglass, total: 51), World));
        Assert.Contains("постраничной", ex.Message);
    }

    // ── Дифф: держатель-команда не делает эстафету «изменённой» ─────────────────────────────

    private static SwimmDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<RecordDiffResult> DiffAgainstRosterAsync(string name, ParsedRecordDto parsed)
    {
        await using var db = Db(name);
        db.Records.Add(new Record
        {
            RegionType = "world", RegionCode = "", Category = "open", AgeKey = "",
            Gender = "female", PoolType = "25m", Style = "freestyle", Distance = "4X50m",
            Time = "01:32.50", HolderName = "Femke Heemskerk, Kim Busch, Maaike de Waard, Ranomi Kromowidjojo",
            HolderCountry = "NED", RecordDate = "12/12/2020", UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return await new RecordDiffService(db, new MemoryCache(new MemoryCacheOptions()))
            .BuildDiffAsync("worldrecords", [parsed]);
    }

    private static ParsedRecordDto Relay(string time, string date) =>
        new("world", "", "open", "", "female", "25m", "freestyle", "4X50m", time, "Netherlands", null, "NED", date,
            HolderIsTeam: true);

    [Fact]
    public async Task Same_relay_from_json_keeps_the_stored_roster()
    {
        var diff = await DiffAgainstRosterAsync(nameof(Same_relay_from_json_keeps_the_stored_roster),
            Relay("01:32.50", "12/12/2020"));

        Assert.Equal(0, diff.ChangedCount);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public async Task Broken_relay_record_is_a_change_even_with_team_holder()
    {
        var diff = await DiffAgainstRosterAsync(nameof(Broken_relay_record_is_a_change_even_with_team_holder),
            Relay("01:31.90", "10/12/2026"));

        var entry = Assert.Single(diff.Changed);
        Assert.Equal("Netherlands", entry.NewHolderName);
    }
}

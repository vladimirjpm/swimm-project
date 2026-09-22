using Xunit;
using Swimm.Domain.Entities;

namespace Swimm.Tests;

/// <summary>
/// Э2 плана records-relays-plan (22.09.2026): пол <c>mixed</c> в справочнике — только у
/// эстафеты; <c>none</c> («пол неизвестен» в Results) в справочник не пускаем.
/// </summary>
public class RecordGenderTests
{
    [Theory]
    [InlineData("male", "100m")]
    [InlineData("female", "4X100m")]
    [InlineData("mixed", "4X100m")]
    [InlineData("mixed", "4x50m")]
    [InlineData("mixed", "4X50")]
    public void Valid(string gender, string distance) =>
        Assert.Null(Swimm.Domain.Entities.Record.ValidateGender(gender, distance));

    [Theory]
    [InlineData("mixed", "100m")]   // смешанной личной дистанции не бывает
    [InlineData("mixed", "400m")]   // «4» в начале — ещё не эстафета
    [InlineData("none", "4X100m")]  // «неизвестен» — не пол дисциплины
    [InlineData("mix", "4X100m")]   // форма WorldRecordsParser — не наша
    [InlineData("", "100m")]
    [InlineData(null, "100m")]
    public void Invalid(string? gender, string distance) =>
        Assert.NotNull(Swimm.Domain.Entities.Record.ValidateGender(gender, distance));

    /// <summary>Э3: «mix» парсеров → «mixed» справочника, и только у эстафеты.</summary>
    [Theory]
    [InlineData("mix", "4X100m", "mixed")]
    [InlineData("male", "100m", "male")]
    [InlineData("mix", "100m", null)]
    [InlineData("none", "4X50m", null)]
    public void ParserGender_MappedToRecordGender(string parser, string distance, string? expected) =>
        Assert.Equal(expected, Swimm.Parsing.RecordSources.WorldAquaticsSource.RecordGender(parser, distance));
}

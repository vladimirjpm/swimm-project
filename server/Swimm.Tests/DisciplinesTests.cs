using Swimm.Application.Constants;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Догадка о виде спорта по названию. Признак ХРАНИТСЯ (Competition.Discipline /
/// DiscoveredCompetition.Discipline), а эта эвристика лишь проставляет его при обнаружении
/// и импорте — поэтому её поведение и закреплено тестом: промах на реальных названиях
/// потом придётся править руками в админке.
/// </summary>
public class DisciplinesTests
{
    [Theory]
    // Обе ивритские формы: с алеф-вав и без (в источнике встречаются обе).
    [InlineData("אליפות ישראל \"ארנה\"  חורף 2025 שחייה אומנותית")]
    [InlineData("אליפות ישראל arena חורף בשחייה אמנותית")]
    [InlineData("תחרות ליגה אמנותית בית שמש")]
    // Английские названия двуязычных выгрузок.
    [InlineData("SYNCHROPASSION FROM 8 TO 80 אילת סינכרו")]
    [InlineData("Israel Artistic Swimming Championship")]
    public void ArtisticIsRecognised(string name)
        => Assert.Equal(Disciplines.Artistic, Disciplines.GuessFromName(name));

    [Theory]
    [InlineData("אליפות ישראל \"ארנה\" לגילאי 8-11 חורף 2026")]
    [InlineData("ליגה מס 3 מכבי רחובות וייסגל")]
    [InlineData("Maccabiah 2026 Masters")]
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElseIsPlainSwimming(string? name)
        => Assert.Equal(Disciplines.Swimming, Disciplines.GuessFromName(name));
}

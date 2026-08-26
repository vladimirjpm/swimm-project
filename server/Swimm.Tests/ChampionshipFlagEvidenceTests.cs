using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Откуда берётся галка «Чемпионат Израиля» при затягивании.
///
/// Имя — сильная улика, регламент — слабая. Обычный турнир сплошь и рядом упоминает
/// чемпионат: «מילניום 2025» — как цель подготовки, «ליגה מס 1 צעירים» — как образец
/// возрастных групп. Оба уехали в БД с ложной галкой (docs/data-integrity.md, И-14).
/// Формулировки отсекает RegulationAnalyzer, но структурная страховка — подтверждение
/// именем: регламент считается, только если в названии есть слово «чемпионат».
/// </summary>
public class ChampionshipFlagEvidenceTests
{
    [Theory]
    [InlineData("אליפות ישראל ארנה נוער ובוגרים חורף 2025")]
    [InlineData("Israel Championship Winter 2025")]
    public void NameAlone_IsEnough(string name)
    {
        Assert.True(CompetitionAdminRepository.IsChampionship(name));
        // …и регламент такому имени уже не нужен.
        Assert.True(CompetitionAdminRepository.IsChampionship(name, regulationSaysChampionship: false));
    }

    [Theory]
    [InlineData("מילניום")]                      // упоминает чемпионат как цель подготовки
    [InlineData("ליגה מס 1 צעירים- מכבי אושן חדרה")] // ссылается на возрастные группы чемпионата
    public void RegulationAlone_IsNotEnough(string name)
    {
        Assert.False(CompetitionAdminRepository.IsChampionship(name, regulationSaysChampionship: true));
    }

    /// <summary>
    /// Возрастной чемпионат страны: в названии «ישראל» потеряно, но слово «אליפות» есть —
    /// вот тут регламент и работает как вторая улика.
    /// </summary>
    [Fact]
    public void NameWithChampionshipWord_PlusRegulation_IsEnough()
    {
        const string name = "אליפות חורף ארנה גילאי 11-10";

        Assert.False(CompetitionAdminRepository.IsChampionship(name));
        Assert.False(CompetitionAdminRepository.IsChampionship(name, regulationSaysChampionship: false));
        Assert.True(CompetitionAdminRepository.IsChampionship(name, regulationSaysChampionship: true));
    }

    [Fact]
    public void EmptyName_IsNeverAChampionship()
    {
        Assert.False(CompetitionAdminRepository.IsChampionship(null, regulationSaysChampionship: true));
        Assert.False(CompetitionAdminRepository.IsChampionship("", regulationSaysChampionship: true));
    }
}

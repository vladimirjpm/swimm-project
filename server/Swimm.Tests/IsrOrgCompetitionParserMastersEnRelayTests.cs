using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Мастерские эстафеты в АНГЛИЙСКОЙ версии протокола loglig (10.10.2026). Склейка пары HE+EN
/// сопоставляет события двух языков по номеру, поэтому английский разбор обязан давать ту же
/// последовательность событий, что ивритский. Не давал в трёх местах — и пары мастерс-зимы 2026
/// (#64, loglig 13805) и Маккабиады-мастерс (#1482, loglig 15155) не склеивались, EN-имена из
/// протокола не приходили:
///  • смешанная эстафета «4X50m Medley Relay Mix» не считалась заголовком — её выбрасывали,
///    «зеркаля» иврит, который «מיקס» давно узнаёт;
///  • смена полосы суммы возрастов внутри эстафеты («מאסטרס שליחות 160-199») была только в HE —
///    EN складывал все полосы в одно событие;
///  • полоса ЛИЧНОГО возраста над эстафетой («מאסטרס נ 90-94») давала в EN пол «female», в HE — «none».
/// Фикстуры — вырезанные страницы настоящих английских протоколов.
/// </summary>
public class IsrOrgCompetitionParserMastersEnRelayTests
{
    private static IsrOrgCompetitionResult[] Parse(string file)
    {
        using var fs = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", file));
        return IsrOrgCompetitionParser.ParseCompetitions(fs, "EN").ToArray();
    }

    [Fact]
    public void MixedMedleyRelay_SplitIntoSumOfAgesBands_LikeHebrew()
    {
        // Мастерс-зима 2026, стр. 32–37: смешанная 4X50 комплексом, затем 50 вольным.
        var events = Parse("MastersWinter2026_13805_relay_mix_bands_EN.pdf");
        var relay = events.Where(e => e.EventStyleLen == "4X50").ToArray();

        // Полосы и число команд — как в ивритской версии того же протокола.
        Assert.Equal(
            [("120-159", 4), ("100-119", 2), ("160-199", 5), ("200-239", 7), ("240-279", 4), ("280-319", 1)],
            relay.Select(e => (e.AgeGroup, e.Results.Count)));
        Assert.All(relay, e => Assert.Equal(("individual_medley", "none"), (e.EventStyleName, e.EventStyleGender)));

        // Личные заплывы после эстафеты — своими событиями, в эстафету не утекли.
        Assert.Equal("50", events[relay.Length].EventStyleLen);
        Assert.Equal("00:23.79", events[relay.Length].Results[0].Time);
    }

    [Fact]
    public void PersonalAgeBandOverRelay_GenderIgnored()
    {
        // Маккабиада-мастерс 2026, стр. 1: над первой командой — «מאסטרס נ 90-94» (женская полоса
        // личного возраста), а команда мужская. Пол такой полосы не берём, как в HE.
        var first = Parse("MaccabiahMasters2026_15155_relay_personal_band_EN.pdf")[0];

        Assert.Equal(("4X50", "90-94", "none"), (first.EventStyleLen, first.AgeGroup, first.EventStyleGender));
        Assert.Equal("01:52.53", first.Results[0].Time);
    }
}

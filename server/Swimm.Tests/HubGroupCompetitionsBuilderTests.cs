using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Список стартов группы — чип «≡ Results» таба Results (решение Влада 28.09.2026:
/// у старта сколько пловцов группы плыло, сколько рекордов и медалей).
///
/// Держат: строка = турнир (дни многодневки сложены), свежие сверху; медали — единое правило
/// продукта; эстафета — всеми ногами из ростера, а не владельцем строки; рекорды — по дням
/// турнира.
/// </summary>
public class HubGroupCompetitionsBuilderTests
{
    private const int A = 1, B = 2, C = 3, Outsider = 99;
    private static readonly IReadOnlySet<int> Roster = new HashSet<int> { A, B, C };
    private static readonly Dictionary<int, List<int>> NoRelays = [];

    private static long _id;

    private static HubGroupCompetitionSwim Swim(
        int competitionId, string date, int swimmer, int? position = null,
        int? eventId = null, string? name = null, bool isAward = true, string? heatType = null,
        string? round = null, bool timeFail = false, int? relayId = null) =>
        new()
        {
            Id = ++_id,
            CompetitionId = competitionId,
            EventId = eventId,
            Name = name ?? $"Meet {competitionId}",
            CompetitionDate = DateTime.ParseExact(date, "dd/MM/yyyy", null),
            SwimmerId = swimmer,
            RelayId = relayId,
            Position = position,
            TimeFail = timeFail,
            HeatType = heatType,
            Round = round,
            IsAward = isAward,
        };

    private static HubGroupBestDto Best(int competitionId) => new() { CompetitionId = competitionId };

    [Fact]
    public void MultiDayEvent_IsOneRow_AndRowsGoNewestFirst()
    {
        var swims = new[]
        {
            Swim(10, "01/06/2026", A),                                        // однодневный, раньше
            Swim(20, "28/07/2026", A, eventId: 5, name: "Summer Champs"),     // день 1
            Swim(21, "30/07/2026", B, eventId: 5, name: "Summer Champs"),     // день 3
            Swim(21, "30/07/2026", C, eventId: 5, name: "Summer Champs"),
        };

        var list = HubGroupCompetitionsBuilder.Build(swims, Roster, NoRelays, []);

        Assert.Equal(2, list.Count);
        var champs = list[0];
        Assert.Equal(5, champs.EventId);
        Assert.Equal(21, champs.CompetitionId);          // адрес — последний день
        Assert.Equal("Summer Champs", champs.Name);
        Assert.Equal(("28/07/2026", "30/07/2026"), (champs.DateFrom, champs.DateTo));
        Assert.Equal((3, 3), (champs.Swimmers, champs.Swims));

        Assert.Null(list[1].EventId);
        Assert.Equal(10, list[1].CompetitionId);
        Assert.Equal(list[1].DateFrom, list[1].DateTo);
    }

    [Fact]
    public void Medals_FollowTheProductRule()
    {
        var swims = new[]
        {
            Swim(10, "10/01/2026", A, 1),                                     // золото
            Swim(10, "10/01/2026", B, 2),                                     // серебро
            Swim(10, "10/01/2026", C, 3),                                     // бронза
            Swim(10, "10/01/2026", A, 1, heatType: HeatTypes.Prelim),         // место в предварительном (Р34)
            Swim(10, "10/01/2026", B, 1, round: ResultRounds.FinalOpen),      // «כללי» (Р43)
            Swim(10, "10/01/2026", C, 2, timeFail: true),                     // снят
            Swim(10, "10/01/2026", A, 4),                                     // мимо пьедестала
            Swim(11, "12/12/2025", A, 1, isAward: false),                     // лига без наград
        };

        var list = HubGroupCompetitionsBuilder.Build(swims, Roster, NoRelays, []);

        var champs = list.Single(c => c.CompetitionId == 10);
        Assert.Equal((1, 1, 1), (champs.Golds, champs.Silvers, champs.Bronzes));
        Assert.Equal(7, champs.Swims);                   // заплывы считаются все, не только медали
        var league = list.Single(c => c.CompetitionId == 11);
        Assert.Equal((0, 0, 0), (league.Golds, league.Silvers, league.Bronzes));
    }

    [Fact]
    public void Relay_CountsItsRosterLegs_NotTheRowOwner()
    {
        // Эстафету 7 плыли A и C, строка — на чужом владельце-ноге. Эстафета 8 — старая, без
        // членств, строка на B: остаётся владелец. Эстафета 9 — чужая целиком.
        var relays = new Dictionary<int, List<int>> { [7] = [A, C] };
        var swims = new[]
        {
            Swim(10, "10/01/2026", Outsider, 1, relayId: 7),
            Swim(10, "10/01/2026", B, 2, relayId: 8),
            Swim(10, "10/01/2026", Outsider, 3, relayId: 9),
        };

        var row = HubGroupCompetitionsBuilder.Build(swims, Roster, relays, []).Single();

        Assert.Equal(3, row.Swimmers);                   // A и C по членству, B владельцем
        Assert.Equal((1, 1, 1), (row.Golds, row.Silvers, row.Bronzes));
    }

    [Fact]
    public void Swimmers_AreDistinctAcrossTheWholeEvent()
    {
        var swims = new[]
        {
            Swim(20, "28/07/2026", A, eventId: 5),
            Swim(20, "28/07/2026", A, eventId: 5),
            Swim(21, "29/07/2026", A, eventId: 5),
            Swim(21, "29/07/2026", B, eventId: 5),
        };

        var row = HubGroupCompetitionsBuilder.Build(swims, Roster, NoRelays, []).Single();

        Assert.Equal((2, 4), (row.Swimmers, row.Swims));
    }

    [Fact]
    public void Records_AreSummedOverAllDaysOfTheEvent()
    {
        var swims = new[]
        {
            Swim(20, "28/07/2026", A, eventId: 5),
            Swim(21, "29/07/2026", B, eventId: 5),
            Swim(10, "01/06/2026", C),
        };
        // Два рекорда в день 1, один в день 2, один — на однодневном, один — на старте, где
        // ростер не плыл (такого не бывает, но строку он не создаёт).
        var bests = new[] { Best(20), Best(20), Best(21), Best(10), Best(77) };

        var list = HubGroupCompetitionsBuilder.Build(swims, Roster, NoRelays, bests);

        Assert.Equal(new[] { 3, 1 }, list.Select(c => c.Records));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void NoSwims_NoRows()
    {
        Assert.Empty(HubGroupCompetitionsBuilder.Build([], Roster, NoRelays, [Best(1)]));
    }
}

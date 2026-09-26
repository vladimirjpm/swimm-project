using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Порядок состава на странице группы (решение Влада 26.09.2026): тренер первым, за ним админы
/// группы (владелец / HubGroupAdmin с привязанным пловцом), потом остальные — каждый слой в
/// своём ручном порядке.
/// </summary>
public class HubGroupRosterOrderTests
{
    private static HubGroupPublicMemberDto M(int id, string role = "member") =>
        new() { SwimmerId = id, Name = $"#{id}", Role = role };

    [Fact]
    public void CoachFirst_ThenAdmins_ThenRest_KeepingManualOrderInsideLayers()
    {
        var roster = new[] { M(1), M(2), M(3, "captain"), M(4), M(5, "coach"), M(6) };

        var ordered = HubGroupRosterOrder.Apply(roster, new HashSet<int> { 6, 2 });

        Assert.Equal(new[] { 5, 2, 6, 1, 3, 4 }, ordered.Select(m => m.SwimmerId));
        Assert.Equal(new[] { 2, 6 }, ordered.Where(m => m.IsAdmin).Select(m => m.SwimmerId));
    }

    [Fact]
    public void CoachWhoIsAlsoAdmin_StaysWithCoaches()
    {
        var roster = new[] { M(1), M(2, "coach"), M(3, "coach") };

        var ordered = HubGroupRosterOrder.Apply(roster, new HashSet<int> { 3 });

        Assert.Equal(new[] { 2, 3, 1 }, ordered.Select(m => m.SwimmerId));
        Assert.True(ordered[1].IsAdmin);   // тренер-админ носит и чип admin
    }

    [Fact]
    public void NoCoachNoLinkedAdmins_OrderUnchanged()
    {
        var roster = new[] { M(3), M(1), M(2) };

        // Админ, чей пловец не в составе, порядок не трогает.
        var ordered = HubGroupRosterOrder.Apply(roster, new HashSet<int> { 99 });

        Assert.Equal(new[] { 3, 1, 2 }, ordered.Select(m => m.SwimmerId));
    }
}

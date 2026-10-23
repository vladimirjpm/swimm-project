using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Правило удаления аккаунта (docs/plans/account-deletion-plan.md, Р2, Р3, Р6): кто не может
/// удалиться и как подтверждается удаление.
/// </summary>
public class AccountDeletionRulesTests
{
    [Fact]
    public void BlockReasons_OrdinaryUser_None()
        => Assert.Empty(AccountDeletionRules.BlockReasons(isSiteAdmin: false, ownsSharedGroups: false));

    [Fact]
    public void BlockReasons_SiteAdmin_Blocked() // Р6
        => Assert.Equal([AccountDeletionRules.ReasonSiteAdmin],
            AccountDeletionRules.BlockReasons(isSiteAdmin: true, ownsSharedGroups: false));

    [Fact]
    public void BlockReasons_SharedGroup_Blocked_UnlessAdminDecidedToDeleteIt() // Р2, Р7
    {
        Assert.Equal([AccountDeletionRules.ReasonSharedGroup],
            AccountDeletionRules.BlockReasons(isSiteAdmin: false, ownsSharedGroups: true));
        Assert.Empty(AccountDeletionRules.BlockReasons(isSiteAdmin: false, ownsSharedGroups: true, deleteSharedGroups: true));
    }

    [Fact]
    public void BlockReasons_DeleteSharedGroups_DoesNotLiftSiteAdmin()
        => Assert.Equal([AccountDeletionRules.ReasonSiteAdmin],
            AccountDeletionRules.BlockReasons(isSiteAdmin: true, ownsSharedGroups: true, deleteSharedGroups: true));

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    public void IsShared_AnyOtherAccount(int members, int admins, bool expected)
        => Assert.Equal(expected, AccountDeletionRules.IsShared(members, admins));

    [Theory]
    [InlineData("Vlad@Example.com", true)]
    [InlineData("  vlad@example.com ", true)]
    [InlineData("other@example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EmailMatches_IgnoresCaseAndSpaces(string? typed, bool expected)
        => Assert.Equal(expected, AccountDeletionRules.EmailMatches(typed, "vlad@example.com"));

    [Fact]
    public void IsFreshSignIn_WithinWindow()
    {
        var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(AccountDeletionRules.IsFreshSignIn(now.AddMinutes(-9), now));
        Assert.False(AccountDeletionRules.IsFreshSignIn(now.AddMinutes(-11), now));
        Assert.False(AccountDeletionRules.IsFreshSignIn(null, now));
        // Время «из будущего» (подделка или сильно уехавшие часы) — не свежий вход.
        Assert.False(AccountDeletionRules.IsFreshSignIn(now.AddMinutes(5), now));
    }

    [Fact]
    public void ParseAuthTime_UnixSeconds()
    {
        var t = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        var raw = new DateTimeOffset(t).ToUnixTimeSeconds().ToString();
        Assert.Equal(t, AccountDeletionRules.ParseAuthTime(raw));
        Assert.Null(AccountDeletionRules.ParseAuthTime(null));
        Assert.Null(AccountDeletionRules.ParseAuthTime("garbage"));
        Assert.Null(AccountDeletionRules.ParseAuthTime("0"));
    }
}

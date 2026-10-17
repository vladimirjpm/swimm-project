using Swimm.API.Pages.Admin.Users;
using Swimm.Application.Dtos;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Фильтр списка /Admin/Users (?show=, 17.10.2026): тестовый — только utest…@swimm.test
/// (TestAccountRules), «обычные» — все остальные, чужой ключ — все.
/// </summary>
public class AdminUsersFilterTests
{
    // Зависимости странице для Matches не нужны — фильтр читает только саму строку.
    private static readonly IndexModel Page = new(null!, null!, null!, null!, null!);

    private static UserDto U(string email, bool active = true, params string[] roles) =>
        new() { Email = email, IsActive = active, Roles = roles };

    [Theory]
    [InlineData("utest-newbie@swimm.test", "test", true)]
    [InlineData("utest-newbie@swimm.test", "regular", false)]
    [InlineData("utest.someone@gmail.com", "test", false)]      // префикс без зоны .test — живой человек
    [InlineData("utest.someone@gmail.com", "regular", true)]
    [InlineData("a@b.com", "anything-else", true)]
    [InlineData("a@b.com", null, true)]
    public void TestVsRegular(string email, string? key, bool expected) =>
        Assert.Equal(expected, Page.Matches(U(email), key));

    [Fact]
    public void Roles_And_Disabled()
    {
        Assert.True(Page.Matches(U("a@b.com", true, "User", "Admin"), "admins"));
        Assert.False(Page.Matches(U("a@b.com", true, "User"), "admins"));
        Assert.True(Page.Matches(U("a@b.com", true, "User", "Coach"), "coaches"));
        Assert.True(Page.Matches(U("a@b.com", active: false), "disabled"));
        Assert.False(Page.Matches(U("a@b.com"), "disabled"));
        Assert.False(Page.Matches(U("a@b.com"), "changed")); // нет сведений об изменениях — не изменён
    }
}

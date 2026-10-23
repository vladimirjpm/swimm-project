using Swimm.API.Security;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Открытый редирект во входе и выходе (docs/plans/account-deletion-plan.md §7): returnUrl ведёт
/// только на свой сайт, остальное — на главную.
/// </summary>
public class ReturnUrlRulesTests
{
    private const string Prod = "swimhub.co.il";

    private static string ProdSafe(string? url) => ReturnUrlRules.Safe(url, Prod, Prod, allowLocalhost: false);

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/setup-profile?delete=1", "/setup-profile?delete=1")]
    [InlineData("/competitions/12#day-2", "/competitions/12#day-2")]
    [InlineData("https://swimhub.co.il/swimmers/5?tab=h2h", "https://swimhub.co.il/swimmers/5?tab=h2h")]
    [InlineData("HTTPS://SWIMHUB.CO.IL/x", "https://swimhub.co.il/x")]
    public void OwnSite_Kept(string url, string expected) => Assert.Equal(expected, ProdSafe(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.com/")]
    [InlineData("http://evil.com")]
    [InlineData("//evil.com")]
    [InlineData("//evil.com/path")]
    [InlineData("/\\evil.com")]
    [InlineData("\\\\evil.com")]
    [InlineData("https://swimhub.co.il.evil.com/")]
    [InlineData("https://swimhub.co.il@evil.com/")]
    [InlineData("https://user@swimhub.co.il/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("evil.com")]
    [InlineData("/x\r\nSet-Cookie: a=b")]
    [InlineData("https://localhost/x")] // localhost — только на машине разработчика
    public void Foreign_GoesHome(string? url) => Assert.Equal("/", ProdSafe(url));

    [Fact]
    public void Dev_ViteOnOtherPort_Kept()
    {
        // Клиент на Vite (5181) шлёт свой полный адрес; API на 5078 видит хост localhost.
        Assert.Equal("http://localhost:5181/setup-profile",
            ReturnUrlRules.Safe("http://localhost:5181/setup-profile", "localhost", Prod, allowLocalhost: true));
        Assert.Equal("/", ReturnUrlRules.Safe("https://evil.com/", "localhost", Prod, allowLocalhost: true));
    }

    [Fact]
    public void RequestHost_Kept_EvenIfNotCanonical()
    {
        // Например, превью-слот App Service: вход на нём возвращает на него же.
        Assert.Equal("https://swimhub-staging.azurewebsites.net/x",
            ReturnUrlRules.Safe("https://swimhub-staging.azurewebsites.net/x", "swimhub-staging.azurewebsites.net", Prod, allowLocalhost: false));
    }
}

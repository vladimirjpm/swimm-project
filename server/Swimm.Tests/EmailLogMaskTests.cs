using Swimm.Infrastructure.Services.Email;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Адрес в логах уровня Error уезжает в Application Insights — маскируем
/// (docs/plans/application-insights-plan.md, А2-бис).
/// </summary>
public class EmailLogMaskTests
{
    [Theory]
    [InlineData("ivan.petrov@gmail.com", "i***@gmail.com")]
    [InlineData("a@b.co", "a***@b.co")]
    [InlineData("weird@name@host.org", "w***@host.org")]
    [InlineData("no-at-sign", "***")]
    [InlineData("@host.org", "***")]
    [InlineData("user@", "***")]
    [InlineData("", "(empty)")]
    [InlineData(null, "(empty)")]
    public void Mask(string? email, string expected) => Assert.Equal(expected, EmailLogMask.Mask(email));
}

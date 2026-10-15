using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Swimm.API.Security;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Признак «Google подтвердил email» доходит до claims (GoogleClaims). Без него AuthController не
/// пускает нового пользователя — так было до 15.10.2026: стандартный набор GoogleOptions поле не
/// переносит, и на проде не мог войти никто новый. Прогоняем настоящие ClaimActions на ответе
/// userinfo в обоих форматах Google.
/// </summary>
public class GoogleClaimsTests
{
    private static ClaimsIdentity Run(string userInfoJson, bool withOurMapping = true)
    {
        var options = new GoogleOptions();
        if (withOurMapping) GoogleClaims.Configure(options);
        var identity = new ClaimsIdentity("Google");
        using var doc = JsonDocument.Parse(userInfoJson);
        foreach (var action in options.ClaimActions)
            action.Run(doc.RootElement, identity, "Google");
        return identity;
    }

    private static bool Verified(ClaimsIdentity id) =>
        string.Equals(id.FindFirst(GoogleClaims.EmailVerified)?.Value, "true", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void UserInfoV2_VerifiedEmail_IsMapped()
    {
        var id = Run("""{"id":"1","email":"a@b.c","verified_email":true,"name":"A"}""");
        Assert.True(Verified(id));
        Assert.Equal("a@b.c", id.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public void UserInfoV3_EmailVerified_IsMapped()
    {
        Assert.True(Verified(Run("""{"sub":"1","email":"a@b.c","email_verified":true}""")));
    }

    [Fact]
    public void NotVerified_StaysFalse()
    {
        Assert.False(Verified(Run("""{"id":"1","email":"a@b.c","verified_email":false}""")));
    }

    [Fact]
    public void NoFlagInResponse_NoClaim()
    {
        Assert.Null(Run("""{"id":"1","email":"a@b.c"}""").FindFirst(GoogleClaims.EmailVerified));
    }

    [Fact]
    public void WithoutOurMapping_FlagIsLost_TheBugThisGuards()
    {
        // Стандартный набор GoogleOptions признак не переносит — ради этого и нужен GoogleClaims.
        Assert.False(Verified(Run("""{"id":"1","email":"a@b.c","verified_email":true}""", withOurMapping: false)));
    }
}

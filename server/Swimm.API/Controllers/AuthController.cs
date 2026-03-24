using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swimm.API.Data;
using Swimm.API.Models;

namespace Swimm.API.Controllers;

[Route("auth")]
public class AuthController : Controller
{
    private readonly SwimmDbContext _db;

    public AuthController(SwimmDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// ?????????????? ???? ????? Google OAuth. ????? ??????????? ?????? ???????????? ?? returnUrl.
    /// </summary>
    [HttpGet("login/google")]
    public IActionResult LoginGoogle([FromQuery] string? returnUrl = "/")
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), new { returnUrl })
        };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Callback ????? Google OAuth. ???????/????????? ???????????? ? ????????? ???? ?????.
    /// </summary>
    [HttpGet("google-callback")]
    public async Task<IActionResult> GoogleCallback([FromQuery] string? returnUrl = "/")
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (result?.Principal == null)
            return Redirect("/auth/login");

        var claims = result.Principal;
        var providerKey = claims.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var email = claims.FindFirstValue(ClaimTypes.Email) ?? "";
        var name = claims.FindFirstValue(ClaimTypes.Name) ?? email;
        var avatar = claims.FindFirstValue("urn:google:picture")
                     ?? claims.FindFirstValue("picture");

        if (string.IsNullOrEmpty(email))
            return BadRequest("Google did not return an email.");

        // ???? ??? ??????? ????????????
        var user = await _db.AppUsers
            .Include(u => u.ExternalLogins)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            user = new AppUser
            {
                Email = email,
                DisplayName = name,
                AvatarUrl = avatar,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();

            // ????????? ???? User ?? ?????????
            var defaultRole = new AppUserRole { UserId = user.Id, RoleId = 2 };
            _db.AppUserRoles.Add(defaultRole);
            await _db.SaveChangesAsync();

            // ????????? ???? ??? cookie
            await _db.Entry(user).Collection(u => u.UserRoles).Query()
                .Include(ur => ur.Role).LoadAsync();
        }
        else
        {
            // ????????? ??????? ?? Google
            user.DisplayName = name;
            user.AvatarUrl = avatar ?? user.AvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;
        }

        // ????????? ??????? ?????, ???? ?? ??? ?? ????????
        var hasLogin = user.ExternalLogins.Any(l =>
            l.Provider == "Google" && l.ProviderKey == providerKey);
        if (!hasLogin)
        {
            _db.UserExternalLogins.Add(new UserExternalLogin
            {
                UserId = user.Id,
                Provider = "Google",
                ProviderKey = providerKey,
                CreatedAt = DateTime.UtcNow
            });
        }

        // ????????? ?????? ?????
        _db.UserLoginHistory.Add(new UserLoginHistory
        {
            UserId = user.Id,
            Provider = "Google",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            LoginAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // ??????????? cookie ? ?????? ??????, ????? ???????? [Authorize(Roles)] ? IsInRole()
        var appClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName)
        };
        foreach (var ur in user.UserRoles)
            appClaims.Add(new Claim(ClaimTypes.Role, ur.Role.Name));

        var identity = new ClaimsIdentity(appClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });

        return Redirect(returnUrl ?? "/");
    }

    /// <summary>
    /// ??????? ???????????? (??? ?????????).
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Ok(new { isAuthenticated = false });

        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
            return Ok(new { isAuthenticated = false });

        var user = await _db.AppUsers
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            return Ok(new { isAuthenticated = false });

        return Ok(new
        {
            isAuthenticated = true,
            id = user.Id,
            email = user.Email,
            displayName = user.DisplayName,
            avatarUrl = user.AvatarUrl,
            roles = user.UserRoles.Select(r => r.Role.Name).ToArray(),
            swimmerId = user.SwimmerId,
            swimmerOrgId = user.SwimmerOrgId,
            clubId = user.ClubId
        });
    }

    /// <summary>
    /// ?????.
    /// </summary>
    [Authorize]
    [HttpGet("logout")]
    public async Task<IActionResult> Logout([FromQuery] string? returnUrl = "/")
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect(returnUrl ?? "/");
    }

    /// <summary>
    /// ???????? ???????? ?????? ??? ??????????????? (???? ??????????? ????? /auth/login/google).
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        return Redirect("/");
    }
}

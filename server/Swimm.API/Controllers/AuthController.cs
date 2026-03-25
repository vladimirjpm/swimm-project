using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swimm.API.Data;
using Swimm.API.Models;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SwimmDbContext _db;
    public AuthController(SwimmDbContext db) => _db = db;

    [HttpGet("google-login")]
    public IActionResult GoogleLogin(string? returnUrl = "/")
    {
        var props = new AuthenticationProperties { RedirectUri = $"/api/auth/google-callback?returnUrl={returnUrl}" };
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("google-callback")]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = "/")
    {
        var result = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
        if (!result.Succeeded) return BadRequest("Google authentication failed.");

        var email = result.Principal!.FindFirstValue(ClaimTypes.Email)!;
        var name = result.Principal.FindFirstValue(ClaimTypes.Name) ?? email;
        var avatar = result.Principal.FindFirstValue("picture");
        var googleId = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.ExternalLogins)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            user = new AppUser
            {
                Email = email,
                DisplayName = name,
                AvatarUrl = avatar,
                LastLoginAt = DateTime.UtcNow
            };
            // Первый пользователь получает роль Admin
            var anyUsers = await _db.Users.AnyAsync();
            var roleName = anyUsers ? "User" : "Admin";
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role is null)
            {
                role = new AppRole { Name = roleName };
                _db.Roles.Add(role);
                await _db.SaveChangesAsync();
            }
            user.UserRoles.Add(new AppUserRole { RoleId = role.Id, Role = role });
            _db.Users.Add(user);
        }
        else
        {
            user.DisplayName = name;
            user.AvatarUrl = avatar;
            user.LastLoginAt = DateTime.UtcNow;
        }

        // Добавляем external login если ещё нет
        if (!user.ExternalLogins.Any(e => e.Provider == "Google" && e.ProviderKey == googleId))
            user.ExternalLogins.Add(new UserExternalLogin { Provider = "Google", ProviderKey = googleId });

        // Сохраняем историю входа
        user.LoginHistory.Add(new UserLoginHistory
        {
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString()
        });

        await _db.SaveChangesAsync();

        // Создаём cookie с ролями
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
        };
        foreach (var ur in user.UserRoles)
            claims.Add(new Claim(ClaimTypes.Role, ur.Role.Name));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Redirect(returnUrl ?? "/");
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            email = User.FindFirstValue(ClaimTypes.Email),
            name = User.FindFirstValue(ClaimTypes.Name),
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value)
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Logged out" });
    }
}

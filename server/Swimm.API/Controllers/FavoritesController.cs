using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/me/favorites")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class FavoritesController : ControllerBase
{
    private readonly IUserFavoriteRepository _favorites;

    public FavoritesController(IUserFavoriteRepository favorites)
    {
        _favorites = favorites;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    [HttpGet]
    public async Task<IActionResult> GetFavorites()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _favorites.GetForUserAsync(userId.Value));
    }

    [HttpPost]
    public async Task<IActionResult> AddFavorite([FromBody] AddFavoriteRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        if (request.TargetType != "swimmer" && request.TargetType != "club")
            return BadRequest(new { error = "target_type must be 'swimmer' or 'club'" });

        if (request.TargetType == "swimmer" && request.SwimmerId == null)
            return BadRequest(new { error = "swimmer_id is required for target_type 'swimmer'" });

        if (request.TargetType == "club" && request.ClubId == null)
            return BadRequest(new { error = "club_id is required for target_type 'club'" });

        var fav = await _favorites.AddAsync(userId.Value, request);
        if (fav == null)
            return Conflict(new { error = "Already in favorites" });

        return CreatedAtAction(nameof(GetFavorites), fav);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveFavorite(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var ok = await _favorites.RemoveAsync(userId.Value, id);
        return ok ? NoContent() : NotFound(new { error = "Favorite not found" });
    }

    [HttpPost("{id:int}/primary")]
    public async Task<IActionResult> SetPrimary(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var ok = await _favorites.SetPrimaryAsync(userId.Value, id);
        return ok ? Ok(new { message = "Primary set" }) : NotFound(new { error = "Favorite not found or not a swimmer" });
    }

    [HttpDelete("{id:int}/primary")]
    public async Task<IActionResult> UnsetPrimary(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var ok = await _favorites.UnsetPrimaryAsync(userId.Value, id);
        return ok ? NoContent() : NotFound(new { error = "Favorite not found or not a swimmer" });
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder([FromBody] List<ReorderItem> items)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        await _favorites.ReorderAsync(userId.Value, items);
        return Ok(new { message = "Reordered" });
    }
}

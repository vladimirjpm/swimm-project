using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryRepository _repo;

    public CategoriesController(ICategoryRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Возвращает список всех категорий соревнований, упорядоченных по DisplayOrder.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _repo.GetCategoriesAsync();
        return Ok(categories);
    }

    /// <summary>
    /// Возвращает категорию по slug-ключу со списком соревнований.
    /// Возвращает 404, если категория не найдена.
    /// </summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> GetByKey(string key)
    {
        var result = await _repo.GetByKeyAsync(key);
        if (result is null)
            return NotFound();

        return Ok(result);
    }
}

using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryRepository _repo;
    private readonly ICacheService _cache;

    private const string CacheControlValue = "public, max-age=300";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromHours(1);

    public CategoriesController(ICategoryRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    /// <summary>
    /// Возвращает список всех категорий соревнований, упорядоченных по DisplayOrder.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        return await this.CachedJson(_cache, "http:categories",
            () => _repo.GetCategoriesAsync(), PayloadTtl, CacheControlValue);
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

        return await this.CachedJson(_cache, $"http:categories:{key}",
            () => Task.FromResult(result), PayloadTtl, CacheControlValue);
    }
}

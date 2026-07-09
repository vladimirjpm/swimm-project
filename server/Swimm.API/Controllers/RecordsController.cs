using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичные рекорды и нормативы (замена клиентских normative*.js).
/// Кэш и выборки — по регионам: ?region=world | EU | ISR | USA …
/// </summary>
[ApiController]
public class RecordsController : ControllerBase
{
    private readonly IRecordRepository _records;

    public RecordsController(IRecordRepository records)
    {
        _records = records;
    }

    /// <summary>
    /// Рекорды региона. region: "world" (по умолчанию — не задан клиент получает 400,
    /// осознанный выбор региона обязателен), category: open/age/junior/masters (опционально).
    /// </summary>
    [HttpGet("/api/records")]
    public async Task<IActionResult> GetRecords(
        [FromQuery] string? region,
        [FromQuery] string? category)
    {
        if (string.IsNullOrWhiteSpace(region))
            return BadRequest("region is required: 'world', continent code (EU/AS) or country code (ISR/USA/…)");

        if (category != null && !Record.Categories.Contains(category))
            return BadRequest($"category must be one of: {string.Join(", ", Record.Categories)}");

        return Ok(await _records.GetRecordsAsync(region, category));
    }

    /// <summary>Нормативы уровней. kind: regular/masters (опционально — иначе все).</summary>
    [HttpGet("/api/normative-standards")]
    public async Task<IActionResult> GetStandards([FromQuery] string? kind)
    {
        if (kind != null && !NormativeStandard.Kinds.Contains(kind))
            return BadRequest($"kind must be one of: {string.Join(", ", NormativeStandard.Kinds)}");

        return Ok(await _records.GetStandardsAsync(kind));
    }
}

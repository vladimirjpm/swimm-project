using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичная выдача значка серии турнира (docs/plans/series-emblem-plan.md). Картинка лежит в базе
/// байтами; адрес собирает <c>SeriesEmblemDto.PublicUrl</c>. В витрину пока не встроен — им
/// пользуется предпросмотр на /Admin/Series.
/// </summary>
[ApiController]
public class SeriesEmblemsController(ISeriesEmblemReader emblems) : ControllerBase
{
    // В адресе хэш содержимого, а строка значка неизменяема (замена — новый Id и новый хэш):
    // под одним адресом всегда одни и те же байты, браузеру незачем переспрашивать.
    private const string CacheControlValue = "public, max-age=31536000, immutable";

    [HttpGet("/api/series-emblems/{id:int}/{sha256}")]
    public async Task<IActionResult> Get(int id, string sha256, CancellationToken ct)
    {
        var file = await emblems.GetAsync(id, sha256, ct);
        if (file is null) return NotFound();

        Response.Headers.CacheControl = CacheControlValue;
        // Тип проверен по сигнатуре при загрузке; nosniff — чтобы браузер не угадывал его заново.
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Data, file.ContentType);
    }
}

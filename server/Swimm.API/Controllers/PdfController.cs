using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swimm.API.Services;

namespace Swimm.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        /// <summary>
        /// Принимает PDF-файл ивритской версии (обязательно) и опционально PDF-файл английской версии,
        /// парсит их и возвращает результаты в формате JSON.
        /// Имя файлов должно быть вида: "<competition>_<country>_<lang>.pdf", например "Championship-israel-2050_IL_EN.pdf" и соответствующая HE.
        /// Если englishFile не передан, англоязычные поля будут заполнены из ивритской версии.
        /// </summary>
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAsync(
            [FromForm] IFormFile? englishFile,
            [FromForm] IFormFile hebrewFile)
        {
            if (hebrewFile == null)
            {
                return BadRequest("Необходимо загрузить Hebrew PDF-файл.");
            }

            try
            {
                // Открываем поток ивритского PDF
                await using var heStream = hebrewFile.OpenReadStream();
                List<Result> parsed;

                if (englishFile != null)
                {
                    // Английский файл передан: парсим оба PDF
                    await using var enStream = englishFile.OpenReadStream();
                    parsed = Parser.Parse(enStream, englishFile.FileName,
                                          heStream, hebrewFile.FileName)
                                  .ToList();
                }
                else
                {
                    // Английский файл не передан: используем только ивритский для обоих
                    heStream.Position = 0;
                    parsed = Parser.Parse(null, null, heStream, hebrewFile.FileName)
                                  .ToList();
                }

                return Ok(parsed);
            }
            catch (InvalidOperationException ex)
            {
                // Ошибки синхронизации или формата
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                // Общие ошибки
                return StatusCode(StatusCodes.Status500InternalServerError,
                                  new { error = "Ошибка сервера при обработке PDF.", detail = ex.Message });
            }
        }
    }
}

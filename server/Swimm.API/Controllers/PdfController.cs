using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swimm.API.Services;
using Swimm.API.Services.Models;
using Swimm.API.Services.Parsers;

namespace Swimm.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        private static readonly Regex FileNamePattern = new(
            @"_[A-Za-z]{2,}_(?<lang>HE|EN)\.pdf$", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAsync(
            [FromForm] IFormFile? englishFile,
            [FromForm] IFormFile hebrewFile)
        {
            if (hebrewFile == null)
            {
                return BadRequest(new { error = "Hebrew PDF required." });
            }

            var heVal = ValidateFileName(hebrewFile.FileName, "HE");
            if (!heVal.IsValid)
            {
                return BadRequest(new { error = heVal.Error });
            }

            if (englishFile != null)
            {
                var enVal = ValidateFileName(englishFile.FileName, "EN");
                if (!enVal.IsValid)
                {
                    return BadRequest(new { error = enVal.Error });
                }
            }

            try
            {
                await using var heStream = hebrewFile.OpenReadStream();
                List<Result> parsed;

                if (englishFile != null)
                {
                    await using var enStream = englishFile.OpenReadStream();
                    parsed = Parser.Parse(enStream, englishFile.FileName, heStream, hebrewFile.FileName).ToList();
                }
                else
                {
                    heStream.Position = 0;
                    parsed = Parser.Parse(null, null, heStream, hebrewFile.FileName).ToList();
                }

                var debugLog = CompetitionParser.GetDebugLog();
                return Ok(new { results = parsed, debugLog = debugLog });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace, debugLog = CompetitionParser.GetDebugLog() });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Server error", detail = ex.Message, debugLog = CompetitionParser.GetDebugLog() });
            }
        }

        private static (bool IsValid, string Error) ValidateFileName(string fileName, string expectedLang)
        {
            var match = FileNamePattern.Match(fileName);
            if (!match.Success)
            {
                return (false, "Invalid filename format. Expected: name_country_lang.pdf");
            }
            var lang = match.Groups["lang"].Value.ToUpper();
            if (lang != expectedLang)
            {
                return (false, "Wrong language: " + lang + ", expected: " + expectedLang);
            }
            return (true, string.Empty);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swimm.Parser.Services.Models;
using Swimm.Parser.Services.Parsers;

namespace Swimm.Parser.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAsync(
            [FromForm] IFormFile file,
            [FromForm] IFormFile? secondaryFile = null,
            [FromForm] IFormFile? thirdFile = null,
            [FromForm] IFormFile? fourthFile = null,
            [FromForm] string format = "IsrOrg",
            [FromForm] bool isAward = false,
            [FromForm] string? poolType = null)
        {
            if (file == null)
            {
                return BadRequest(new { error = "Primary file required." });
            }

            IFormatParser parser;
            try
            {
                parser = ParserFactory.Get(format);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    error = ex.Message,
                    availableFormats = ParserFactory.AvailableFormats
                });
            }

            var extraStreams = new List<(Stream Stream, string FileName)>();
            try
            {
                await using var primaryStream = file.OpenReadStream();
                Stream? secondaryStream = secondaryFile?.OpenReadStream();

                if (thirdFile != null)
                    extraStreams.Add((thirdFile.OpenReadStream(), thirdFile.FileName));
                if (fourthFile != null)
                    extraStreams.Add((fourthFile.OpenReadStream(), fourthFile.FileName));

                var request = new ParseRequest(
                    PrimaryStream: primaryStream,
                    PrimaryFileName: file.FileName,
                    SecondaryStream: secondaryStream,
                    SecondaryFileName: secondaryFile?.FileName,
                    IsAward: isAward,
                    PoolType: poolType,
                    ExtraStreams: extraStreams.Count > 0 ? extraStreams : null
                );

                // Try normative format first (for age records)
                var normative = parser.ParseNormative(request);
                if (normative != null)
                {
                    var debugLog = parser.GetDebugLog();
                    if (secondaryStream != null)
                        await secondaryStream.DisposeAsync();
                    foreach (var (s, _) in extraStreams)
                        await s.DisposeAsync();
                    return Ok(new { format, normative, debugLog });
                }

                // Reset stream position for regular parse
                primaryStream.Position = 0;
                if (secondaryStream != null)
                    secondaryStream.Position = 0;
                foreach (var (s, _) in extraStreams)
                    s.Position = 0;

                var results = parser.Parse(request).ToList();
                var debugLog2 = parser.GetDebugLog();

                if (secondaryStream != null)
                    await secondaryStream.DisposeAsync();
                foreach (var (s, _) in extraStreams)
                    await s.DisposeAsync();

                return Ok(new { format, results, debugLog = debugLog2 });
            }
            catch (InvalidOperationException ex)
            {
                foreach (var (s, _) in extraStreams)
                    await s.DisposeAsync();
                return BadRequest(new { error = ex.Message, debugLog = parser.GetDebugLog() });
            }
            catch (Exception ex)
            {
                foreach (var (s, _) in extraStreams)
                    await s.DisposeAsync();
                return StatusCode(500, new { error = "Server error", detail = ex.Message, debugLog = parser.GetDebugLog() });
            }
        }

        [HttpGet("formats")]
        public IActionResult GetFormats() => Ok(ParserFactory.AvailableFormats);
    }
}

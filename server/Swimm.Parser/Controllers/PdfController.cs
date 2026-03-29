using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        private readonly IHttpClientFactory _httpClientFactory;

        public PdfController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }
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

        [HttpPost("fetch-world-records")]
        public async Task<IActionResult> FetchWorldRecordsAsync(
            [FromQuery] string? countryId = "962f77d6-d9c0-49ad-ba93-adc831c9ec9f")
        {
            IFormatParser parser;
            try
            {
                parser = ParserFactory.Get("WorldRecords");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            var streams = new List<MemoryStream>();
            try
            {
                var client = _httpClientFactory.CreateClient();

                var urls = new List<(string Url, string FileName)>
                {
                    ("https://api.worldaquatics.com/fina/records/report?pool=SCM&recordCode=WR", "WR_SCM.xlsx"),
                    ("https://api.worldaquatics.com/fina/records/report?pool=LCM&recordCode=WR", "WR_LCM.xlsx"),
                };

                if (!string.IsNullOrEmpty(countryId))
                {
                    urls.Add(($"https://api.worldaquatics.com/fina/records/report?recordCode=NR&pool=SCM&countryId={countryId}", "NR_SCM.xlsx"));
                    urls.Add(($"https://api.worldaquatics.com/fina/records/report?recordCode=NR&pool=LCM&countryId={countryId}", "NR_LCM.xlsx"));
                }

                foreach (var (url, _) in urls)
                {
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var ms = new MemoryStream();
                    await response.Content.CopyToAsync(ms);
                    ms.Position = 0;
                    streams.Add(ms);
                }

                var extraStreams = new List<(Stream Stream, string FileName)>();
                for (int i = 2; i < streams.Count; i++)
                    extraStreams.Add((streams[i], urls[i].FileName));

                var request = new ParseRequest(
                    PrimaryStream: streams[0],
                    PrimaryFileName: urls[0].FileName,
                    SecondaryStream: streams.Count > 1 ? streams[1] : null,
                    SecondaryFileName: streams.Count > 1 ? urls[1].FileName : null,
                    IsAward: false,
                    PoolType: null,
                    ExtraStreams: extraStreams.Count > 0 ? extraStreams : null
                );

                var normative = parser.ParseNormative(request);
                var debugLog = parser.GetDebugLog();
                var format = "WorldRecords";

                if (normative != null)
                    return Ok(new { format, normative, debugLog });

                streams.ForEach(s => s.Position = 0);
                var results = parser.Parse(request).ToList();
                return Ok(new { format, results, debugLog = parser.GetDebugLog() });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(502, new { error = "Failed to fetch from World Aquatics API", detail = ex.Message, debugLog = parser.GetDebugLog() });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Server error", detail = ex.Message, debugLog = parser.GetDebugLog() });
            }
            finally
            {
                foreach (var ms in streams)
                    await ms.DisposeAsync();
            }
        }

        [HttpGet("formats")]
        public IActionResult GetFormats() => Ok(ParserFactory.AvailableFormats);
    }
}

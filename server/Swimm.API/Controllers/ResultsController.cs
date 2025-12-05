using Microsoft.AspNetCore.Mvc;

namespace Swimm.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResultsController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetSampleResults()
        {
            var sample = new[]
            {
                new { Name = "John", Time = "00:32.45", Style = "Freestyle" },
                new { Name = "Anna", Time = "00:35.10", Style = "Backstroke" }
            };

            return Ok(sample);
        }
    }
}
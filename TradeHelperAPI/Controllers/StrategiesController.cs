using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class StrategiesController : ControllerBase
    {
        [HttpGet]
        public IActionResult Index()
        {
            // Return empty strategies array
            return Ok(new object[0]);
        }

        [HttpGet("{id}")]
        public IActionResult Details(int id)
        {
            return NotFound(new { message = "Strategy not found" });
        }

        [HttpPost]
        public IActionResult Create([FromBody] object strategy)
        {
            return BadRequest(new { message = "Strategy creation not yet implemented" });
        }

        [HttpPut("{id}")]
        public IActionResult Edit(int id, [FromBody] object strategy)
        {
            return BadRequest(new { message = "Strategy update not yet implemented" });
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            return BadRequest(new { message = "Strategy deletion not yet implemented" });
        }
    }
}


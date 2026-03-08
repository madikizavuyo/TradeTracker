using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TradeHelper.Controllers
{
    [Route("api")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet("health")]
        [AllowAnonymous]
        public IActionResult Get() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });
    }
}

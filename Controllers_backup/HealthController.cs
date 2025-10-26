using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "API is running", timestamp = DateTime.UtcNow });
    }

    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        return Ok(new { version = "1.0.0", environment = "Development" });
    }
}
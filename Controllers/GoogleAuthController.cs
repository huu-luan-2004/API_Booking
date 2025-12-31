using Microsoft.AspNetCore.Mvc;
using HotelBookingApi.Services;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/google-auth")]
public class GoogleAuthController : ControllerBase
{
    private readonly GoogleAuthService _googleAuth;

    public GoogleAuthController(GoogleAuthService googleAuth)
    {
        _googleAuth = googleAuth;
    }

    public class GoogleLoginRequest
    {
        public string idToken { get; set; } = string.Empty;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] GoogleLoginRequest req)
    {
        var result = await _googleAuth.LoginWithIdTokenAsync(req.idToken);
        if (!result.success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}

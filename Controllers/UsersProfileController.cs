using Microsoft.AspNetCore.Mvc;
using HotelBookingApi.Data;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/users")]
public class UsersProfileController : ControllerBase
{
    private readonly NguoiDungRepository _userRepo;

    public UsersProfileController(NguoiDungRepository userRepo)
    {
        _userRepo = userRepo;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            // Test user ID - replace with actual JWT token parsing later
            var userId = 1;
            var user = await _userRepo.GetByIdAsync(userId);
            
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy thông tin người dùng" });
            }

            return Ok(user);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi server", error = ex.Message });
        }
    }
}
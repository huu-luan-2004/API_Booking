using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HotelBookingApi.Services;
using HotelBookingApi.Data;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/database-auth")]
public class DatabaseAuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly NguoiDungRepository _nguoiDungRepo;

    public DatabaseAuthController(AuthService authService, NguoiDungRepository nguoiDungRepo)
    {
        _authService = authService;
        _nguoiDungRepo = nguoiDungRepo;
    }

    // Test ping endpoint
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { 
            success = true, 
            message = "Auth API is alive",
            authServiceLoaded = _authService != null,
            nguoiDungRepoLoaded = _nguoiDungRepo != null
        });
    }

    // Generate BCrypt hash for testing
    [HttpPost("generate-hash")]
    public IActionResult GenerateHash([FromBody] GenerateHashRequest request)
    {
        try
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            return Ok(new { 
                success = true,
                password = request.Password,
                hash = hash,
                message = "Copy hash SQL: UPDATE NguoiDung SET MatKhau = '" + hash + "' WHERE Email = 'your-email@example.com'"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // Test service
    [HttpGet("test-service")]
    public async Task<IActionResult> TestService()
    {
        try
        {
            // Test get by email
            var user = await _nguoiDungRepo.GetByEmailAsync("test@example.com");
            return Ok(new { 
                success = true, 
                message = "Service test OK",
                userFound = user != null
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    // Test POST
    [HttpPost("test-post")]
    public IActionResult TestPost([FromBody] RegisterRequest request)
    {
        return Ok(new { 
            success = true, 
            message = "POST test OK",
            receivedEmail = request.Email,
            receivedPassword = request.Password?.Length > 0 ? "***" : "empty"
        });
    }

    // Đăng ký tài khoản mới
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            // Debug log
            Console.WriteLine($"[DEBUG] Register called - Email: {request.Email}");
            
            var (success, message, data) = await _authService.RegisterAsync(
                request.Email,
                request.Password,
                request.HoTen ?? "",
                request.SoDienThoai,
                request.VaiTro ?? "User"
            );

            Console.WriteLine($"[DEBUG] RegisterAsync result - Success: {success}, Message: {message}");

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { success = true, message, data });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Register exception: {ex.Message}");
            Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // Đăng nhập
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var (success, message, data) = await _authService.LoginAsync(request.Email, request.Password);

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { success = true, message, data });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message });
        }
    }

    // Đổi mật khẩu (yêu cầu đăng nhập)
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            var userIdClaim = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(new { success = false, message = "Không xác định được user" });
            }

            var (success, message) = await _authService.ChangePasswordAsync(
                userId,
                request.OldPassword,
                request.NewPassword
            );

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { success = true, message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message });
        }
    }

    // Reset mật khẩu (chỉ Admin) - Random password
    [Authorize(Roles = "Admin")]
    [HttpPost("reset-password/{userId}")]
    public async Task<IActionResult> ResetPassword(int userId)
    {
        try
        {
            var (success, message, newPassword) = await _authService.ResetPasswordAsync(userId);

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { 
                success = true, 
                message, 
                data = new { 
                    userId, 
                    newPassword,
                    note = "Vui lòng thông báo mật khẩu mới cho người dùng" 
                } 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message });
        }
    }

    // Set mật khẩu cụ thể cho 1 user (chỉ Admin)
    [Authorize(Roles = "Admin")]
    [HttpPost("set-password/{userId}")]
    public async Task<IActionResult> SetPassword(int userId, [FromBody] SetPasswordRequest request)
    {
        try
        {
            var (success, message) = await _authService.SetPasswordAsync(userId, request.NewPassword);

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { 
                success = true, 
                message,
                data = new { userId }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message });
        }
    }

    // Set mật khẩu giống nhau cho TẤT CẢ user (chỉ Admin)
    [Authorize(Roles = "Admin")]
    [HttpPost("set-password-all")]
    public async Task<IActionResult> SetPasswordForAll([FromBody] SetPasswordRequest request)
    {
        try
        {
            var (success, message, count) = await _authService.SetPasswordForAllAsync(request.NewPassword);

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { 
                success = true, 
                message,
                data = new { 
                    totalUsers = count,
                    password = request.NewPassword
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message });
        }
    }

    // Lấy thông tin user hiện tại
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        try
        {
            var userIdClaim = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(new { success = false, message = "Không xác định được user" });
            }

            var user = await _nguoiDungRepo.GetByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy user" });
            }

            return Ok(new {
                success = true,
                data = new {
                    id = user.Id,
                    email = user.Email,
                    hoTen = user.HoTen,
                    soDienThoai = user.SoDienThoai,
                    vaiTro = user.VaiTro,
                    trangThai = user.TrangThai
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message });
        }
    }

    // Cập nhật thông tin cá nhân
    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        try
        {
            var userIdClaim = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(new { success = false, message = "Không xác định được user" });
            }

            await _nguoiDungRepo.UpdateProfileAsync(userId, request.HoTen, request.SoDienThoai);

            return Ok(new { success = true, message = "Cập nhật thông tin thành công" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message });
        }
    }

    // Block/Unblock user (chỉ Admin)
    [Authorize(Roles = "Admin")]
    [HttpPut("users/{userId}/status")]
    public async Task<IActionResult> UpdateUserStatus(int userId, [FromBody] UpdateStatusRequest request)
    {
        try
        {
            await _nguoiDungRepo.UpdateStatusAsync(userId, request.Status);

            return Ok(new { 
                success = true, 
                message = $"Đã cập nhật trạng thái user {userId} thành {request.Status}" 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server", error = ex.Message });
        }
    }
}

// DTO classes
public class RegisterRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? HoTen { get; set; }
    public string? SoDienThoai { get; set; }
    public string? VaiTro { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class UpdateProfileRequest
{
    public string? HoTen { get; set; }
    public string? SoDienThoai { get; set; }
}

public class UpdateStatusRequest
{
    public string Status { get; set; } = "Active"; // Active, Inactive, Banned
}

public class SetPasswordRequest
{
    public string NewPassword { get; set; } = "";
}

public class GenerateHashRequest
{
    public string Password { get; set; } = "";
}

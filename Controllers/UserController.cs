using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

/// <summary>
/// User Controller - Compatible with mobile app (singular "user")
/// This is a compatibility layer that redirects to UsersController
/// </summary>
[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly NguoiDungRepository _repo;
    
    public UserController(NguoiDungRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Health check endpoint - no auth required
    /// GET /api/user/ping
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new
        {
            success = true,
            message = "User API is working",
            timestamp = DateTime.Now,
            endpoints = new[]
            {
                "GET /api/user/ping - Health check (no auth)",
                "GET /api/user/debug-token - Debug token claims (requires auth)",
                "GET /api/user/profile - Get current user profile (requires auth)",
                "GET /api/user/me - Alternative profile endpoint (requires auth)"
            }
        });
    }

    /// <summary>
    /// Debug endpoint to check token claims
    /// GET /api/user/debug-token
    /// </summary>
    [Authorize]
    [HttpGet("debug-token")]
    public IActionResult DebugToken()
    {
        var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
        var userId = User.FindFirst("id")?.Value;
        var email = User.FindFirst("email")?.Value;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var authHeader = Request.Headers["Authorization"].ToString();

        return Ok(new
        {
            success = true,
            message = "Token valid",
            data = new
            {
                userId,
                email,
                role,
                authHeader = authHeader.Length > 50 ? authHeader.Substring(0, 50) + "..." : authHeader,
                allClaims = claims,
                isAuthenticated = User.Identity?.IsAuthenticated,
                authenticationType = User.Identity?.AuthenticationType
            }
        });
    }

    /// <summary>
    /// Get current user profile
    /// Compatible endpoint for mobile app: GET /api/user/profile
    /// </summary>
    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        var user = await _repo.GetByIdAsync(userId);
        if (user == null)
            return NotFound(new { success = false, message = "Không tìm thấy thông tin người dùng" });

        // Build full avatar URL
        var avatarPath = user.AnhDaiDien?.ToString();
        string? avatarUrl = null;
        if (!string.IsNullOrEmpty(avatarPath))
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            avatarUrl = avatarPath!.StartsWith("http") ? avatarPath : $"{baseUrl}{avatarPath}";
        }

        var userData = new
        {
            id = (int)user.Id,
            email = user.Email?.ToString(),
            hoTen = user.HoTen?.ToString(),
            soDienThoai = user.SoDienThoai?.ToString(),
            anhDaiDien = avatarPath,
            avatarUrl = avatarUrl,
            vaiTro = user.VaiTro?.ToString(),
            trangThaiTaiKhoan = user.TrangThaiTaiKhoan
        };

        return Ok(new { success = true, message = "Thông tin người dùng hiện tại", data = userData });
    }

    /// <summary>
    /// Alternative endpoint: GET /api/user/me
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        return await Profile();
    }

    /// <summary>
    /// Update user profile (compatible with mobile app)
    /// PUT /api/user/profile
    /// </summary>
    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UserUpdateProfileRequest request)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        try
        {
            // Validate request
            if (request == null)
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ" });

            // Update profile using repository
            await _repo.UpdateProfileAsync(userId, request.HoTen, request.SoDienThoai);

            // Get updated user info
            var updatedUser = await _repo.GetByIdAsync(userId);
            if (updatedUser == null)
                return NotFound(new { success = false, message = "Không tìm thấy thông tin người dùng" });

            // Build response with full avatar URL
            var avatarPath = updatedUser.AnhDaiDien?.ToString();
            string? avatarUrl = null;
            if (!string.IsNullOrEmpty(avatarPath))
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                avatarUrl = avatarPath!.StartsWith("http") ? avatarPath : $"{baseUrl}{avatarPath}";
            }

            var userData = new
            {
                id = (int)updatedUser.Id,
                email = updatedUser.Email?.ToString(),
                hoTen = updatedUser.HoTen?.ToString(),
                soDienThoai = updatedUser.SoDienThoai?.ToString(),
                anhDaiDien = avatarPath,
                avatarUrl = avatarUrl,
                vaiTro = updatedUser.VaiTro?.ToString(),
                trangThaiTaiKhoan = updatedUser.TrangThaiTaiKhoan,
                updatedAt = DateTime.Now
            };

            return Ok(new
            {
                success = true,
                message = "Cập nhật thông tin thành công",
                data = userData
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi cập nhật thông tin: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Change user password (compatible with mobile app)
    /// PUT /api/user/password
    /// </summary>
    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] UserChangePasswordRequest request)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        try
        {
            // Validate request
            if (request == null || string.IsNullOrEmpty(request.OldPassword) || string.IsNullOrEmpty(request.NewPassword))
                return BadRequest(new { success = false, message = "Vui lòng nhập đầy đủ mật khẩu cũ và mật khẩu mới" });

            if (request.NewPassword.Length < 6)
                return BadRequest(new { success = false, message = "Mật khẩu mới phải có ít nhất 6 ký tự" });

            // Get current user
            var user = await _repo.GetByIdAsync(userId);
            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy thông tin người dùng" });

            // Verify old password
            var currentPassword = user.MatKhau?.ToString();
            if (string.IsNullOrEmpty(currentPassword))
                return BadRequest(new { success = false, message = "Tài khoản chưa có mật khẩu" });

            if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, currentPassword))
                return BadRequest(new { success = false, message = "Mật khẩu cũ không đúng" });

            // Hash new password
            var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

            // Update password in database
            await _repo.UpdatePasswordAsync(userId, newPasswordHash);

            return Ok(new
            {
                success = true,
                message = "Đổi mật khẩu thành công",
                data = new
                {
                    userId = userId,
                    updatedAt = DateTime.Now
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi đổi mật khẩu: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Cập nhật vai trò (tương thích mobile): PATCH /api/user/{id}/role
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPatch("{id:int}/role")]
    public async Task<IActionResult> UpdateRole([FromRoute] int id, [FromBody] System.Text.Json.JsonElement body)
    {
        // Uỷ quyền sang UsersController logic: sử dụng cùng repository _repo
        var role = body.TryGetProperty("vaiTro", out var v) ? (v.GetString() ?? string.Empty) : string.Empty;
        var allowed = new[] { "Admin", "ChuCoSo", "KhachHang" };
        if (string.IsNullOrWhiteSpace(role) || !allowed.Contains(role))
            return BadRequest(new { success = false, message = "Vai trò không hợp lệ", allowed });

        await _repo.UpdateVaiTroAsync(id, role);
        return Ok(new { success = true, message = "Cập nhật vai trò thành công", data = new { id, vaiTro = role } });
    }

    // Alias để hỗ trợ client không gửi được PATCH
    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}/role")]
    public async Task<IActionResult> UpdateRolePut([FromRoute] int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var role = body.TryGetProperty("vaiTro", out var v) ? (v.GetString() ?? string.Empty) : string.Empty;
        var allowed = new[] { "Admin", "ChuCoSo", "KhachHang" };
        if (string.IsNullOrWhiteSpace(role) || !allowed.Contains(role))
            return BadRequest(new { success = false, message = "Vai trò không hợp lệ", allowed });

        await _repo.UpdateVaiTroAsync(id, role);
        return Ok(new { success = true, message = "Cập nhật vai trò thành công", data = new { id, vaiTro = role } });
    }

    /// <summary>
    /// Update user avatar (compatible with mobile app)
    /// PUT /api/user/avatar
    /// </summary>
    [Authorize]
    [HttpPut("avatar")]
    public async Task<IActionResult> UpdateAvatar([FromForm] IFormFile file)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "Vui lòng chọn file ảnh" });

        // Validate file type: chấp nhận mọi loại ảnh hoặc dựa trên phần mở rộng nếu Content-Type không phải image/*
        if (string.IsNullOrWhiteSpace(file.ContentType) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            var allowedExt = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".svg", ".heic", ".heif", ".jfif" };
            if (string.IsNullOrEmpty(ext) || !allowedExt.Contains(ext))
                return BadRequest(new { success = false, message = "File phải là ảnh (image/* hoặc phần mở rộng ảnh phổ biến)" });
        }

        // Validate file size (max 5MB)
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { success = false, message = "Kích thước file không được vượt quá 5MB" });

        try
        {
            // Create avatars directory if not exists
            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
            if (!Directory.Exists(uploadsPath))
                Directory.CreateDirectory(uploadsPath);

            // Delete old avatar if exists
            var currentUser = await _repo.GetByIdAsync(userId);
            var oldAvatarPath = currentUser?.AnhDaiDien?.ToString();
            if (!string.IsNullOrEmpty(oldAvatarPath))
            {
                var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldAvatarPath!.TrimStart('/'));
                if (System.IO.File.Exists(oldFilePath))
                {
                    System.IO.File.Delete(oldFilePath);
                }
            }

            // Generate unique filename
            var fileExtension = Path.GetExtension(file.FileName);
            var fileName = $"avatar_{userId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
            var filePath = Path.Combine(uploadsPath, fileName);

            // Save the file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Update user avatar in database
            var relativePath = $"/uploads/avatars/{fileName}";
            await _repo.UpdateAvatarAsync(userId, relativePath);

            // Return success with new avatar URL
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var avatarUrl = $"{baseUrl}{relativePath}";

            return Ok(new
            {
                success = true,
                message = "Cập nhật ảnh đại diện thành công",
                data = new
                {
                    avatarPath = relativePath,
                    avatarUrl = avatarUrl,
                    updatedAt = DateTime.Now
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi cập nhật ảnh đại diện: {ex.Message}"
            });
        }
    }
}

/// <summary>
/// Request model for updating user profile via /api/user/profile
/// </summary>
public class UserUpdateProfileRequest
{
    public string? HoTen { get; set; }
    public string? SoDienThoai { get; set; }
}

/// <summary>
/// Request model for changing password via /api/user/password
/// </summary>
public class UserChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

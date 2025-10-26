using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/users")]
public class UsersProfileController : ControllerBase
{
    private readonly NguoiDungRepository _repo;
    public UsersProfileController(NguoiDungRepository repo) => _repo = repo;

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        var user = await _repo.GetByIdAsync(userId);
        if (user == null)
            return NotFound(new { success = false, message = "Không tìm thấy thông tin người dùng" });

        // Build full avatar URL
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var avatarPath = user.AnhDaiDien?.ToString();
        string? avatarUrl = null;
        if (!string.IsNullOrEmpty(avatarPath))
        {
            avatarUrl = avatarPath!.StartsWith("http") ? avatarPath : $"{baseUrl}{avatarPath}";
        }

        var result = new
        {
            id = user.Id,
            email = user.Email,
            hoTen = user.HoTen,
            soDienThoai = user.SoDienThoai,
            vaiTro = user.VaiTro,
            anhDaiDien = avatarUrl,
            ngayTao = user.NgayTao,
            firebaseUid = user.FirebaseUid
        };

        return Ok(new { success = true, data = result });
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] dynamic body)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        string? hoTen = body?.hoTen;
        string? soDienThoai = body?.soDienThoai;

        try
        {
            await _repo.UpdateProfileAsync(userId, hoTen, soDienThoai);
            var updatedUser = await _repo.GetByIdAsync(userId);
            
            // Build full avatar URL
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var avatarPath = updatedUser?.AnhDaiDien?.ToString();
            string? avatarUrl = null;
            if (!string.IsNullOrEmpty(avatarPath))
            {
                avatarUrl = avatarPath!.StartsWith("http") ? avatarPath : $"{baseUrl}{avatarPath}";
            }

            var result = new
            {
                id = updatedUser?.Id,
                email = updatedUser?.Email,
                hoTen = updatedUser?.HoTen,
                soDienThoai = updatedUser?.SoDienThoai,
                vaiTro = updatedUser?.VaiTro,
                anhDaiDien = avatarUrl,
                ngayTao = updatedUser?.NgayTao,
                firebaseUid = updatedUser?.FirebaseUid
            };

            return Ok(new { success = true, message = "Cập nhật profile thành công", data = result });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi cập nhật profile: " + ex.Message });
        }
    }
}
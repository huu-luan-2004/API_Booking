using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/user")]
public class UsersController : ControllerBase
{
    private readonly NguoiDungRepository _repo;
    public UsersController(NguoiDungRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? q = null)
    {
        var (items, total) = await _repo.ListAsync(page, pageSize, q);
        
        // Build full avatar URLs for all users
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var processedItems = items.Select(user => {
            var avatarPath = user.AnhDaiDien?.ToString();
            string? avatarUrl = null;
            if (!string.IsNullOrEmpty(avatarPath))
            {
                avatarUrl = avatarPath!.StartsWith("http") ? avatarPath : $"{baseUrl}{avatarPath}";
            }
            
            return new
            {
                id = user.Id,
                email = user.Email?.ToString(),
                hoTen = user.HoTen?.ToString(),
                soDienThoai = user.SoDienThoai?.ToString(),
                anhDaiDien = avatarPath,
                avatarUrl = avatarUrl,
                vaiTro = user.VaiTro?.ToString(),
                trangThaiTaiKhoan = user.TrangThaiTaiKhoan
            };
        }).ToList();
        
        return Ok(new { success = true, message = "Danh sách người dùng", data = new { items = processedItems, total, page, pageSize } });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
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

        if (string.IsNullOrWhiteSpace(hoTen) && string.IsNullOrWhiteSpace(soDienThoai))
            return BadRequest(new { success = false, message = "Vui lòng cung cấp thông tin cần cập nhật" });

        await _repo.UpdateProfileAsync(userId, hoTen, soDienThoai);

        return Ok(new { success = true, message = "Cập nhật thông tin thành công" });
    }

    [Authorize]
    [HttpPut("avatar")]
    public async Task<IActionResult> UpdateAvatar([FromForm] IFormFile file)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "Vui lòng chọn file ảnh" });

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(new { success = false, message = "Chỉ hỗ trợ file ảnh (JPEG, PNG, GIF, WebP)" });

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

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Save relative path to database
            var relativePath = $"/uploads/avatars/{fileName}";
            await _repo.UpdateProfileAsync(userId, null, null, relativePath);

            var response = new
            {
                success = true,
                message = "Cập nhật ảnh đại diện thành công",
                data = new
                {
                    avatarUrl = relativePath,
                    fileName = fileName
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi upload ảnh: " + ex.Message });
        }
    }

    [Authorize]
    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar()
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        try
        {
            // Get current user to find avatar path
            var currentUser = await _repo.GetByIdAsync(userId);
            var oldAvatarPath = currentUser?.AnhDaiDien?.ToString();
            
            // Delete physical file if exists
            if (!string.IsNullOrEmpty(oldAvatarPath))
            {
                var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldAvatarPath!.TrimStart('/'));
                if (System.IO.File.Exists(oldFilePath))
                {
                    System.IO.File.Delete(oldFilePath);
                }
            }

            // Update database to remove avatar path
            await _repo.UpdateProfileAsync(userId, null, null, null);

            return Ok(new { success = true, message = "Xóa ảnh đại diện thành công" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi xóa ảnh: " + ex.Message });
        }
    }

    [Authorize(Roles="Admin")]
    [HttpPatch("{id:int}/role")]
    public async Task<IActionResult> UpdateRole([FromRoute] int id, [FromBody] System.Text.Json.JsonElement body)
    {
        string vaiTro = body.TryGetProperty("vaiTro", out var v) ? (v.GetString() ?? string.Empty) : string.Empty;
        var allowed = new[] { "Admin", "ChuCoSo", "KhachHang" };
        if (string.IsNullOrWhiteSpace(vaiTro) || !allowed.Contains(vaiTro))
            return BadRequest(new { success=false, message="Vai trò không hợp lệ", allowed });
        await _repo.UpdateVaiTroAsync(id, vaiTro);
        return Ok(new { success=true, message="Cập nhật vai trò thành công", data = new { id, vaiTro } });
    }
}

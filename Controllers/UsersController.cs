using HotelBookingApi.Data;
using HotelBookingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/user")]
public class UsersController : ControllerBase
{
    private readonly NguoiDungRepository _repo;
    private readonly FirebaseService _firebase;
    public UsersController(NguoiDungRepository repo, FirebaseService firebase) { _repo = repo; _firebase = firebase; }

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


    public class UpdateProfileRequest
    {
        public string? hoTen { get; set; }
        public string? soDienThoai { get; set; }
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest? body)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        var hoTen = body?.hoTen;
        var soDienThoai = body?.soDienThoai;

        if (string.IsNullOrWhiteSpace(hoTen) && string.IsNullOrWhiteSpace(soDienThoai))
            return BadRequest(new { success = false, message = "Vui lòng cung cấp thông tin cần cập nhật" });

        await _repo.UpdateProfileAsync(userId, hoTen, soDienThoai);

        return Ok(new { success = true, message = "Cập nhật thông tin thành công" });
    }

    public class ChangeEmailRequest
    {
        public string email { get; set; } = string.Empty;
        public string idToken { get; set; } = string.Empty; // Firebase ID token after email updated
    }

    [Authorize]
    [HttpPut("email")]
    public async Task<IActionResult> UpdateEmail([FromBody] ChangeEmailRequest? body)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });
        if (body is null || string.IsNullOrWhiteSpace(body.email) || string.IsNullOrWhiteSpace(body.idToken))
            return BadRequest(new { success=false, message="Thiếu email hoặc idToken" });

        // Verify Firebase ID token and ensure it reflects the new email
        var decoded = await _firebase.VerifyIdTokenAsync(body.idToken);
        if (decoded is null)
            return BadRequest(new { success=false, message="ID token không hợp lệ" });
        var decodedEmail = decoded.Value.Email;
        if (!string.Equals(decodedEmail, body.email, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success=false, message="Vui lòng cập nhật email trên Firebase trước, sau đó gửi lại idToken mới (chứa email mới) để đồng bộ DB" });

        // Check uniqueness
        var other = await _repo.FindByEmailAsync(body.email);
        if (other is not null && Convert.ToInt32(other.Id) != userId)
            return Conflict(new { success=false, message="Email đã tồn tại" });

        await _repo.UpdateEmailAsync(userId, body.email);
        return Ok(new { success=true, message="Đồng bộ email thành công" });
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

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var absoluteUrl = relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? relativePath : $"{baseUrl}{relativePath}";

            var response = new
            {
                success = true,
                message = "Cập nhật ảnh đại diện thành công",
                data = new
                {
                    // Giữ nguyên avatarUrl là path tương đối để tương thích cũ
                    avatarUrl = relativePath,
                    // Bổ sung absoluteUrl để FE có thể dùng trực tiếp
                    absoluteUrl = absoluteUrl,
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

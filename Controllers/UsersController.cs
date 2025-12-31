using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly NguoiDungRepository _repo;
    public UsersController(NguoiDungRepository repo) { _repo = repo; }

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

    // Compatibility endpoint for mobile apps: /api/users/profile
    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });

        var user = await _repo.GetByIdAsync(userId);
        if (user == null)
            return NotFound(new { success = false, message = "Không tìm thấy thông tin người dùng" });

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

    // API lấy thông tin user theo ID (cho Admin)
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(int id)
    {
        try
        {
            var user = await _repo.GetByIdAsync(id);
            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy người dùng với ID này" });

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
                trangThaiTaiKhoan = user.TrangThaiTaiKhoan,
                ngayTao = user.NgayTao,
                diaChi = user.DiaChi?.ToString(),
                gioiTinh = user.GioiTinh?.ToString(),
                ngaySinh = user.NgaySinh
            };

            return Ok(new { success = true, message = "Thông tin chi tiết người dùng", data = userData });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server khi lấy thông tin người dùng", error = ex.Message });
        }
    }

    // API thống kê users cho Admin
    [HttpGet("stats")]
    public async Task<IActionResult> GetUserStats()
    {
        try
        {
            var (users, total) = await _repo.ListAsync(1, 1000, null); // Lấy tất cả để thống kê
            
            var stats = new
            {
                totalUsers = total,
                usersByRole = users.GroupBy(u => u.VaiTro?.ToString() ?? "Unknown")
                                  .Select(g => new { role = g.Key, count = g.Count() })
                                  .ToList(),
                usersByStatus = users.GroupBy(u => u.TrangThaiTaiKhoan?.ToString() ?? "Unknown")
                                    .Select(g => new { status = g.Key, count = g.Count() })
                                    .ToList(),
                recentUsers = users.OrderByDescending(u => u.NgayTao ?? DateTime.MinValue)
                                  .Take(5)
                                  .Select(u => new {
                                      id = u.Id,
                                      hoTen = u.HoTen?.ToString(),
                                      email = u.Email?.ToString(),
                                      vaiTro = u.VaiTro?.ToString(),
                                      ngayTao = u.NgayTao
                                  })
                                  .ToList()
            };

            return Ok(new { success = true, message = "Thống kê người dùng", data = stats });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server khi lấy thống kê", error = ex.Message });
        }
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
    }

    [Authorize]
    [HttpPut("email")]
    public async Task<IActionResult> UpdateEmail([FromBody] ChangeEmailRequest? body)
    {
        if (!int.TryParse(User.FindFirst("id")?.Value, out var userId))
            return Unauthorized(new { success = false, message = "Token không hợp lệ" });
        if (body is null || string.IsNullOrWhiteSpace(body.email))
            return BadRequest(new { success=false, message="Thiếu email" });

        // Check uniqueness
        var other = await _repo.FindByEmailAsync(body.email);
        if (other is not null && Convert.ToInt32(other.Id) != userId)
            return Conflict(new { success=false, message="Email đã tồn tại" });

        await _repo.UpdateEmailAsync(userId, body.email);
        return Ok(new { success=true, message="Cập nhật email thành công" });
    }

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

    // Alias để hỗ trợ client không gửi được PATCH
    [Authorize(Roles="Admin")]
    [HttpPut("{id:int}/role")]
    public async Task<IActionResult> UpdateRolePut([FromRoute] int id, [FromBody] System.Text.Json.JsonElement body)
    {
        string vaiTro = body.TryGetProperty("vaiTro", out var v) ? (v.GetString() ?? string.Empty) : string.Empty;
        var allowed = new[] { "Admin", "ChuCoSo", "KhachHang" };
        if (string.IsNullOrWhiteSpace(vaiTro) || !allowed.Contains(vaiTro))
            return BadRequest(new { success=false, message="Vai trò không hợp lệ", allowed });
        await _repo.UpdateVaiTroAsync(id, vaiTro);
        return Ok(new { success=true, message="Cập nhật vai trò thành công", data = new { id, vaiTro } });
    }
}

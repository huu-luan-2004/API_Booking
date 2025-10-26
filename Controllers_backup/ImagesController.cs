using HotelBookingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/images")]
public class ImagesController : ControllerBase
{
    private readonly FirebaseStorageService _storage;
    public ImagesController(FirebaseStorageService storage) => _storage = storage;

    // Upload ảnh: multipart/form-data với key file (hoặc image), folder tùy chọn
    [Authorize(Roles = "ChuCoSo,Admin")]
    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Upload([FromQuery] string? folder = null)
    {
        if (!_storage.IsConfigured(out var err))
            return BadRequest(new { success=false, message = err });
        var form = await Request.ReadFormAsync();
        var file = form.Files["file"] ?? form.Files["image"] ?? form.Files.FirstOrDefault();
        if (file == null || file.Length == 0) return BadRequest(new { success=false, message="Thiếu file để upload" });
        var f = (folder ?? "uploads").Trim('/');
        var (path, url) = await _storage.UploadAsync(file, f);
        return StatusCode(201, new { success=true, message="Upload thành công", data = new { path, url } });
    }

    // Liệt kê ảnh theo prefix (thư mục)
    [Authorize(Roles = "ChuCoSo,Admin")]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? prefix = null, [FromQuery] int pageSize = 50, [FromQuery] string? pageToken = null)
    {
        if (!_storage.IsConfigured(out var err))
            return BadRequest(new { success=false, message = err });
        var (items, next) = await _storage.ListAsync(prefix, Math.Clamp(pageSize, 1, 100), pageToken);
        return Ok(new { success=true, data = new { items, nextPageToken = next } });
    }

    // Xóa ảnh theo path (object name)
    [Authorize(Roles = "ChuCoSo,Admin")]
    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { success=false, message="Thiếu path" });
        var ok = await _storage.DeleteAsync(path);
        return Ok(new { success = ok, message = ok ? "Đã xóa ảnh" : "Không xóa được ảnh" });
    }

    // Tạo signed URL tạm thời (nếu có service account)
    [Authorize(Roles = "ChuCoSo,Admin")]
    [HttpGet("signed-url")]
    public IActionResult SignedUrl([FromQuery] string path, [FromQuery] int minutes = 10)
    {
        if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { success=false, message="Thiếu path" });
        var url = _storage.GetSignedUrl(path, TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 60)));
        if (url is null) return BadRequest(new { success=false, message = "Không tạo được signed URL (cần FIREBASE_SERVICE_ACCOUNT_PATH)" });
        return Ok(new { success=true, data = new { url } });
    }
}

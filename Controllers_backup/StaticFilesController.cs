using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/static")]
public class StaticFilesController : ControllerBase
{
    [HttpGet("avatars/{fileName}")]
    public IActionResult GetAvatar(string fileName)
    {
        try
        {
            // Validate filename to prevent directory traversal
            if (string.IsNullOrEmpty(fileName) || fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
            {
                return BadRequest(new { success = false, message = "Tên file không hợp lệ" });
            }

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars", fileName);
            
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { success = false, message = "Không tìm thấy file ảnh" });
            }

            var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = fileExtension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, contentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi tải ảnh: " + ex.Message });
        }
    }

    [HttpGet("uploads/{category}/{fileName}")]
    public IActionResult GetFile(string category, string fileName)
    {
        try
        {
            // Validate inputs to prevent directory traversal
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(fileName) || 
                category.Contains("..") || category.Contains("/") || category.Contains("\\") ||
                fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
            {
                return BadRequest(new { success = false, message = "Đường dẫn file không hợp lệ" });
            }

            // Only allow specific categories
            var allowedCategories = new[] { "avatars", "rooms", "accommodations" };
            if (!allowedCategories.Contains(category.ToLower()))
            {
                return BadRequest(new { success = false, message = "Danh mục file không được phép" });
            }

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", category, fileName);
            
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { success = false, message = "Không tìm thấy file" });
            }

            var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = fileExtension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, contentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi tải file: " + ex.Message });
        }
    }
}
using HotelBookingApi.Data;
using HotelBookingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/cosoluutru")]
public class CoSoLuuTruController : ControllerBase
{
    private readonly CoSoLuuTruRepository _repo;
    private readonly OpenStreetMapService _mapService;
    public CoSoLuuTruController(CoSoLuuTruRepository repo, OpenStreetMapService mapService) 
    { 
        _repo = repo; 
        _mapService = mapService;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page=1, [FromQuery] int pageSize=20, [FromQuery] string? q=null, [FromQuery] string? ownerId = null)
    {
        var roles = User?.Claims?.Where(c => c.Type==System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList() ?? new();
        // includeUnapproved: Admin luôn thấy tất cả; ngoài ra, nếu người dùng đang xem chính dữ liệu của họ (effectiveOwner == authUserId),
        // cũng cho phép thấy cả bản ghi chưa duyệt của chính họ.
        var includeUnapproved = roles.Contains("Admin");
        int? authUserId = int.TryParse(User?.FindFirst("id")?.Value, out var idVal) ? idVal : null;

        // Parse ownerId from string to int safely
        int? ownerIdInt = null;
        if (!string.IsNullOrWhiteSpace(ownerId) && int.TryParse(ownerId, out var parseResult))
        {
            ownerIdInt = parseResult;
        }

        // Decide which ownerId to pass to repository:
        // - If caller provided ownerId in query and is Admin, allow it.
        // - If caller provided ownerId but is not Admin, only allow if it equals the authenticated user's id.
        // - If no ownerId provided and caller is authenticated but not Admin, default to their own id (show only their items).
        int? effectiveOwnerId = null;
        if (ownerIdInt.HasValue)
        {
            if (roles.Contains("Admin")) effectiveOwnerId = ownerIdInt;
            else if (authUserId.HasValue && ownerIdInt.Value == authUserId.Value) effectiveOwnerId = ownerIdInt; // allow querying own id
            else effectiveOwnerId = authUserId; // ignore requested ownerId and restrict to own id
        }
        else
        {
            if (!roles.Contains("Admin") && authUserId.HasValue) effectiveOwnerId = authUserId;
        }
        
        // Nếu không phải Admin mà đang xem chính dữ liệu của mình => includeUnapproved = true
        if (!includeUnapproved && authUserId.HasValue && effectiveOwnerId.HasValue && authUserId.Value == effectiveOwnerId.Value)
            includeUnapproved = true;

        var rawData = await _repo.ListAsync(page, pageSize, q, includeUnapproved, effectiveOwnerId);
        
        // Add full image URLs for all accommodations
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var processedData = rawData.Select(item => {
            var itemDict = item as IDictionary<string, object> ?? new Dictionary<string, object>();
            var imagePath = item.Anh?.ToString();
            if (!string.IsNullOrEmpty(imagePath))
            {
                itemDict["imageUrl"] = $"{baseUrl}{imagePath}";
            }
            return itemDict;
        }).ToList();
        
        return Ok(new { success=true, message="Danh sách cơ sở lưu trú", data = processedData });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get([FromRoute] int id)
    {
        var cs = await _repo.GetByIdAsync(id);
        if (cs is null) return NotFound(new { success=false, message="Không tìm thấy cơ sở lưu trú" });
        // Quy tắc duyệt: nếu có trường TrangThaiDuyet thì chặn khi chưa duyệt
        var roles = User?.Claims?.Where(c => c.Type==System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList() ?? new();
        var isAdmin = roles.Contains("Admin");
        var isOwner = int.TryParse(User?.FindFirst("id")?.Value, out var uid) && ((int?)cs?.IdNguoiDung)==uid;
        if ((string?)cs?.TrangThaiDuyet != null && (string?)cs?.TrangThaiDuyet != "DaDuyet" && !isAdmin && !isOwner)
            return StatusCode(403, new { success=false, message="Cơ sở lưu trú chưa được duyệt" });
        
        // Add full image URL
        var csData = cs as IDictionary<string, object> ?? new Dictionary<string, object>();
        var imagePath = cs?.Anh?.ToString();
        if (!string.IsNullOrEmpty(imagePath))
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            csData["imageUrl"] = $"{baseUrl}{imagePath}";
        }
        
        return Ok(new { success=true, data = csData });
    }

    [Authorize(Roles="ChuCoSo")]
    [HttpPost]
    public async Task<IActionResult> Create()
    {
        string? tenCoSo = null; string? moTa = null; string? soTaiKhoan = null; 
        string? tenTaiKhoan = null; string? tenNganHang = null; string? anhPath = null;
        
        // Thông tin địa chỉ
        string? soNha = null; string? phuong = null; string? quan = null; string? thanhPho = null;
        double? kinhDo = null; double? viDo = null;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            tenCoSo = form["tenCoSo"].FirstOrDefault();
            moTa = form["moTa"].FirstOrDefault();
            soTaiKhoan = form["soTaiKhoan"].FirstOrDefault();
            tenTaiKhoan = form["tenTaiKhoan"].FirstOrDefault();
            tenNganHang = form["tenNganHang"].FirstOrDefault();
            
            // Đọc thông tin địa chỉ
            soNha = form["soNha"].FirstOrDefault();
            phuong = form["phuong"].FirstOrDefault();
            quan = form["quan"].FirstOrDefault();
            thanhPho = form["thanhPho"].FirstOrDefault();
            if (double.TryParse(form["kinhDo"].FirstOrDefault(), out var kinhDoVal)) kinhDo = kinhDoVal;
            if (double.TryParse(form["viDo"].FirstOrDefault(), out var viDoVal)) viDo = viDoVal;

            var file = form.Files["file"] ?? form.Files["image"] ?? form.Files.FirstOrDefault();
            if (file != null && file.Length > 0)
            {
                // Validate file type
                var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };
                if (!allowedTypes.Contains(file.ContentType.ToLower()))
                    return BadRequest(new { success = false, message = "Chỉ hỗ trợ file ảnh (JPEG, PNG, GIF, WebP)" });

                // Validate file size (max 10MB)
                if (file.Length > 10 * 1024 * 1024)
                    return BadRequest(new { success = false, message = "Kích thước file không được vượt quá 10MB" });

                try
                {
                    // Create accommodations directory if not exists
                    var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "accommodations");
                    if (!Directory.Exists(uploadsPath))
                        Directory.CreateDirectory(uploadsPath);

                    // Generate unique filename
                    var fileExtension = Path.GetExtension(file.FileName);
                    var fileName = $"accommodation_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{fileExtension}";
                    var filePath = Path.Combine(uploadsPath, fileName);

                    // Save file
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Save relative path
                    anhPath = $"/uploads/accommodations/{fileName}";
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { success = false, message = "Lỗi khi lưu ảnh: " + ex.Message });
                }
            }
        }
        else
        {
            return BadRequest(new { success = false, message = "Vui lòng sử dụng multipart/form-data để upload dữ liệu và ảnh" });
        }

        if (string.IsNullOrWhiteSpace(tenCoSo))
            return BadRequest(new { success=false, message="Thiếu tenCoSo" });
        
        if (string.IsNullOrWhiteSpace(thanhPho))
            return BadRequest(new { success=false, message="Thành phố là bắt buộc" });
        
        var idUser = int.TryParse(User?.FindFirst("id")?.Value, out var uid) ? uid : 0;
        
        int? idDiaChi = null;

        // Auto-geocoding nếu chưa có tọa độ
        if (!kinhDo.HasValue || !viDo.HasValue)
        {
            try
            {
                var fullAddress = $"{soNha} {phuong}, {quan}, {thanhPho}, Vietnam".Trim().Replace("  ", " ");
                var geocodeResult = await _mapService.GeocodeAsync(fullAddress);
                
                if (geocodeResult != null)
                {
                    kinhDo = geocodeResult.Longitude;
                    viDo = geocodeResult.Latitude;
                }
            }
            catch (Exception)
            {
                // Log error but continue - geocoding is optional
            }
        }

        // Tạo địa chỉ mới trong bảng DiaChiChiTiet
        try
        {
            idDiaChi = await _repo.CreateAddressAsync(new {
                soNha = soNha,
                phuong = phuong, 
                quan = quan,
                thanhPho = thanhPho,
                kinhDo = kinhDo,
                viDo = viDo
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi tạo địa chỉ: " + ex.Message });
        }

        // Convert to object for repository
        var coSoData = new {
            tenCoSo = tenCoSo,
            moTa = moTa,
            soTaiKhoan = soTaiKhoan,
            tenTaiKhoan = tenTaiKhoan,
            tenNganHang = tenNganHang,
            anh = anhPath, // image file path
            idDiaChi = idDiaChi,
            idNguoiDung = idUser
        };
        
        var cs = await _repo.CreateAsync(coSoData);
        await _repo.EnsurePendingApprovalAsync((int)cs.Id);

        // Add full image URL to response
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var csData = cs as IDictionary<string, object> ?? new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(anhPath))
        {
            csData["imageUrl"] = $"{baseUrl}{anhPath}";
        }

        return StatusCode(201, new { success=true, message="Tạo cơ sở lưu trú thành công, vui lòng chờ admin duyệt", data = csData });
    }

    [Authorize(Roles="Admin")]
    [HttpPatch("{id:int}/approve")]
    public async Task<IActionResult> Approve([FromRoute] int id)
    {
        await _repo.SetApprovalStatusAsync(id, "DaDuyet");
        var cs = await _repo.GetByIdAsync(id);
        return Ok(new { success=true, message="Đã duyệt cơ sở lưu trú", data = cs });
    }

    [Authorize(Roles="Admin")]
    [HttpPatch("{id:int}/reject")]
    public async Task<IActionResult> Reject([FromRoute] int id, [FromBody] System.Text.Json.JsonElement body)
    {
        string? lyDo = body.TryGetProperty("lyDo", out var v) ? v.GetString() : null;
        await _repo.SetApprovalStatusAsync(id, "TuChoi", lyDo);
        var cs = await _repo.GetByIdAsync(id);
        return Ok(new { success=true, message="Đã từ chối cơ sở lưu trú", data = cs });
    }

    [Authorize]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id)
    {
        // Kiểm tra quyền: chỉ chủ cơ sở hoặc Admin mới được sửa
        var existing = await _repo.GetByIdAsync(id);
        if (existing == null)
            return NotFound(new { success = false, message = "Không tìm thấy cơ sở lưu trú" });

        var currentUserId = int.TryParse(User?.FindFirst("id")?.Value, out var uid) ? uid : 0;
        var roles = User?.Claims?.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList() ?? new();
        
        // Kiểm tra quyền sở hữu
        if (!roles.Contains("Admin") && (int)existing.IdNguoiDung != currentUserId)
            return Forbid("Bạn không có quyền sửa cơ sở này");

        string? tenCoSo = null; string? moTa = null; string? soTaiKhoan = null; 
        string? tenTaiKhoan = null; string? tenNganHang = null; string? anhPath = null;
        int? idDiaChi = null;
        bool hasNewImage = false;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            tenCoSo = form["tenCoSo"].FirstOrDefault();
            moTa = form["moTa"].FirstOrDefault();
            soTaiKhoan = form["soTaiKhoan"].FirstOrDefault();
            tenTaiKhoan = form["tenTaiKhoan"].FirstOrDefault();
            tenNganHang = form["tenNganHang"].FirstOrDefault();
            if (int.TryParse(form["idDiaChi"].FirstOrDefault(), out var idDiaChiVal)) idDiaChi = idDiaChiVal;

            var file = form.Files["file"] ?? form.Files["image"] ?? form.Files.FirstOrDefault();
            if (file != null && file.Length > 0)
            {
                hasNewImage = true;
                
                // Validate file type
                var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };
                if (!allowedTypes.Contains(file.ContentType.ToLower()))
                    return BadRequest(new { success = false, message = "Chỉ hỗ trợ file ảnh (JPEG, PNG, GIF, WebP)" });

                // Validate file size (max 10MB)
                if (file.Length > 10 * 1024 * 1024)
                    return BadRequest(new { success = false, message = "Kích thước file không được vượt quá 10MB" });

                try
                {
                    // Delete old image if exists
                    var oldImagePath = existing.Anh?.ToString();
                    if (!string.IsNullOrEmpty(oldImagePath))
                    {
                        var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldImagePath!.TrimStart('/'));
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            System.IO.File.Delete(oldFilePath);
                        }
                    }

                    // Create accommodations directory if not exists
                    var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "accommodations");
                    if (!Directory.Exists(uploadsPath))
                        Directory.CreateDirectory(uploadsPath);

                    // Generate unique filename
                    var fileExtension = Path.GetExtension(file.FileName);
                    var fileName = $"accommodation_{id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
                    var filePath = Path.Combine(uploadsPath, fileName);

                    // Save file
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Save relative path
                    anhPath = $"/uploads/accommodations/{fileName}";
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { success = false, message = "Lỗi khi lưu ảnh: " + ex.Message });
                }
            }
        }
        else
        {
            return BadRequest(new { success = false, message = "Vui lòng sử dụng multipart/form-data" });
        }

        // Update data object
        var updateData = new {
            tenCoSo = tenCoSo,
            moTa = moTa,
            soTaiKhoan = soTaiKhoan,
            tenTaiKhoan = tenTaiKhoan,
            tenNganHang = tenNganHang,
            anh = hasNewImage ? anhPath : existing.Anh?.ToString(), // Keep old image if no new one
            idDiaChi = idDiaChi
        };

        await _repo.UpdateAsync(id, updateData);
        
        var updated = await _repo.GetByIdAsync(id);
        
        // Add full image URL to response
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var csData = updated as IDictionary<string, object> ?? new Dictionary<string, object>();
        var imagePath = updated?.Anh?.ToString();
        if (!string.IsNullOrEmpty(imagePath))
        {
            csData["imageUrl"] = $"{baseUrl}{imagePath}";
        }

        return Ok(new { success = true, message = "Cập nhật cơ sở lưu trú thành công", data = csData });
    }

    [Authorize]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        // Kiểm tra quyền: chỉ chủ cơ sở hoặc Admin mới được xóa
        var existing = await _repo.GetByIdAsync(id);
        if (existing == null)
            return NotFound(new { success = false, message = "Không tìm thấy cơ sở lưu trú" });

        var currentUserId = int.TryParse(User?.FindFirst("id")?.Value, out var uid) ? uid : 0;
        var roles = User?.Claims?.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList() ?? new();
        
        // Kiểm tra quyền sở hữu
        if (!roles.Contains("Admin") && (int)existing.IdNguoiDung != currentUserId)
            return Forbid("Bạn không có quyền xóa cơ sở này");

        try
        {
            // Delete image file if exists
            var imagePath = existing.Anh?.ToString();
            if (!string.IsNullOrEmpty(imagePath))
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", imagePath!.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }

            // Delete from database
            await _repo.DeleteAsync(id);

            return Ok(new { success = true, message = "Xóa cơ sở lưu trú thành công" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi xóa cơ sở: " + ex.Message });
        }
    }
}

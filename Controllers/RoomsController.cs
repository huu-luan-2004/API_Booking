using HotelBookingApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Dynamic;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/rooms")]
public class RoomsController : ControllerBase
{
    private readonly PhongRepository _repo;
    private readonly KhuyenMaiRepository _kmRepo;
    private readonly CoSoLuuTruRepository _cslRepo;
    private readonly HotelBookingApi.Services.FirebaseStorageService _storage;
    public RoomsController(PhongRepository repo, KhuyenMaiRepository kmRepo, CoSoLuuTruRepository cslRepo, HotelBookingApi.Services.FirebaseStorageService storage) { _repo = repo; _kmRepo = kmRepo; _cslRepo = cslRepo; _storage = storage; }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page=1, [FromQuery] int pageSize=20, [FromQuery] string? q=null, [FromQuery] int? accommodationId = null, [FromQuery] int? cosoluutru_id = null)
    {
        var data = (await _repo.GetAllAsync(page, pageSize, q)).ToList();
        var targetId = accommodationId ?? cosoluutru_id;
        if (targetId.HasValue)
        {
            data = data.Where(r => TryGet<int?>(r, "IdCoSoLuuTru") == targetId.Value).ToList();
        }
        // Gắn khuyến mãi tự động (nếu có) để FE gạch giá
        try
        {
            var ids = data.Select(d => { try { return (int)d.Id; } catch { return 0; } }).Where(i => i>0).Distinct().ToArray();
            var promos = await _kmRepo.GetActiveAutoForRoomsAsync(ids);
            var map = promos.ToDictionary(p => (int)p.IdPhong, p => p);
            var projected = new List<object>(data.Count);
            foreach (var r in data)
            {
                int id = TryGet<int>(r, "Id") ?? 0;
                decimal? gia = TryGet<decimal?>(r, "Gia");
                var km = map.ContainsKey(id) ? map[id] : null;
                decimal? giaGoc = gia;
                decimal? giaKM = null;
                bool coKM = false;
                if (giaGoc.HasValue && km != null)
                {
                    var loai = (TryGet<string>(km, "LoaiGiamGia") ?? string.Empty).ToLowerInvariant();
                    var giaTri = TryGet<decimal?>(km, "GiaTriGiam") ?? 0m;
                    var baseGia = giaGoc.GetValueOrDefault(0m);
                    var calc = baseGia;
                    if (loai.Contains("percent") || loai.Contains("phantram") || loai == "pct") calc = Math.Round(baseGia * (1 - giaTri/100));
                    else calc = Math.Max(0, baseGia - giaTri);
                    if (calc < giaGoc.Value) { giaKM = calc; coKM = true; }
                }
                var imagePath = TryGet<string>(r, "Anh");
                var imageUrl = !string.IsNullOrEmpty(imagePath) ? $"{Request.Scheme}://{Request.Host}{imagePath}" : null;
                
                projected.Add(new {
                    Id = id,
                    TenPhong = TryGet<string>(r,"TenPhong"),
                    MoTa = TryGet<string>(r,"MoTa"),
                    SoNguoiToiDa = TryGet<int?>(r,"SoNguoiToiDa"),
                    IdCoSoLuuTru = TryGet<int?>(r,"IdCoSoLuuTru"),
                    IdLoaiPhong = TryGet<int?>(r,"IdLoaiPhong"),
                    Anh = imagePath, // Image file path
                    ImageUrl = imageUrl, // Full image URL
                    NgayApDungGia = TryGet<DateTime?>(r, "NgayApDungGia"),
                    Gia = coKM ? giaKM : giaGoc,
                    GiaGoc = giaGoc,
                    GiaKhuyenMai = giaKM,
                    CoKhuyenMai = coKM,
                    ChiTietKhuyenMai = km
                });
            }
            return Ok(new { success=true, message="Danh sách phòng", data = projected });
        }
        catch
        {
            // Nếu lỗi, trả dữ liệu gốc
            return Ok(new { success=true, message="Danh sách phòng", data });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get([FromRoute] int id)
    {
        var room = await _repo.GetByIdAsync(id);
        if (room is null) return NotFound(new { success=false, message="Không tìm thấy phòng" });
        
        // Add full image URL
        var imagePath = TryGet<string>(room, "Anh");
        var roomData = room as IDictionary<string, object> ?? new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(imagePath))
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            roomData["ImageUrl"] = $"{baseUrl}{imagePath}";
        }
        
        return Ok(new { success=true, data = roomData });
    }

    [HttpPost]
    [Authorize(Roles = "ChuCoSo")] // only owners can create rooms
    [RequestSizeLimit(20_000_000)] // ~20MB uploads
    public async Task<IActionResult> Create()
    {
        // Parse safely from JsonElement and map to a dynamic object compatible with repository
        // Save image as file on server instead of base64 in database
        string? tenPhong = null; string? moTa = null; int? soNguoiToiDa = null; int? idCoSoLuuTru = null; int? idLoaiPhong = null; decimal? gia = null; string? anhPath = null;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            tenPhong = form["tenPhong"].FirstOrDefault();
            moTa = form["moTa"].FirstOrDefault();
            if (int.TryParse(form["soNguoiToiDa"].FirstOrDefault(), out var sntdVal)) soNguoiToiDa = sntdVal;
            if (int.TryParse(form["idCoSoLuuTru"].FirstOrDefault(), out var idcsVal)) idCoSoLuuTru = idcsVal;
            if (int.TryParse(form["idLoaiPhong"].FirstOrDefault(), out var idlpVal)) idLoaiPhong = idlpVal;
            // Accept both 'gia' and 'giaPhong' for price input
            if (decimal.TryParse(form["gia"].FirstOrDefault(), out var giaVal)) gia = giaVal;
            else if (decimal.TryParse(form["giaPhong"].FirstOrDefault(), out var giaPhongVal)) gia = giaPhongVal;

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
                    // Create rooms directory if not exists
                    var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "rooms");
                    if (!Directory.Exists(uploadsPath))
                        Directory.CreateDirectory(uploadsPath);

                    // Generate unique filename
                    var fileExtension = Path.GetExtension(file.FileName);
                    var fileName = $"room_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{fileExtension}";
                    var filePath = Path.Combine(uploadsPath, fileName);

                    // Save file
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Save relative path
                    anhPath = $"/uploads/rooms/{fileName}";
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { success = false, message = "Lỗi khi lưu ảnh: " + ex.Message });
                }
            }
        }
        else
        {
            return BadRequest(new { success = false, message = "Vui lòng sử dụng multipart/form-data để upload ảnh" });
        }

        if (string.IsNullOrWhiteSpace(tenPhong))
            return BadRequest(new { success=false, message="Thiếu tenPhong" });

        // Require image upload
        if (string.IsNullOrWhiteSpace(anhPath))
            return BadRequest(new { success=false, message="Bắt buộc phải có ảnh phòng. Vui lòng upload ảnh bằng multipart/form-data với key 'file' hoặc 'image'." });

        // Validate foreign keys
        if (idCoSoLuuTru.HasValue)
        {
            var csl = await _cslRepo.GetByIdAsync(idCoSoLuuTru.Value);
            if (csl == null)
                return BadRequest(new { success=false, message=$"IdCoSoLuuTru={idCoSoLuuTru.Value} không tồn tại trong bảng CoSoLuuTru" });
            // Verify ownership: the current user must own this CoSoLuuTru (unless Admin)
            var userIdStr = User.FindFirst("id")?.Value;
            if (!int.TryParse(userIdStr, out var currentUserId))
                return Unauthorized(new { success=false, message="Không xác định được người dùng từ token" });
            int? ownerId = TryGet<int?>(csl, "IdNguoiDung");
            if (!User.IsInRole("Admin") && (!ownerId.HasValue || ownerId.Value != currentUserId))
                return Forbid();
        }

        dynamic dto = new ExpandoObject();
        dto.tenPhong = tenPhong;
        dto.moTa = moTa;
        dto.soNguoiToiDa = soNguoiToiDa;
        dto.idCoSoLuuTru = idCoSoLuuTru;
        dto.idLoaiPhong = idLoaiPhong;
        dto.gia = gia;
        dto.anh = anhPath; // image file path

        var room = await _repo.CreateAsync(dto);
        
        // Add full image URL to response
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var roomData = room as IDictionary<string, object> ?? new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(anhPath))
        {
            roomData["imageUrl"] = $"{baseUrl}{anhPath}";
        }

        return StatusCode(201, new { success=true, message="Tạo phòng thành công", data = roomData });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "ChuCoSo,Admin")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] JsonElement body)
    {
        var room = await _repo.GetByIdAsync(id);
        if (room is null) return NotFound(new { success=false, message="Không tìm thấy phòng" });

        // Verify ownership via CoSoLuuTru
        var userIdStr = User.FindFirst("id")?.Value;
        if (!int.TryParse(userIdStr, out var currentUserId))
            return Unauthorized(new { success=false, message="Không xác định được người dùng từ token" });

        int? idCoSo = TryGet<int?>(room, "IdCoSoLuuTru");
        if (!idCoSo.HasValue) return BadRequest(new { success=false, message="Phòng không có IdCoSoLuuTru" });
        var csl = await _cslRepo.GetByIdAsync(idCoSo.Value);
        int? ownerId = TryGet<int?>(csl!, "IdNguoiDung");
        if (!User.IsInRole("Admin") && (!ownerId.HasValue || ownerId.Value != currentUserId))
            return Forbid();

        // Update room data
        var updatedRoom = await _repo.UpdateAsync(id, body);
        return Ok(new { success=true, message="Cập nhật phòng thành công", data = updatedRoom });
    }

    [HttpPut("{id:int}/image")]
    [Authorize(Roles = "ChuCoSo,Admin")]
    public async Task<IActionResult> UpdateImage([FromRoute] int id, IFormFile file)
    {
        var room = await _repo.GetByIdAsync(id);
        if (room is null) return NotFound(new { success=false, message="Không tìm thấy phòng" });

        // Verify ownership via CoSoLuuTru
        var userIdStr = User.FindFirst("id")?.Value;
        if (!int.TryParse(userIdStr, out var currentUserId))
            return Unauthorized(new { success=false, message="Không xác định được người dùng từ token" });

        int? idCoSo = TryGet<int?>(room, "IdCoSoLuuTru");
        if (!idCoSo.HasValue) return BadRequest(new { success=false, message="Phòng không có IdCoSoLuuTru" });
        var csl = await _cslRepo.GetByIdAsync(idCoSo.Value);
        int? ownerId = TryGet<int?>(csl!, "IdNguoiDung");
        if (!User.IsInRole("Admin") && (!ownerId.HasValue || ownerId.Value != currentUserId))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { success=false, message="Cần upload file ảnh" });

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(new { success = false, message = "Chỉ hỗ trợ file ảnh (JPEG, PNG, GIF, WebP)" });

        // Validate file size (max 10MB)
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { success = false, message = "Kích thước file không được vượt quá 10MB" });

        try
        {
            // Create rooms directory if not exists
            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "rooms");
            if (!Directory.Exists(uploadsPath))
                Directory.CreateDirectory(uploadsPath);

            // Delete old image if exists
            var oldImagePath = TryGet<string>(room, "Anh");
            if (!string.IsNullOrEmpty(oldImagePath))
            {
                var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldImagePath.TrimStart('/'));
                if (System.IO.File.Exists(oldFilePath))
                {
                    System.IO.File.Delete(oldFilePath);
                }
            }

            // Generate unique filename
            var fileExtension = Path.GetExtension(file.FileName);
            var fileName = $"room_{id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
            var filePath = Path.Combine(uploadsPath, fileName);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Save relative path to database
            var relativePath = $"/uploads/rooms/{fileName}";
            await _repo.UpdateImageAsync(id, relativePath);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var response = new
            {
                success = true,
                message = "Đã cập nhật ảnh phòng",
                data = new
                {
                    imagePath = relativePath,
                    imageUrl = $"{baseUrl}{relativePath}",
                    fileName = fileName
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi cập nhật ảnh: " + ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "ChuCoSo,Admin")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var room = await _repo.GetByIdAsync(id);
        if (room is null) return NotFound(new { success=false, message="Không tìm thấy phòng" });

        // Verify ownership via CoSoLuuTru
        var userIdStr = User.FindFirst("id")?.Value;
        if (!int.TryParse(userIdStr, out var currentUserId))
            return Unauthorized(new { success=false, message="Không xác định được người dùng từ token" });

        int? idCoSo = TryGet<int?>(room, "IdCoSoLuuTru");
        if (!idCoSo.HasValue) return BadRequest(new { success=false, message="Phòng không có IdCoSoLuuTru" });
        var csl = await _cslRepo.GetByIdAsync(idCoSo.Value);
        int? ownerId = TryGet<int?>(csl!, "IdNguoiDung");
        if (!User.IsInRole("Admin") && (!ownerId.HasValue || ownerId.Value != currentUserId))
            return Forbid();

        // Check if room has active bookings
        var hasBookings = await _repo.HasActiveBookingsAsync(id);
        if (hasBookings)
            return BadRequest(new { success=false, message="Không thể xóa phòng đang có booking hoạt động" });

        try
        {
            // Delete physical image file if exists
            var imagePath = TryGet<string>(room, "Anh");
            if (!string.IsNullOrEmpty(imagePath))
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", imagePath.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }

            // Delete room from database
            await _repo.DeleteAsync(id);
            return Ok(new { success=true, message="Xóa phòng thành công" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi xóa phòng: " + ex.Message });
        }
    }

    [HttpDelete("{id:int}/image")]
    [Authorize(Roles = "ChuCoSo,Admin")] // owner or admin can remove image
    public async Task<IActionResult> DeleteImage([FromRoute] int id)
    {
        var room = await _repo.GetByIdAsync(id);
        if (room is null) return NotFound(new { success=false, message="Không tìm thấy phòng" });

        // Verify ownership via CoSoLuuTru
        var userIdStr = User.FindFirst("id")?.Value;
        if (!int.TryParse(userIdStr, out var currentUserId))
            return Unauthorized(new { success=false, message="Không xác định được người dùng từ token" });

        int? idCoSo = TryGet<int?>(room, "IdCoSoLuuTru");
        if (!idCoSo.HasValue) return BadRequest(new { success=false, message="Phòng không có IdCoSoLuuTru" });
        var csl = await _cslRepo.GetByIdAsync(idCoSo.Value);
        int? ownerId = TryGet<int?>(csl!, "IdNguoiDung");
        if (!User.IsInRole("Admin") && (!ownerId.HasValue || ownerId.Value != currentUserId))
            return Forbid();

        try
        {
            // Delete physical file if exists
            var oldImagePath = TryGet<string>(room, "Anh");
            if (!string.IsNullOrEmpty(oldImagePath))
            {
                var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldImagePath.TrimStart('/'));
                if (System.IO.File.Exists(oldFilePath))
                {
                    System.IO.File.Delete(oldFilePath);
                }
            }

            // Clear image field in DB
            await _repo.ClearImageAsync(id);
            return Ok(new { success=true, message="Đã xóa ảnh phòng và cập nhật CSDL" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi khi xóa ảnh: " + ex.Message });
        }
    }
    private static T? TryGet<T>(object obj, string name)
    {
        try
        {
            if (obj is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue(name, out var v)) return (T?)v;
            }
            var prop = obj?.GetType().GetProperty(name);
            if (prop != null)
            {
                var v = prop.GetValue(obj, null);
                return (T?)v;
            }
        }
        catch { }
        return default;
    }
}
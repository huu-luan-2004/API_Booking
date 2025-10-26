using HotelBookingApi.Data;
using HotelBookingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Globalization;

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

        // Build response objects with imageUrl, id alias and embedded address (diaChi)
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var tasks = rawData.Select(async item => {
            var itemDict = item as IDictionary<string, object> ?? new Dictionary<string, object>();
            // image url
            var imagePath = item.Anh?.ToString();
            if (!string.IsNullOrEmpty(imagePath))
            {
                itemDict["imageUrl"] = $"{baseUrl}{imagePath}";
            }
            // lowercase id alias
            if (!itemDict.ContainsKey("id") && itemDict.TryGetValue("Id", out var idVal))
            {
                itemDict["id"] = idVal;
            }
            // attach address if available
            try
            {
                if (itemDict.TryGetValue("IdDiaChi", out var idDiaChiObj) && idDiaChiObj != null)
                {
                    if (int.TryParse(idDiaChiObj.ToString(), out var idDiaChi) && idDiaChi > 0)
                    {
                        var addr = await _repo.GetAddressByIdAsync(idDiaChi);
                        if (addr != null)
                        {
                            itemDict["diaChi"] = addr as IDictionary<string, object> ?? new Dictionary<string, object>();
                        }
                    }
                }
            }
            catch { }
            return itemDict;
        });

        var processedData = (await Task.WhenAll(tasks)).ToList();

        return Ok(new { success=true, message="Danh sách cơ sở lưu trú", data = processedData });
    }

    // Reverse geocode: nhận lat/lng (hoặc viDo/kinhDo) và trả về địa chỉ chi tiết
    [HttpGet("reverse-geocode")]
    public async Task<IActionResult> ReverseGeocode([FromQuery] string? lat = null, [FromQuery] string? lng = null,
                                                    [FromQuery] string? viDo = null, [FromQuery] string? kinhDo = null)
    {
        // Chấp nhận cả tham số lat/lng và viDo/kinhDo; parse linh hoạt dấu phẩy
        string? latRaw = lat ?? viDo;
        string? lngRaw = lng ?? kinhDo;
        if (string.IsNullOrWhiteSpace(latRaw) || string.IsNullOrWhiteSpace(lngRaw))
            return BadRequest(new { success = false, message = "Thiếu lat/lng hoặc viDo/kinhDo" });

        if (!double.TryParse(latRaw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var latVal))
            return BadRequest(new { success = false, message = "lat/viDo không hợp lệ" });
        if (!double.TryParse(lngRaw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lngVal))
            return BadRequest(new { success = false, message = "lng/kinhDo không hợp lệ" });

        double? _viDo = latVal; double? _kinhDo = lngVal;
        NormalizeCoordinates(ref _viDo, ref _kinhDo);

        var result = await _mapService.ReverseGeocodeAsync(_viDo!.Value, _kinhDo!.Value);
        if (result == null)
        {
            return Ok(new { success = false, data = new { viDo = _viDo, kinhDo = _kinhDo }, message = "Không tìm thấy địa chỉ phù hợp, vui lòng chọn gần đường/tòa nhà hoặc nhập tay." });
        }

      var diaChi = new {
            chiTiet = (string?)null, // cho người dùng tự nhập
        // Phố/Đường: chỉ hiển thị tên đường (road); nếu không có, có thể dùng số nhà như fallback tối thiểu.
        pho = result.Address?.Road
            ?? result.Address?.HouseNumber,
            // Thôn/xóm (cấp dưới xã): ưu tiên hamlet; nếu thiếu, lấy neighbourhood/quarter
            thon = result.Address?.Hamlet
                   ?? result.Address?.Neighbourhood
                   ?? result.Address?.Quarter,
            // Ưu tiên cấp phường/xã/thị trấn: suburb → town → village → hamlet → neighbourhood → quarter → municipality → city_district
            phuong = result.Address?.Suburb
                     ?? result.Address?.Town
                     ?? result.Address?.Village
                     ?? result.Address?.Hamlet
                     ?? result.Address?.Neighbourhood
                     ?? result.Address?.Quarter
                     ?? result.Address?.Municipality
                     ?? result.Address?.CityDistrict,
            // Thành phố/Tỉnh (không lưu DB, chỉ trả về cho FE hiển thị)
            thanhPho = result.Address?.City ?? result.Address?.Province,
            // Alias tinhThanh để dễ bind trên FE nếu dùng tên khác
            tinhThanh = result.Address?.City ?? result.Address?.Province,
            nuoc = result.Address?.Country,
            displayName = result.DisplayName,
            viDo = _viDo,
            kinhDo = _kinhDo
        };

        return Ok(new { success = true, data = diaChi });
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
        // add lowercase alias for FE convenience
        if (!csData.ContainsKey("id") && csData.TryGetValue("Id", out var idVal))
        {
            csData["id"] = idVal;
        }
        // embed address if present
        try
        {
            if (csData.TryGetValue("IdDiaChi", out var idDiaChiObj) && idDiaChiObj != null)
            {
                if (int.TryParse(idDiaChiObj.ToString(), out var idDiaChi) && idDiaChi > 0)
                {
                    var addr = await _repo.GetAddressByIdAsync(idDiaChi);
                    if (addr != null)
                    {
                        csData["diaChi"] = addr as IDictionary<string, object> ?? new Dictionary<string, object>();
                    }
                }
            }
        }
        catch { }
        
        return Ok(new { success=true, data = csData });
    }

    [Authorize(Roles="ChuCoSo")]
    [HttpPost]
    public async Task<IActionResult> Create()
    {
        string? tenCoSo = null; string? moTa = null; string? soTaiKhoan = null; 
        string? tenTaiKhoan = null; string? tenNganHang = null; string? anhPath = null;
        
        // Thông tin địa chỉ
    string? chiTiet = null; string? pho = null; string? phuong = null; string? nuoc = null;
        double? kinhDo = null; double? viDo = null;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            tenCoSo = form["tenCoSo"].FirstOrDefault();
            moTa = form["moTa"].FirstOrDefault();
            soTaiKhoan = form["soTaiKhoan"].FirstOrDefault();
            tenTaiKhoan = form["tenTaiKhoan"].FirstOrDefault();
            tenNganHang = form["tenNganHang"].FirstOrDefault();

            // Đọc thông tin địa chỉ (schema mới) + tương thích ngược
            chiTiet = form["chiTiet"].FirstOrDefault() ?? form["soNha"].FirstOrDefault();
            // Chỉ nhận phố/đường thật sự (road); không trộn với thôn
            var _ignoredThon = form["thon"].FirstOrDefault();
            pho = form["pho"].FirstOrDefault() ?? form["road"].FirstOrDefault();
            phuong = form["phuong"].FirstOrDefault();
            nuoc = form["nuoc"].FirstOrDefault() ?? form["country"].FirstOrDefault();

            // Parse toạ độ theo InvariantCulture và chấp nhận dấu phẩy
            var kdRaw = form["kinhDo"].FirstOrDefault();
            var vdRaw = form["viDo"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(kdRaw) && double.TryParse(kdRaw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var kdVal))
                kinhDo = kdVal;
            if (!string.IsNullOrWhiteSpace(vdRaw) && double.TryParse(vdRaw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var vdVal))
                viDo = vdVal;

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
        
        // Thử reverse geocode nếu thiếu các trường nhưng có toạ độ
        NormalizeCoordinates(ref viDo, ref kinhDo);
        if ((string.IsNullOrWhiteSpace(pho) || string.IsNullOrWhiteSpace(phuong) || string.IsNullOrWhiteSpace(nuoc))
            && viDo.HasValue && kinhDo.HasValue)
        {
            try
            {
                var rev = await _mapService.ReverseGeocodeAsync(viDo.Value, kinhDo.Value);
                if (rev?.Address != null)
                {
                    // Phố/đường: chỉ lấy road, hoặc tối thiểu số nhà nếu có
                    pho ??= rev.Address.Road
                            ?? rev.Address.HouseNumber;

                    // Phường/xã/thị trấn: suburb → town → village → hamlet → neighbourhood → quarter → municipality → city_district
                    phuong ??= rev.Address.Suburb
                               ?? rev.Address.Town
                               ?? rev.Address.Village
                               ?? rev.Address.Hamlet
                               ?? rev.Address.Neighbourhood
                               ?? rev.Address.Quarter
                               ?? rev.Address.Municipality
                               ?? rev.Address.CityDistrict;
                    nuoc ??= rev.Address.Country;
                }
            }
            catch { }
        }
        // Không bắt buộc thành phố theo schema mới
        
        var idUser = int.TryParse(User?.FindFirst("id")?.Value, out var uid) ? uid : 0;
        
        int? idDiaChi = null;

        // Auto-geocoding nếu chưa có tọa độ
        if (!kinhDo.HasValue || !viDo.HasValue)
        {
            try
            {
                var fullAddress = $"{chiTiet} {pho} {phuong} {nuoc}".Trim().Replace("  ", " ");
                var geocodeResult = await _mapService.GeocodeAsync(fullAddress);
                
                if (geocodeResult != null)
                {
                    kinhDo = geocodeResult.Longitude;
                    viDo = geocodeResult.Latitude;
                    NormalizeCoordinates(ref viDo, ref kinhDo);
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
                chiTiet = chiTiet,
                pho = pho,
                phuong = phuong,
                nuoc = nuoc,
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
        // Address fields (optional on update)
        string? soNha = null; string? phuong = null; string? quan = null; string? thanhPho = null;
        double? kinhDo = null; double? viDo = null;
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

            // Read address fields (may be partial)
            soNha = form["soNha"].FirstOrDefault();
            phuong = form["phuong"].FirstOrDefault();
            quan = form["quan"].FirstOrDefault();
            thanhPho = form["thanhPho"].FirstOrDefault();
            if (double.TryParse(form["kinhDo"].FirstOrDefault(), out var _kinhDo)) kinhDo = _kinhDo;
            if (double.TryParse(form["viDo"].FirstOrDefault(), out var _viDo)) viDo = _viDo;

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

        // If address info provided, upsert DiaChiChiTiet and set idDiaChi accordingly
        var hasAddressPayload = !string.IsNullOrWhiteSpace(soNha)
                                 || !string.IsNullOrWhiteSpace(phuong)
                                 || !string.IsNullOrWhiteSpace(quan)
                                 || !string.IsNullOrWhiteSpace(thanhPho)
                                 || kinhDo.HasValue || viDo.HasValue;

        // Chuyển sang schema mới: chiTiet/pho/phuong/nuoc
        string? u_chiTiet = null, u_pho = null, u_phuong = null, u_nuoc = null;
        if (Request.HasFormContentType)
        {
            // Đọc từ form lần nữa (để tương thích cũ)
            var form = await Request.ReadFormAsync();
            u_chiTiet = form["chiTiet"].FirstOrDefault() ?? form["soNha"].FirstOrDefault();
            var _ignoredUThon = form["thon"].FirstOrDefault();
            u_pho = form["pho"].FirstOrDefault() ?? form["road"].FirstOrDefault(); // chỉ đường phố
            u_phuong = form["phuong"].FirstOrDefault();
            u_nuoc = form["nuoc"].FirstOrDefault() ?? form["country"].FirstOrDefault();
        }

        if (hasAddressPayload)
        {
            // Chuẩn hoá/hoán đổi kinhDo/viDo nếu cần, tránh overflow khi ghi DB numeric
            NormalizeCoordinates(ref viDo, ref kinhDo);

            // Auto-geocoding if missing coords and have textual address
            if ((!kinhDo.HasValue || !viDo.HasValue) && !string.IsNullOrWhiteSpace(thanhPho))
            {
                try
                {
                    var fullAddress = $"{soNha} {phuong}, {quan}, {thanhPho}, Vietnam".Trim().Replace("  ", " ");
                    var geocodeResult = await _mapService.GeocodeAsync(fullAddress);
                    if (geocodeResult != null)
                    {
                        kinhDo = geocodeResult.Longitude;
                        viDo = geocodeResult.Latitude;
                        NormalizeCoordinates(ref viDo, ref kinhDo);
                    }
                }
                catch { /* best-effort */ }
            }

            var addressData = new {
                chiTiet = u_chiTiet,
                pho = u_pho,
                phuong = u_phuong,
                nuoc = u_nuoc,
                kinhDo,
                viDo
            };

            try
            {
                if (idDiaChi.HasValue && idDiaChi.Value > 0)
                {
                    await _repo.UpdateAddressAsync(idDiaChi.Value, addressData);
                }
                else
                {
                    idDiaChi = await _repo.CreateAddressAsync(addressData);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi cập nhật địa chỉ: " + ex.Message });
            }
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

    // Chuẩn hoá toạ độ: đổi dấu phẩy, hoán đổi lat/lon nếu khả nghi, clamp và round 6 chữ số thập phân
    private static void NormalizeCoordinates(ref double? viDo, ref double? kinhDo)
    {
        if (viDo.HasValue && kinhDo.HasValue)
        {
            // Nếu lat nằm ngoài [-90,90] và lon nằm trong [-90,90], có thể bị đảo → hoán đổi
            if ((Math.Abs(viDo.Value) > 90 && Math.Abs(kinhDo.Value) <= 90) ||
                (Math.Abs(viDo.Value) <= 90 && Math.Abs(kinhDo.Value) > 180))
            {
                var t = viDo; viDo = kinhDo; kinhDo = t;
            }
        }

        if (viDo.HasValue)
        {
            var v = Math.Max(-90, Math.Min(90, viDo.Value));
            viDo = Math.Round(v, 6);
        }
        if (kinhDo.HasValue)
        {
            var v = Math.Max(-180, Math.Min(180, kinhDo.Value));
            kinhDo = Math.Round(v, 6);
        }
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

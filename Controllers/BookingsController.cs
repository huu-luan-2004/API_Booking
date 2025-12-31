using HotelBookingApi.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly DatPhongRepository _repo;
    private readonly PhongRepository _rooms;
    private readonly HuyDatPhongRepository _cancelRepo;
    private readonly ThanhToanRepository _payRepo;
    public BookingsController(DatPhongRepository repo, PhongRepository rooms, HuyDatPhongRepository cancelRepo, ThanhToanRepository payRepo)
    {
        _repo = repo; _rooms = rooms; _cancelRepo = cancelRepo; _payRepo = payRepo;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { success=true, message="API đặt phòng hoạt động" });

    // API đơn giản cho Admin - chỉ lấy danh sách booking với thông tin cơ bản
    [HttpGet("admin/simple")]
    public async Task<IActionResult> AdminSimpleList([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try 
        {
            var (bookings, total) = await _repo.ListAsync(page, pageSize, null, null);
            
            return Ok(new { 
                success = true, 
                message = "Danh sách đặt phòng cho Admin (đơn giản)", 
                data = new { 
                    items = bookings.Select(b => new {
                        id = b.Id,
                        idPhong = b.IdPhong,
                        idNguoiDung = b.IdNguoiDung,
                        ngayNhanPhong = b.NgayNhanPhong,
                        ngayTraPhong = b.NgayTraPhong,
                        tongTien = b.TongTien,
                        trangThai = b.TrangThai,
                        ngayDat = b.NgayDat,
                        ghiChu = b.GhiChu
                    }),
                    total, 
                    page, 
                    pageSize
                } 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi server", 
                error = ex.Message,
                stackTrace = ex.StackTrace?.Split('\n').Take(5).ToArray()
            });
        }
    }

    // API lấy chi tiết một booking cho Admin (không cần auth để test)
    [HttpGet("detail/{id}")]
    public async Task<IActionResult> GetBookingDetail(int id)
    {
        try 
        {
            // Sử dụng method GetByIdAsync có sẵn
            var booking = await _repo.GetByIdAsync(id);
            
            if (booking == null)
                return NotFound(new { success = false, message = "Không tìm thấy booking với ID này" });
            
            return Ok(new { 
                success = true, 
                message = "Chi tiết booking", 
                data = new {
                    // Thông tin cơ bản
                    id = booking.Id,
                    idPhong = booking.IdPhong,
                    idNguoiDung = booking.IdNguoiDung,
                    ngayNhanPhong = booking.NgayNhanPhong,
                    ngayTraPhong = booking.NgayTraPhong,
                    tongTien = booking.TongTienTamTinh ?? booking.TongTien,
                    ngayDat = booking.NgayDat,
                    ghiChu = booking.GhiChu?.ToString(),
                    createdAt = booking.CreatedAt,
                    
                    // Thông tin trạng thái
                    trangThai = new {
                        id = booking.IdTrangThai,
                        ma = booking.TT_MaTrangThai,
                        ten = booking.TT_TenTrangThai
                    },
                    
                    // Thông tin khách hàng  
                    khachHang = new {
                        id = booking.IdNguoiDung,
                        hoTen = booking.ND_HoTen,
                        email = booking.ND_Email,
                        soDienThoai = booking.ND_SoDienThoai
                    },
                    
                    // Thông tin phòng
                    phong = new {
                        id = booking.IdPhong,
                        tenPhong = booking.P_TenPhong,
                        loaiPhong = booking.P_LoaiPhong,
                        gia = booking.P_Gia
                    },
                    
                    // Thông tin cơ sở
                    coSoLuuTru = new {
                        tenCoSo = booking.CS_TenCoSo,
                        diaChi = booking.CS_DiaChi
                    }
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi server khi lấy chi tiết booking", 
                error = ex.Message
            });
        }
    }

    // API thống kê cho Admin
    [HttpGet("admin/stats")]
    public async Task<IActionResult> AdminStats()
    {
        try 
        {
            // Lấy thống kê cơ bản
            var statusCounts = await _repo.GetBookingStatusCountsAsync();
            var (allBookings, total) = await _repo.ListAsync(1, 1000); // Lấy tất cả để thống kê
            
            return Ok(new { 
                success = true, 
                message = "Thống kê booking cho Admin", 
                data = new {
                    totalBookings = total,
                    statusCounts = statusCounts,
                    recentBookings = allBookings.Take(5).Select(b => new {
                        id = b.Id,
                        idNguoiDung = b.IdNguoiDung,
                        tongTien = b.TongTien,
                        ngayDat = b.NgayDat,
                        trangThai = b.TrangThai
                    })
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi server khi lấy thống kê", 
                error = ex.Message
            });
        }
    }

    // GET /api/bookings - Lấy danh sách đặt phòng với phân trang
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, [FromQuery] int? userId = null)
    {
        try 
        {
            var (bookings, total) = await _repo.ListAsync(page, pageSize, status, userId);
            
            return Ok(new { 
                success = true, 
                message = "Danh sách đặt phòng", 
                data = new { 
                    items = bookings.Select(b => new {
                        id = b.Id,
                        idPhong = b.IdPhong,
                        idNguoiDung = b.IdNguoiDung,
                        ngayNhanPhong = b.NgayNhanPhong,
                        ngayTraPhong = b.NgayTraPhong,
                        tongTien = b.TongTien,
                        trangThai = b.TrangThai?.ToString(),
                        ngayDat = b.NgayDat,
                        ghiChu = b.GhiChu?.ToString()
                    }),
                    total, 
                    page, 
                    pageSize 
                } 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server khi lấy danh sách đặt phòng", error = ex.Message });
        }
    }

    // DTO để nhận request tạo đặt phòng (tránh dùng dynamic gây lỗi RuntimeBinder với JsonElement)
    public class CreateBookingRequest
    {
        public int IdPhong { get; set; }
        public DateTime NgayNhanPhong { get; set; }
        public DateTime NgayTraPhong { get; set; }
        public decimal? TongTien { get; set; }
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest body)
    {
        // Dọn rác các đơn giữ chỗ đã hết hạn & chưa thanh toán để tránh chiếm lịch
        try { await _repo.PurgeExpiredUnpaidAsync(15); } catch { }
        if (!int.TryParse(User?.FindFirst("id")?.Value, out var idNguoiDung))
            return Unauthorized(new { success=false, message="Vui lòng đăng nhập để đặt phòng" });

        int idPhong = body?.IdPhong ?? 0;
        DateTime ngayNhan = body?.NgayNhanPhong ?? default;
        DateTime ngayTra = body?.NgayTraPhong ?? default;
    decimal tong = body?.TongTien ?? 0m;

        if (idPhong<=0 || ngayNhan==default || ngayTra==default)
            return BadRequest(new { success=false, message="Thiếu thông tin phòng cần đặt (idPhong, ngayNhanPhong, ngayTraPhong)" });

        // Kiểm tra ngày hợp lệ
        if (ngayNhan >= ngayTra)
            return BadRequest(new { success=false, message="Ngày nhận phòng phải trước ngày trả phòng" });
        if (ngayNhan < DateTime.Today)
            return BadRequest(new { success=false, message="Ngày nhận phòng không thể là ngày quá khứ" });

        // Kiểm tra tính khả dụng của phòng (chỉ kiểm tra khi có idPhong hợp lệ)
        var isAvailable = await _repo.CheckAvailabilityAsync(idPhong, ngayNhan, ngayTra);
        if (!isAvailable)
            return BadRequest(new { success=false, message="Phòng không khả dụng trong khoảng thời gian này" });

        // Nếu client không gửi tổng tiền, tự tính theo giá phòng x số đêm
        if (tong <= 0)
        {
            try
            {
                var room = await _rooms.GetByIdAsync(idPhong);
                decimal gia = 0m;
                try { gia = (decimal)(room?.Gia ?? 0m); } catch { }
                var nights = (ngayTra.Date - ngayNhan.Date).Days;
                if (nights <= 0) nights = 1; // tối thiểu 1 đêm
                var calc = gia * nights;
                if (calc > 0) tong = calc;
            }
            catch { /* nếu không lấy được giá, sẽ dùng fallback bên dưới */ }
        }

        if (tong <= 0) tong = 3000000m; // fallback cuối cùng để không chặn luồng

        int id;
        try
        {
            id = await _repo.CreateAsync(idNguoiDung, idPhong, ngayNhan, ngayTra, tong);
        }
        catch (Exception ex)
        {
            var msg = ex.Message ?? string.Empty;
            var detail = (ex.InnerException?.Message ?? string.Empty);
            var all = string.Join(" | ", new[]{ msg, detail }).ToLowerInvariant();
            // Nếu phòng vừa bị giữ/đặt trong khoảng thời gian này
            if (msg.Contains("giữ/đặt", StringComparison.OrdinalIgnoreCase) || msg.Contains("trùng", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { success=false, errorCode = "BOOKING_CONFLICT", message = msg });

            // Nếu không thể khóa tài nguyên (hết timeout)
            if (all.Contains("khoá tài nguyên") || all.Contains("khóa tài nguyên") || all.Contains("timeout") || all.Contains("deadlock") || all.Contains("sp_getapplock"))
                return StatusCode(503, new { success=false, errorCode = "BOOKING_LOCK_TIMEOUT", message = "Hệ thống đang bận xử lý đặt phòng khác cho phòng này, vui lòng thử lại sau." });

            return StatusCode(500, new { success=false, errorCode = "BOOKING_CREATE_ERROR", message = "Có lỗi xảy ra khi tạo đặt phòng", detail = msg });
        }
        var dp = await _repo.GetByIdAsync(id);
        // Nếu không lấy được đầy đủ join dữ liệu, vẫn trả về dữ liệu tối thiểu để FE tiếp tục luồng
        if (dp == null)
        {
            var createdAtFallback = DateTime.Now;
            var responseLite = new {
                Id = id,
                IdNguoiDung = idNguoiDung,
                IdPhong = idPhong,
                NgayNhanPhong = ngayNhan,
                NgayTraPhong = ngayTra,
                TongTienTamTinh = tong,
                createdAt = createdAtFallback,
                holdExpiresAt = createdAtFallback.AddMinutes(15),
                trangThai = new { ma = "ChoThanhToanCoc", ten = "Chờ thanh toán cọc" }
            };
            return StatusCode(201, new { success=true, message="Đặt phòng thành công", data = responseLite });
        }

        DateTime? createdAt = null;
        try { createdAt = (DateTime?)dp.CreatedAt; } catch { }
        var holdExpiresAt = createdAt?.AddMinutes(15);
        var response = new {
            dp.Id,
            dp.IdNguoiDung,
            dp.IdPhong,
            dp.NgayNhanPhong,
            dp.NgayTraPhong,
            dp.TongTienTamTinh,
            createdAt,
            holdExpiresAt,
            nguoiDung = new { id = dp.IdNguoiDung, hoTen = dp.ND_HoTen, email = dp.ND_Email, soDienThoai = dp.ND_SoDienThoai },
            phong = new { id = dp.IdPhong, tenPhong = dp.P_TenPhong },
            coSo = new { tenCoSo = dp.CS_TenCoSo },
            trangThai = new { id = dp.IdTrangThai, ma = dp.TT_MaTrangThai, ten = dp.TT_TenTrangThai }
        };
        return StatusCode(201, new { success=true, message="Đặt phòng thành công", data = response });
    }

    // Admin: chạy dọn rác đơn chưa thanh toán (hết hạn giữ chỗ)
    [Authorize(Roles="Admin")]
    [HttpPost("cleanup-unpaid")]
    public async Task<IActionResult> CleanupUnpaid([FromQuery] int minutes = 15)
    {
        try
        {
            var affected = await _repo.PurgeExpiredUnpaidAsync(minutes);
            return Ok(new { success = true, message = "Đã xoá đơn chưa thanh toán hết hạn", data = new { affected, minutes } });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success=false, message="Lỗi server khi dọn rác đơn chưa thanh toán", error = ex.Message });
        }
    }

    [Authorize]
    [HttpGet("user")]
    public async Task<IActionResult> UserBookings()
    {
        int idNguoiDung = int.Parse(User.FindFirst("id")!.Value);
        var bookings = await _repo.ListByUserAsync(idNguoiDung);
        return Ok(new { success=true, message="Lấy danh sách đặt phòng thành công", data = bookings });
    }

    // API chuyên dụng cho Admin để xem danh sách đặt phòng với đầy đủ thông tin
    // [Authorize(Roles = "Admin")] // Tạm comment để test
    [HttpGet("admin/all")]
    public async Task<IActionResult> AdminGetAllBookings([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? status = null, [FromQuery] int? userId = null)
    {
        try 
        {
            // Sử dụng ListAsync đơn giản trước để đảm bảo hoạt động
            var (bookings, total) = await _repo.ListAsync(page, pageSize, status, userId);
            
            return Ok(new { 
                success = true, 
                message = "Danh sách đặt phòng cho Admin", 
                data = new { 
                    items = bookings.Select(b => new {
                        // Thông tin đặt phòng cơ bản
                        id = b.Id,
                        idPhong = b.IdPhong,
                        idNguoiDung = b.IdNguoiDung,
                        ngayNhanPhong = b.NgayNhanPhong,
                        ngayTraPhong = b.NgayTraPhong,
                        tongTien = b.TongTien,
                        ngayDat = b.NgayDat,
                        trangThaiId = b.TrangThai,
                        ghiChu = b.GhiChu?.ToString()
                    }),
                    total, 
                    page, 
                    pageSize,
                    summary = new {
                        totalBookings = total,
                        statusCounts = await _repo.GetBookingStatusCountsAsync()
                    }
                } 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server khi lấy danh sách đặt phòng cho Admin", error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    // API cho Admin để xem chi tiết một booking cụ thể
    // [Authorize(Roles = "Admin")] // Tạm comment để test
    [HttpGet("admin/{id}")]
    public async Task<IActionResult> AdminGetBookingDetail(int id)
    {
        try
        {
            var booking = await _repo.GetByIdWithFullDetailsAsync(id);
            if (booking == null)
                return NotFound(new { success = false, message = "Không tìm thấy đặt phòng" });

            // Lấy thêm lịch sử thanh toán
            var payments = await _payRepo.GetByBookingIdAsync(id);
            
            var result = new {
                // Thông tin đặt phòng
                id = booking.Id,
                idPhong = booking.IdPhong,
                idNguoiDung = booking.IdNguoiDung,
                ngayNhanPhong = booking.NgayNhanPhong,
                ngayTraPhong = booking.NgayTraPhong,
                tongTien = booking.TongTien,
                ngayDat = booking.NgayDat,
                ghiChu = booking.GhiChu?.ToString(),
                createdAt = booking.CreatedAt,
                updatedAt = booking.UpdatedAt,
                
                // Thông tin trạng thái
                trangThai = new {
                    id = booking.IdTrangThai,
                    ma = booking.TT_MaTrangThai,
                    ten = booking.TT_TenTrangThai
                },
                
                // Thông tin khách hàng đầy đủ
                khachHang = new {
                    id = booking.IdNguoiDung,
                    hoTen = booking.ND_HoTen,
                    email = booking.ND_Email,
                    soDienThoai = booking.ND_SoDienThoai,
                    avatar = booking.ND_Avatar,
                    ngayTao = booking.ND_NgayTao,
                    trangThai = booking.ND_TrangThai
                },
                
                // Thông tin phòng đầy đủ
                phong = new {
                    id = booking.IdPhong,
                    tenPhong = booking.P_TenPhong,
                    loaiPhong = booking.P_LoaiPhong,
                    dienTich = booking.P_DienTich,
                    soLuongKhach = booking.P_SoLuongKhach,
                    gia = booking.P_Gia,
                    moTa = booking.P_MoTa,
                    hinhAnh = booking.P_HinhAnh
                },
                
                // Thông tin cơ sở lưu trú đầy đủ
                coSoLuuTru = new {
                    id = booking.IdCoSoLuuTru,
                    tenCoSo = booking.CS_TenCoSo,
                    diaChi = booking.CS_DiaChi,
                    soDienThoai = booking.CS_SoDienThoai,
                    email = booking.CS_Email,
                    website = booking.CS_Website,
                    moTa = booking.CS_MoTa
                },
                
                // Lịch sử thanh toán
                lichSuThanhToan = payments?.Select(p => new {
                    id = p.Id,
                    soTien = p.SoTien,
                    phuongThuc = p.PhuongThuc ?? p.PhuongThucThanhToan,
                    ngayThanhToan = p.NgayThanhToan,
                    trangThai = p.TrangThai,
                    maGiaoDich = p.MaGiaoDich ?? p.MaGiaoDichVnPay,
                    ghiChu = p.GhiChu
                }).ToArray() ?? new object[0]
            };

            return Ok(new { success = true, message = "Chi tiết đặt phòng", data = result });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server khi lấy chi tiết đặt phòng", error = ex.Message });
        }
    }

    [HttpGet("check-availability")]
    public async Task<IActionResult> CheckAvailability([FromQuery] int idPhong, [FromQuery] DateTime ngayNhanPhong, [FromQuery] DateTime ngayTraPhong)
    {
        if (ngayNhanPhong==default || ngayTraPhong==default)
            return BadRequest(new { success=false, message="Vui lòng cung cấp ngày nhận phòng và ngày trả phòng hợp lệ" });
        
        if (idPhong < 0)
            return BadRequest(new { success=false, message="ID phòng không hợp lệ" });
            
        if (ngayNhanPhong >= ngayTraPhong)
            return BadRequest(new { success=false, message="Ngày nhận phòng phải trước ngày trả phòng" });
            
        // Nếu idPhong = 0, có nghĩa là kiểm tra tính khả dụng chung (không cần phòng cụ thể)
        if (idPhong == 0)
        {
            return Ok(new { success=true, data = new { available = true, message = "Có thể đặt phòng trong khoảng thời gian này" } });
        }
        
        var ok = await _repo.CheckAvailabilityAsync(idPhong, ngayNhanPhong, ngayTraPhong);
        return Ok(new { success=true, data = new { available = ok } });
    }

    [Authorize]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var dp = await _repo.GetByIdAsync(id);
        if (dp is null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });
        int idNguoiDung = int.Parse(User.FindFirst("id")!.Value);
        var roles = User.Claims.Where(c => c.Type==System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        if ((int)dp.IdNguoiDung != idNguoiDung && !roles.Contains("Admin"))
            return StatusCode(403, new { success=false, message="Không có quyền truy cập vào đơn đặt phòng này" });
        // Thanh toán/hủy có thể bổ sung sau
        return Ok(new { success=true, message="Lấy thông tin đặt phòng thành công", data = dp });
    }

    public class UpdateStatusRequest
    {
        public string maTrangThai { get; set; } = string.Empty; // Ví dụ: "DaCoc", "DaThanhToanDayDu", "ChoCheckIn", ...
    }

    // Cập nhật trạng thái đơn: cho phép Admin; hoặc chủ đơn (một số trạng thái an toàn như YEU_CAU_HUY)
    [Authorize]
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus([FromRoute] int id, [FromBody] UpdateStatusRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.maTrangThai))
            return BadRequest(new { success=false, message="Thiếu maTrangThai" });

        var allowed = new HashSet<string>(new[]{
            "ChoThanhToanCoc","DaCoc","ChoCheckIn","DaThanhToanDayDu",
            "HuyPhong","NoShow","DaNhanPhong","HoanTat","YEU_CAU_HUY"
        }, StringComparer.OrdinalIgnoreCase);
        if (!allowed.Contains(body.maTrangThai))
            return BadRequest(new { success=false, message="maTrangThai không hợp lệ", allowed });

        // Kiểm tra quyền: Admin luôn được phép. Chủ đơn chỉ được YEU_CAU_HUY.
        var dp = await _repo.GetByIdAsync(id);
        if (dp is null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });
        var roles = User.Claims.Where(c => c.Type==System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        var isAdmin = roles.Contains("Admin");
        var userIdStr = User.FindFirst("id")?.Value;
        int.TryParse(userIdStr, out var authId);
        var isOwner = (int)dp.IdNguoiDung == authId;

        if (!isAdmin && !(isOwner && body.maTrangThai.Equals("YEU_CAU_HUY", StringComparison.OrdinalIgnoreCase)))
            return StatusCode(403, new { success=false, message="Bạn không có quyền cập nhật trạng thái này" });

        await _repo.UpdateTrangThaiAsync(id, body.maTrangThai);
        var updated = await _repo.GetByIdAsync(id);
        return Ok(new { success=true, message="Cập nhật trạng thái thành công", data = updated });
    }

    public class CancelBookingRequest
    {
        public string? lyDo { get; set; }
        public bool simulateRefund { get; set; } = true; // mô phỏng hoàn tiền ngay
    }

    [Authorize]
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelBooking([FromRoute] int id, [FromBody] CancelBookingRequest? body)
    {
        var dp = await _repo.GetByIdAsync(id);
        if (dp is null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });

        // Quyền: chủ đơn hoặc Admin
        var roles = User.Claims.Where(c => c.Type==System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        var isAdmin = roles.Contains("Admin");
        int.TryParse(User.FindFirst("id")?.Value, out var authId);
        var isOwner = (int)dp.IdNguoiDung == authId;
        if (!isAdmin && !isOwner)
            return StatusCode(403, new { success=false, message="Bạn không có quyền hủy đơn này" });

        // Tính toán tiền hoàn/phạt theo chính sách (>=24h: 0%, 12..24h: 30%, <12h: 100%)
        var calc = await _cancelRepo.TinhTienHoanTraVaTienPhatAsync(id);
        var lyDo = string.IsNullOrWhiteSpace(body?.lyDo) ? "Khách hàng yêu cầu hủy" : body!.lyDo!;

        // Tạo bản ghi hủy (trạng thái ban đầu: Chờ xử lý)
        var cancelId = await _cancelRepo.CreateAsync(id, lyDo, calc.tienHoanLai, calc.tienPhat, "Chờ xử lý", authId);

        // Cập nhật trạng thái đơn sang HuyPhong
        await _repo.UpdateTrangThaiAsync(id, "HuyPhong");

        object? refundTx = null;
        if (calc.tienHoanLai > 0)
        {
            var refundCode = $"RF{DateTime.UtcNow.Ticks}";
            var noiDung = $"Hoàn tiền hủy đặt phòng #{id}. Tiền phạt: {calc.tienPhat:N0}";
            refundTx = await _payRepo.CreateRefundAsync(id, refundCode, calc.tienHoanLai, noiDung, $"DP{id}");

            if (body?.simulateRefund ?? true)
            {
                var meta = JsonSerializer.Serialize(new { cancelId, penalty = calc.tienPhat, refundable = calc.tienHoanLai });
                await _payRepo.UpdateTrangThaiAsync(refundCode, "Thành công", meta);
                await _cancelRepo.UpdateTrangThaiAsync(cancelId, "Đã hoàn", "Hoàn tiền VNPAY (mô phỏng) thành công");
            }
        }
        else
        {
            await _cancelRepo.UpdateTrangThaiAsync(cancelId, "Không hoàn", "Không có số tiền cần hoàn");
        }

        var updated = await _repo.GetByIdAsync(id);
        return Ok(new {
            success = true,
            message = "Hủy đặt phòng thành công",
            data = new {
                datPhong = updated,
                huy = new { id = cancelId, lyDo, tienHoanLai = calc.tienHoanLai, tienPhat = calc.tienPhat, soGioConLai = calc.soGioConLai },
                refund = refundTx
            }
        });
    }

    [Authorize]
    [HttpGet("{id:int}/payments")]
    public async Task<IActionResult> ListPayments([FromRoute] int id)
    {
        var dp = await _repo.GetByIdAsync(id);
        if (dp is null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });

        // Quyền: chủ đơn hoặc Admin
        var roles = User.Claims.Where(c => c.Type==System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        var isAdmin = roles.Contains("Admin");
        int.TryParse(User.FindFirst("id")?.Value, out var authId);
        var isOwner = (int)dp.IdNguoiDung == authId;
        if (!isAdmin && !isOwner)
            return StatusCode(403, new { success=false, message="Bạn không có quyền xem lịch sử thanh toán của đơn này" });

        var rows = await _payRepo.ListByBookingAsync(id);

        // Chuẩn hóa dữ liệu trả về để FE dễ dùng, không phụ thuộc schema cột tùy biến
        var items = new List<object>();
        foreach (var r in rows)
        {
            try
            {
                if (r is System.Collections.Generic.IDictionary<string, object> d)
                {
                    d.TryGetValue("Id", out var _id);
                    d.TryGetValue("MaGiaoDich", out var _magd);
                    d.TryGetValue("SoTien", out var _tien);
                    d.TryGetValue("PhuongThuc", out var _pm);
                    d.TryGetValue("TrangThai", out var _tt);
                    d.TryGetValue("NoiDung", out var _nd);
                    d.TryGetValue("MaDonHang", out var _mdh);
                    d.TryGetValue("LoaiGiaoDich", out var _lgd);
                    d.TryGetValue("CreatedAt", out var _created);

                    var loai = (_lgd?.ToString() ?? string.Empty);
                    var pm = (_pm?.ToString() ?? string.Empty);
                    var isRefund = loai.Equals("Hoàn tiền", StringComparison.OrdinalIgnoreCase) || pm.Contains("Refund", StringComparison.OrdinalIgnoreCase);

                    items.Add(new {
                        id = _id,
                        maGiaoDich = _magd,
                        soTien = _tien,
                        phuongThuc = _pm,
                        trangThai = _tt,
                        noiDung = _nd,
                        maDonHang = _mdh,
                        loaiGiaoDich = _lgd,
                        createdAt = _created,
                        isRefund
                    });
                }
                else
                {
                    // Fallback: trả nguyên r nếu không cast được
                    items.Add(r);
                }
            }
            catch
            {
                items.Add(r);
            }
        }

        return Ok(new { success=true, data = new { items, total = items.Count } });
    }

    // Admin utility: tính lại trạng thái đơn dựa theo số tiền đã thanh toán
    [Authorize(Roles="Admin")]
    [HttpPost("{id:int}/recompute-status")]
    public async Task<IActionResult> RecomputeStatus([FromRoute] int id)
    {
        var dp = await _repo.GetByIdAsync(id);
        if (dp is null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });

        // Tính tổng và số đã trả
        decimal tong = 0m;
        try { tong = (decimal)(dp?.TongTienTamTinh ?? 0m); } catch {}
        if (tong == 0m) { try { tong = (decimal)(dp?.TongTien ?? 0m); } catch {} }
        var paid = await _payRepo.GetTongDaThanhToanAsync(id);

        if (tong > 0m)
        {
            if (paid >= tong)
                await _repo.UpdateTrangThaiAsync(id, "DaThanhToanDayDu");
            else if (paid > 0m)
                await _repo.UpdateTrangThaiAsync(id, "DaCoc");
            else
                await _repo.UpdateTrangThaiAsync(id, "ChoThanhToanCoc");
        }

        var updated = await _repo.GetByIdAsync(id);
        return Ok(new { success=true, message="Đã tính lại trạng thái theo giao dịch thanh toán", data = updated, paid, tong });
    }
}

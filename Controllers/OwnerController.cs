using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Dapper;

namespace HotelBookingApi.Controllers;

/// <summary>
/// Controller for accommodation owners (ChuCoSo) to manage their properties and bookings
/// </summary>
[ApiController]
[Route("api/owner")]
[Authorize(Roles = "ChuCoSo,Admin")]
public class OwnerController : ControllerBase
{
    private readonly CoSoLuuTruRepository _accommodationRepo;
    private readonly DatPhongRepository _bookingRepo;
    private readonly PhongRepository _roomRepo;
    private readonly NguoiDungRepository _userRepo;
    private readonly SqlConnectionFactory _connectionFactory;

    public OwnerController(
        CoSoLuuTruRepository accommodationRepo,
        DatPhongRepository bookingRepo,
        PhongRepository roomRepo,
        NguoiDungRepository userRepo,
        SqlConnectionFactory connectionFactory)
    {
        _accommodationRepo = accommodationRepo;
        _bookingRepo = bookingRepo;
        _roomRepo = roomRepo;
        _userRepo = userRepo;
        _connectionFactory = connectionFactory;
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.FindFirst("id")?.Value ?? "0");
    }

    /// <summary>
    /// Dashboard for accommodation owner
    /// GET /api/owner/dashboard
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            var ownerId = GetCurrentUserId();
            
            // Get owner's accommodations
            var accommodations = await _accommodationRepo.ListAsync(1, 100, null, true, ownerId);
            var accommodationIds = accommodations.Select(a => (int)a.Id).ToList();

            // Get total bookings for owner's accommodations  
            var (allBookings, totalBookings) = await _bookingRepo.ListAsync(1, 1000, null, null);
            var ownerBookings = allBookings.Where(b => {
                var roomId = (int?)b.IdPhong;
                return roomId.HasValue && accommodationIds.Any(accId => 
                    _roomRepo.GetByIdAsync(roomId.Value).Result?.IdCoSoLuuTru == accId);
            }).ToList();

            // Get booking status counts
            var statusCounts = ownerBookings.GroupBy(b => (int?)b.IdTrangThai)
                .Select(g => new { 
                    statusId = g.Key ?? 0, 
                    count = g.Count() 
                }).ToList();

            // Get today's check-ins and check-outs
            var today = DateTime.Today;
            var todayCheckIns = ownerBookings.Where(b => 
                ((DateTime?)b.NgayNhanPhong)?.Date == today && 
                (int?)b.IdTrangThai == 3 // ChoCheckIn
            ).Count();

            var todayCheckOuts = ownerBookings.Where(b => 
                ((DateTime?)b.NgayTraPhong)?.Date == today && 
                (int?)b.IdTrangThai == 7 // DaNhanPhong
            ).Count();

            return Ok(new
            {
                success = true,
                message = "Dashboard chủ cơ sở",
                data = new
                {
                    totalAccommodations = accommodations.Count(),
                    totalBookings = ownerBookings.Count,
                    todayCheckIns = todayCheckIns,
                    todayCheckOuts = todayCheckOuts,
                    statusCounts = statusCounts,
                    recentBookings = ownerBookings.Take(5).Select(b => new {
                        id = b.Id,
                        roomId = b.IdPhong,
                        guestId = b.IdNguoiDung,
                        checkIn = b.NgayNhanPhong,
                        checkOut = b.NgayTraPhong,
                        status = b.IdTrangThai,
                        total = b.TongTienTamTinh
                    })
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi tải dashboard: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get all bookings for owner's accommodations
    /// GET /api/owner/bookings
    /// </summary>
    [HttpGet("bookings")]
    public async Task<IActionResult> GetBookings(
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? date = null)
    {
        try
        {
            var ownerId = GetCurrentUserId();
            
            // Get owner's accommodations first
            var accommodations = await _accommodationRepo.ListAsync(1, 1000, null, true, ownerId);
            var accommodationIds = accommodations.Select(a => (int)a.Id).ToList();

            if (!accommodationIds.Any())
            {
                return Ok(new
                {
                    success = true,
                    message = "Danh sách đặt phòng",
                    data = new { items = new List<object>(), total = 0, page, pageSize }
                });
            }

            // Get all bookings and filter for owner's rooms
            var (allBookings, _) = await _bookingRepo.ListAsync(1, 10000, null, null);
            var ownerBookings = new List<dynamic>();

            foreach (var booking in allBookings)
            {
                var roomId = (int?)booking.IdPhong;
                if (roomId.HasValue)
                {
                    var room = await _roomRepo.GetByIdAsync(roomId.Value);
                    if (room != null && accommodationIds.Contains((int)room.IdCoSoLuuTru))
                    {
                        var guest = await _userRepo.GetByIdAsync((int)booking.IdNguoiDung);
                        
                        ownerBookings.Add(new
                        {
                            id = booking.Id,
                            roomId = booking.IdPhong,
                            room = new
                            {
                                id = room.Id,
                                name = room.TenPhong?.ToString(),
                                accommodationId = room.IdCoSoLuuTru
                            },
                            guest = new
                            {
                                id = guest?.Id,
                                name = guest?.HoTen?.ToString(),
                                email = guest?.Email?.ToString(),
                                phone = guest?.SoDienThoai?.ToString()
                            },
                            checkInDate = booking.NgayNhanPhong,
                            checkOutDate = booking.NgayTraPhong,
                            bookingDate = booking.NgayDat,
                            statusId = booking.IdTrangThai,
                            total = booking.TongTienTamTinh,
                            createdAt = booking.CreatedAt
                        });
                    }
                }
            }

            // Apply filters
            if (!string.IsNullOrEmpty(status) && int.TryParse(status, out var statusId))
            {
                ownerBookings = ownerBookings.Where(b => (int)b.statusId == statusId).ToList();
            }

            if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var filterDate))
            {
                ownerBookings = ownerBookings.Where(b => 
                    ((DateTime?)b.checkInDate)?.Date == filterDate.Date ||
                    ((DateTime?)b.checkOutDate)?.Date == filterDate.Date
                ).ToList();
            }

            // Pagination
            var total = ownerBookings.Count;
            var items = ownerBookings.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new
            {
                success = true,
                message = "Danh sách đặt phòng",
                data = new { items, total, page, pageSize }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi tải danh sách đặt phòng: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Check-in a guest (change status from ChoCheckIn to DaNhanPhong)
    /// PUT /api/owner/bookings/{id}/checkin
    /// </summary>
    [HttpPut("bookings/{id}/checkin")]
    public async Task<IActionResult> CheckIn(int id, [FromBody] CheckInRequest? request)
    {
        try
        {
            var ownerId = GetCurrentUserId();
            
            // Verify booking exists and belongs to owner
            var booking = await _bookingRepo.GetByIdAsync(id);
            if (booking == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy đặt phòng" });
            }

            // Verify ownership through room -> accommodation
            var roomId = (int?)booking.IdPhong;
            if (!roomId.HasValue)
            {
                return BadRequest(new { success = false, message = "Đặt phòng không có thông tin phòng" });
            }

            var room = await _roomRepo.GetByIdAsync(roomId.Value);
            if (room == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy phòng" });
            }

            var accommodation = await _accommodationRepo.GetByIdAsync((int)room.IdCoSoLuuTru);
            if (accommodation == null || (int?)accommodation.IdNguoiDung != ownerId)
            {
                return Forbid("Bạn không có quyền thực hiện check-in cho đặt phòng này");
            }

            // Check if booking is in correct status (ChoCheckIn = 3 or DaThanhToanDayDu = 4)
            var currentStatus = (int?)booking.IdTrangThai;
            if (currentStatus != 3 && currentStatus != 4)
            {
                return BadRequest(new { 
                    success = false, 
                    message = "Đặt phòng phải ở trạng thái 'Chờ check-in' hoặc 'Đã thanh toán đầy đủ' mới có thể check-in"
                });
            }

            // Update status to DaNhanPhong (7)
            await _bookingRepo.UpdateTrangThaiAsync(id, 7);

            // Log the check-in activity
            var checkInNote = $"Check-in bởi chủ cơ sở lúc {DateTime.Now:dd/MM/yyyy HH:mm}";
            if (!string.IsNullOrEmpty(request?.notes))
            {
                checkInNote += $". Ghi chú: {request.notes}";
            }

            return Ok(new
            {
                success = true,
                message = "Check-in thành công",
                data = new
                {
                    bookingId = id,
                    newStatus = 7,
                    statusName = "Đã nhận phòng",
                    checkInTime = DateTime.Now,
                    notes = checkInNote
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi check-in: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Check-out a guest (change status from DaNhanPhong to HoanTat)
    /// PUT /api/owner/bookings/{id}/checkout
    /// </summary>
    [HttpPut("bookings/{id}/checkout")]
    public async Task<IActionResult> CheckOut(int id, [FromBody] CheckOutRequest? request)
    {
        try
        {
            var ownerId = GetCurrentUserId();
            
            // Verify booking exists and belongs to owner
            var booking = await _bookingRepo.GetByIdAsync(id);
            if (booking == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy đặt phòng" });
            }

            // Verify ownership through room -> accommodation
            var roomId = (int?)booking.IdPhong;
            if (!roomId.HasValue)
            {
                return BadRequest(new { success = false, message = "Đặt phòng không có thông tin phòng" });
            }

            var room = await _roomRepo.GetByIdAsync(roomId.Value);
            if (room == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy phòng" });
            }

            var accommodation = await _accommodationRepo.GetByIdAsync((int)room.IdCoSoLuuTru);
            if (accommodation == null || (int?)accommodation.IdNguoiDung != ownerId)
            {
                return Forbid("Bạn không có quyền thực hiện check-out cho đặt phòng này");
            }

            // Check if booking is in correct status (DaNhanPhong = 7)
            var currentStatus = (int?)booking.IdTrangThai;
            if (currentStatus != 7)
            {
                return BadRequest(new { 
                    success = false, 
                    message = "Đặt phòng phải ở trạng thái 'Đã nhận phòng' mới có thể check-out"
                });
            }

            // Update status to HoanTat (8)
            await _bookingRepo.UpdateTrangThaiAsync(id, 8);

            // Log the check-out activity
            var checkOutNote = $"Check-out bởi chủ cơ sở lúc {DateTime.Now:dd/MM/yyyy HH:mm}";
            if (!string.IsNullOrEmpty(request?.notes))
            {
                checkOutNote += $". Ghi chú: {request.notes}";
            }

            return Ok(new
            {
                success = true,
                message = "Check-out thành công",
                data = new
                {
                    bookingId = id,
                    newStatus = 8,
                    statusName = "Hoàn tất",
                    checkOutTime = DateTime.Now,
                    notes = checkOutNote
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi check-out: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get owner's accommodations
    /// GET /api/owner/accommodations
    /// </summary>
    [HttpGet("accommodations")]
    public async Task<IActionResult> GetAccommodations()
    {
        try
        {
            var ownerId = GetCurrentUserId();
            var accommodations = await _accommodationRepo.ListAsync(1, 1000, null, true, ownerId);

            var result = accommodations.Select(a => new
            {
                id = a.Id,
                name = a.TenCoSo?.ToString(),
                type = a.LoaiCoSo?.ToString(),
                address = a.DiaChi?.ToString(),
                description = a.MoTa?.ToString(),
                rating = a.XepHang,
                isApproved = a.DaDuyet,
                isActive = GetAccommodationStatus(a), // Trạng thái hoạt động: true=hoạt động, false=khóa
                status = GetAccommodationStatusText(a), // Text mô tả trạng thái
                createdAt = a.NgayTao,
                imageUrl = BuildImageUrl(a.AnhDaiDien?.ToString())
            });

            return Ok(new
            {
                success = true,
                message = "Danh sách cơ sở lưu trú",
                data = new
                {
                    items = result,
                    total = accommodations.Count()
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi tải danh sách cơ sở: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get rooms for a specific accommodation
    /// GET /api/owner/accommodations/{id}/rooms
    /// </summary>
    [HttpGet("accommodations/{accommodationId}/rooms")]
    public async Task<IActionResult> GetRooms(int accommodationId)
    {
        try
        {
            var ownerId = GetCurrentUserId();
            
            // Verify ownership
            var accommodation = await _accommodationRepo.GetByIdAsync(accommodationId);
            if (accommodation == null || (int?)accommodation.IdNguoiDung != ownerId)
            {
                return Forbid("Bạn không có quyền xem phòng của cơ sở này");
            }

            // Get rooms for this accommodation
            using var db = _connectionFactory.Create();
            var rooms = await db.QueryAsync("SELECT * FROM Phong WHERE IdCoSoLuuTru = @id", new { id = accommodationId });

            var result = rooms.Select(r => new
            {
                id = r.Id,
                name = r.TenPhong?.ToString(),
                type = r.LoaiPhong?.ToString(),
                price = r.GiaPhong,
                capacity = r.SoNguoi,
                area = r.DienTich,
                description = r.MoTa?.ToString(),
                amenities = r.TienNghi?.ToString(),
                isAvailable = r.TinhTrang,
                imageUrl = BuildImageUrl(r.AnhDaiDien?.ToString())
            });

            return Ok(new
            {
                success = true,
                message = "Danh sách phòng",
                data = new
                {
                    accommodationId = accommodationId,
                    items = result,
                    total = rooms.Count()
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi tải danh sách phòng: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get revenue statistics for owner's accommodations
    /// GET /api/owner/revenue
    /// </summary>
    [HttpGet("revenue")]
    public async Task<IActionResult> GetRevenue(
        [FromQuery] string? period = "month", // day, week, month, year
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null)
    {
        try
        {
            var ownerId = GetCurrentUserId();
            
            // Get owner's accommodations
            var accommodations = await _accommodationRepo.ListAsync(1, 1000, null, true, ownerId);
            var accommodationIds = accommodations.Select(a => (int)a.Id).ToList();

            if (!accommodationIds.Any())
            {
                return Ok(new
                {
                    success = true,
                    message = "Thống kê doanh thu",
                    data = new
                    {
                        totalRevenue = 0,
                        totalBookings = 0,
                        averageBookingValue = 0,
                        revenueByPeriod = new List<object>(),
                        revenueByAccommodation = new List<object>()
                    }
                });
            }

            // Calculate date range based on period
            var (startDate, endDate) = CalculateDateRange(period, fromDate, toDate);

            // Get all bookings for owner's accommodations in date range
            var (allBookings, _) = await _bookingRepo.ListAsync(1, 10000, null, null);
            var revenueBookings = new List<dynamic>();

            using var db = _connectionFactory.Create();
            
            foreach (var booking in allBookings)
            {
                var roomId = (int?)booking.IdPhong;
                if (roomId.HasValue)
                {
                    var room = await _roomRepo.GetByIdAsync(roomId.Value);
                    if (room != null && accommodationIds.Contains((int)room.IdCoSoLuuTru))
                    {
                        var bookingDate = (DateTime?)booking.NgayDat;
                        var statusId = (int?)booking.IdTrangThai;
                        
                        // Only count completed bookings (status 8 = HoanTat)
                        if (bookingDate.HasValue && bookingDate.Value.Date >= startDate.Date && 
                            bookingDate.Value.Date <= endDate.Date && statusId == 8)
                        {
                            revenueBookings.Add(booking);
                        }
                    }
                }
            }

            // Calculate statistics
            var totalRevenue = revenueBookings.Sum(b => (decimal?)b.TongTienTamTinh ?? 0);
            var totalBookings = revenueBookings.Count;
            var averageBookingValue = totalBookings > 0 ? totalRevenue / totalBookings : 0;

            // Revenue by period (daily, weekly, monthly breakdown)
            var revenueByPeriod = GroupRevenueByPeriod(revenueBookings, period);

            // Revenue by accommodation
            var revenueByAccommodation = await GetRevenueByAccommodation(revenueBookings, accommodations);

            return Ok(new
            {
                success = true,
                message = "Thống kê doanh thu",
                data = new
                {
                    period = period,
                    fromDate = startDate.ToString("yyyy-MM-dd"),
                    toDate = endDate.ToString("yyyy-MM-dd"),
                    totalRevenue = totalRevenue,
                    totalBookings = totalBookings,
                    averageBookingValue = Math.Round(averageBookingValue, 0),
                    revenueByPeriod = revenueByPeriod,
                    revenueByAccommodation = revenueByAccommodation
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi tải thống kê doanh thu: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Update accommodation status (active/inactive)
    /// PUT /api/owner/accommodations/{id}/status
    /// </summary>
    [HttpPut("accommodations/{id}/status")]
    public async Task<IActionResult> UpdateAccommodationStatus(int id, [FromBody] UpdateAccommodationStatusRequest request)
    {
        try
        {
            var ownerId = GetCurrentUserId();
            
            // Verify ownership
            var accommodation = await _accommodationRepo.GetByIdAsync(id);
            if (accommodation == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy cơ sở lưu trú" });
            }

            if ((int?)accommodation.IdNguoiDung != ownerId)
            {
                return Forbid("Bạn không có quyền cập nhật trạng thái cơ sở này");
            }

            // Update status in database
            using var db = _connectionFactory.Create();
            
            // Check if TrangThai column exists
            var hasStatusColumn = await db.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='CoSoLuuTru' AND COLUMN_NAME='TrangThai'");

            if (hasStatusColumn == 0)
            {
                return BadRequest(new { success = false, message = "Cột trạng thái chưa được tạo trong database" });
            }

            // Update status (0 = khóa, 1 = hoạt động)
            var newStatus = request.isActive ? 1 : 0;
            await db.ExecuteAsync("UPDATE CoSoLuuTru SET TrangThai = @status WHERE Id = @id", 
                new { status = newStatus, id = id });

            return Ok(new
            {
                success = true,
                message = $"Cập nhật trạng thái cơ sở thành công",
                data = new
                {
                    accommodationId = id,
                    isActive = request.isActive,
                    status = request.isActive ? "Đang hoạt động" : "Tạm khóa",
                    updatedAt = DateTime.Now
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi cập nhật trạng thái: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get detailed revenue report with payments
    /// GET /api/owner/revenue/detailed
    /// </summary>
    [HttpGet("revenue/detailed")]
    public async Task<IActionResult> GetDetailedRevenue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null)
    {
        try
        {
            var ownerId = GetCurrentUserId();
            
            // Get owner's accommodations
            var accommodations = await _accommodationRepo.ListAsync(1, 1000, null, true, ownerId);
            var accommodationIds = accommodations.Select(a => (int)a.Id).ToList();

            var (startDate, endDate) = CalculateDateRange("month", fromDate, toDate);

            // Get detailed revenue data
            var revenueDetails = new List<object>();
            var (allBookings, _) = await _bookingRepo.ListAsync(1, 10000, null, null);

            foreach (var booking in allBookings)
            {
                var roomId = (int?)booking.IdPhong;
                if (roomId.HasValue)
                {
                    var room = await _roomRepo.GetByIdAsync(roomId.Value);
                    if (room != null && accommodationIds.Contains((int)room.IdCoSoLuuTru))
                    {
                        var bookingDate = (DateTime?)booking.NgayDat;
                        var statusId = (int?)booking.IdTrangThai;
                        
                        if (bookingDate.HasValue && bookingDate.Value.Date >= startDate.Date && 
                            bookingDate.Value.Date <= endDate.Date && statusId == 8)
                        {
                            var guest = await _userRepo.GetByIdAsync((int)booking.IdNguoiDung);
                            var accommodation = accommodations.FirstOrDefault(a => (int)a.Id == (int)room.IdCoSoLuuTru);
                            
                            revenueDetails.Add(new
                            {
                                bookingId = booking.Id,
                                bookingDate = bookingDate,
                                checkInDate = booking.NgayNhanPhong,
                                checkOutDate = booking.NgayTraPhong,
                                guest = new
                                {
                                    name = guest?.HoTen?.ToString(),
                                    email = guest?.Email?.ToString()
                                },
                                accommodation = new
                                {
                                    id = accommodation?.Id,
                                    name = accommodation?.TenCoSo?.ToString()
                                },
                                room = new
                                {
                                    id = room.Id,
                                    name = room.TenPhong?.ToString()
                                },
                                amount = booking.TongTienTamTinh,
                                status = "Hoàn tất"
                            });
                        }
                    }
                }
            }

            // Sort by booking date desc
            revenueDetails = revenueDetails.OrderByDescending(r => ((DateTime?)((dynamic)r).bookingDate)).ToList();

            // Pagination
            var total = revenueDetails.Count;
            var items = revenueDetails.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new
            {
                success = true,
                message = "Báo cáo doanh thu chi tiết",
                data = new
                {
                    items = items,
                    total = total,
                    page = page,
                    pageSize = pageSize,
                    totalRevenue = revenueDetails.Sum(r => (decimal?)((dynamic)r).amount ?? 0)
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi tải báo cáo chi tiết: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get today's check-ins and check-outs
    /// GET /api/owner/today-activities
    /// </summary>
    [HttpGet("today-activities")]
    public async Task<IActionResult> GetTodayActivities([FromQuery] string? date = null)
    {
        try
        {
            var ownerId = GetCurrentUserId();
            // Hỗ trợ chọn ngày qua query ?date=yyyy-MM-dd; mặc định hôm nay
            DateTime targetDate = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed))
            {
                targetDate = parsed.Date;
            }
            
            // Get owner's accommodations
            var accommodations = await _accommodationRepo.ListAsync(1, 1000, null, true, ownerId);
            var accommodationIds = accommodations.Select(a => (int)a.Id).ToList();

            if (!accommodationIds.Any())
            {
                return Ok(new
                {
                    success = true,
                    message = "Hoạt động hôm nay",
                    data = new { checkIns = new List<object>(), checkOuts = new List<object>() }
                });
            }

            // Get all bookings and filter for activities theo ngày đã chọn
            var (allBookings, _) = await _bookingRepo.ListAsync(1, 10000, null, null);
            var todayCheckIns = new List<dynamic>();
            var todayCheckOuts = new List<dynamic>();

            foreach (var booking in allBookings)
            {
                var roomId = (int?)booking.IdPhong;
                if (roomId.HasValue)
                {
                    var room = await _roomRepo.GetByIdAsync(roomId.Value);
                    if (room != null && accommodationIds.Contains((int)room.IdCoSoLuuTru))
                    {
                        var checkInDate = (DateTime?)booking.NgayNhanPhong;
                        var checkOutDate = (DateTime?)booking.NgayTraPhong;
                        var status = (int?)booking.IdTrangThai;

                        // Check-ins của ngày chọn (status 3 = ChoCheckIn hoặc 4 = DaThanhToanDayDu)
                        if (checkInDate?.Date == targetDate && (status == 3 || status == 4))
                        {
                            var guest = await _userRepo.GetByIdAsync((int)booking.IdNguoiDung);
                            todayCheckIns.Add(new
                            {
                                bookingId = booking.Id,
                                room = new { id = room.Id, name = room.TenPhong?.ToString() },
                                guest = new
                                {
                                    id = guest?.Id,
                                    name = guest?.HoTen?.ToString(),
                                    phone = guest?.SoDienThoai?.ToString()
                                },
                                checkInTime = checkInDate,
                                status = status
                            });
                        }

                        // Check-outs của ngày chọn (status 7 = DaNhanPhong)
                        if (checkOutDate?.Date == targetDate && status == 7)
                        {
                            var guest = await _userRepo.GetByIdAsync((int)booking.IdNguoiDung);
                            todayCheckOuts.Add(new
                            {
                                bookingId = booking.Id,
                                room = new { id = room.Id, name = room.TenPhong?.ToString() },
                                guest = new
                                {
                                    id = guest?.Id,
                                    name = guest?.HoTen?.ToString(),
                                    phone = guest?.SoDienThoai?.ToString()
                                },
                                checkOutTime = checkOutDate,
                                status = status
                            });
                        }
                    }
                }
            }

            return Ok(new
            {
                success = true,
                message = $"Hoạt động ngày {targetDate:dd/MM/yyyy}",
                data = new
                {
                    checkIns = todayCheckIns,
                    checkOuts = todayCheckOuts,
                    totalCheckIns = todayCheckIns.Count,
                    totalCheckOuts = todayCheckOuts.Count
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi tải hoạt động hôm nay: {ex.Message}"
            });
        }
    }

    private string? BuildImageUrl(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return null;
        
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return imagePath.StartsWith("http") ? imagePath : $"{baseUrl}{imagePath}";
    }

    private (DateTime startDate, DateTime endDate) CalculateDateRange(string? period, string? fromDate, string? toDate)
    {
        DateTime startDate, endDate;

        if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out startDate) &&
            !string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out endDate))
        {
            return (startDate, endDate);
        }

        var now = DateTime.Now;
        switch (period?.ToLower())
        {
            case "day":
                startDate = now.Date;
                endDate = now.Date.AddDays(1).AddSeconds(-1);
                break;
            case "week":
                var dayOfWeek = (int)now.DayOfWeek;
                startDate = now.Date.AddDays(-dayOfWeek);
                endDate = startDate.AddDays(7).AddSeconds(-1);
                break;
            case "year":
                startDate = new DateTime(now.Year, 1, 1);
                endDate = new DateTime(now.Year, 12, 31, 23, 59, 59);
                break;
            default: // month
                startDate = new DateTime(now.Year, now.Month, 1);
                endDate = startDate.AddMonths(1).AddSeconds(-1);
                break;
        }

        return (startDate, endDate);
    }

    private List<object> GroupRevenueByPeriod(List<dynamic> bookings, string? period)
    {
        var result = new List<object>();

        switch (period?.ToLower())
        {
            case "day":
                var dailyGroups = bookings
                    .GroupBy(b => ((DateTime)b.NgayDat).Date)
                    .OrderBy(g => g.Key);
                
                foreach (var group in dailyGroups)
                {
                    result.Add(new
                    {
                        period = group.Key.ToString("yyyy-MM-dd"),
                        revenue = group.Sum(b => (decimal?)b.TongTienTamTinh ?? 0),
                        bookings = group.Count()
                    });
                }
                break;

            case "week":
                var weeklyGroups = bookings
                    .GroupBy(b => GetWeekOfYear((DateTime)b.NgayDat))
                    .OrderBy(g => g.Key);
                
                foreach (var group in weeklyGroups)
                {
                    var firstBooking = group.First();
                    var weekStart = GetStartOfWeek((DateTime)firstBooking.NgayDat);
                    result.Add(new
                    {
                        period = $"Tuần {group.Key} - {weekStart:dd/MM}",
                        revenue = group.Sum(b => (decimal?)b.TongTienTamTinh ?? 0),
                        bookings = group.Count()
                    });
                }
                break;

            case "year":
                var yearlyGroups = bookings
                    .GroupBy(b => ((DateTime)b.NgayDat).Month)
                    .OrderBy(g => g.Key);
                
                foreach (var group in yearlyGroups)
                {
                    result.Add(new
                    {
                        period = $"Tháng {group.Key}",
                        revenue = group.Sum(b => (decimal?)b.TongTienTamTinh ?? 0),
                        bookings = group.Count()
                    });
                }
                break;

            default: // month - group by day
                var monthlyGroups = bookings
                    .GroupBy(b => ((DateTime)b.NgayDat).Day)
                    .OrderBy(g => g.Key);
                
                foreach (var group in monthlyGroups)
                {
                    result.Add(new
                    {
                        period = $"Ngày {group.Key}",
                        revenue = group.Sum(b => (decimal?)b.TongTienTamTinh ?? 0),
                        bookings = group.Count()
                    });
                }
                break;
        }

        return result;
    }

    private async Task<List<object>> GetRevenueByAccommodation(List<dynamic> bookings, IEnumerable<dynamic> accommodations)
    {
        var result = new List<object>();

        foreach (var accommodation in accommodations)
        {
            var accommodationId = (int)accommodation.Id;
            var accommodationRevenue = 0m;
            var accommodationBookings = 0;

            foreach (var booking in bookings)
            {
                var roomId = (int?)booking.IdPhong;
                if (roomId.HasValue)
                {
                    var room = await _roomRepo.GetByIdAsync(roomId.Value);
                    if (room != null && (int)room.IdCoSoLuuTru == accommodationId)
                    {
                        accommodationRevenue += (decimal?)booking.TongTienTamTinh ?? 0;
                        accommodationBookings++;
                    }
                }
            }

            if (accommodationRevenue > 0 || accommodationBookings > 0)
            {
                result.Add(new
                {
                    accommodationId = accommodationId,
                    accommodationName = accommodation.TenCoSo?.ToString(),
                    revenue = accommodationRevenue,
                    bookings = accommodationBookings,
                    averageBookingValue = accommodationBookings > 0 ? Math.Round(accommodationRevenue / accommodationBookings, 0) : 0
                });
            }
        }

        return result.OrderByDescending(r => (decimal)((dynamic)r).revenue).ToList();
    }

    private int GetWeekOfYear(DateTime date)
    {
        var dayOfYear = date.DayOfYear;
        return (dayOfYear - 1) / 7 + 1;
    }

    private DateTime GetStartOfWeek(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        return date.Date.AddDays(-dayOfWeek);
    }

    private bool GetAccommodationStatus(dynamic accommodation)
    {
        // Kiểm tra cột TrangThai (0 = khóa, 1 = hoạt động)
        var trangThai = accommodation.TrangThai;
        if (trangThai == null) return true; // Mặc định hoạt động nếu chưa có cột
        
        return Convert.ToInt32(trangThai) == 1;
    }

    private string GetAccommodationStatusText(dynamic accommodation)
    {
        var isActive = GetAccommodationStatus(accommodation);
        return isActive ? "Đang hoạt động" : "Tạm khóa";
    }
}

// Request Models
public class CheckInRequest
{
    public string? notes { get; set; }
}

public class CheckOutRequest
{
    public string? notes { get; set; }
    public decimal? additionalCharges { get; set; }
    public string? damageReport { get; set; }
}

public class UpdateAccommodationStatusRequest
{
    public bool isActive { get; set; }
}
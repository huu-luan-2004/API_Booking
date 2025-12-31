using HotelBookingApi.Data;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/demo")]
public class DemoController : ControllerBase
{
    private readonly DatPhongRepository _bookingRepo;
    private readonly NguoiDungRepository _userRepo;
    private readonly CoSoLuuTruRepository _accommodationRepo;

    public DemoController(
        DatPhongRepository bookingRepo,
        NguoiDungRepository userRepo,
        CoSoLuuTruRepository accommodationRepo)
    {
        _bookingRepo = bookingRepo;
        _userRepo = userRepo;
        _accommodationRepo = accommodationRepo;
    }

    [HttpGet("all-data")]
    public async Task<IActionResult> GetAllRealData()
    {
        try
        {
            // Lấy dữ liệu thật từ database hiện có
            var (bookings, bookingTotal) = await _bookingRepo.ListWithDetailsAsync(1, 50);
            var (users, userTotal) = await _userRepo.ListAsync(1, 50, null);
            var accommodations = await _accommodationRepo.ListAsync(1, 50, null, true, null);
            var bookingStatusCounts = await _bookingRepo.GetBookingStatusCountsAsync();

            // Tạo response với tất cả dữ liệu thật
            var bookingsList = bookings.ToList();
            var usersList = users.ToList();
            var accommodationsList = accommodations.ToList();
            var statusCountsList = ((IEnumerable<dynamic>)bookingStatusCounts).ToList();

            var realData = new
            {
                success = true,
                message = "🔥 DỮ LIỆU THẬT TỪ DATABASE - KHÔNG PHẢI MOCK!",
                timestamp = DateTime.Now,
                dataSource = "SQL Server Database - Real Production Data",
                summary = new
                {
                    totalBookings = bookingTotal,
                    totalUsers = userTotal, 
                    totalAccommodations = accommodationsList.Count,
                    totalStatusTypes = statusCountsList.Count
                },
                
                // 1. BOOKINGS - Dữ liệu thật từ bảng DatPhong (28 records)
                realBookings = new
                {
                    total = bookingTotal,
                    note = "Real booking data from DatPhong table",
                    sample = bookingsList.Take(10).Select(b => new {
                        id = b.Id,
                        idPhong = b.IdPhong,
                        idNguoiDung = b.IdNguoiDung,
                        ngayNhanPhong = b.NgayNhanPhong,
                        ngayTraPhong = b.NgayTraPhong,
                        tongTien = b.TongTien,
                        trangThai = new {
                            id = b.IdTrangThai,
                            ma = b.TT_MaTrangThai,
                            ten = b.TT_TenTrangThai
                        },
                        ngayDat = b.NgayDat,
                        createdAt = b.CreatedAt,
                        khachHang = new {
                            hoTen = b.KH_HoTen,
                            email = b.KH_Email
                        }
                    }).ToList()
                },

                // 2. USERS - Dữ liệu thật từ bảng NguoiDung (8 users)
                realUsers = new
                {
                    total = userTotal,
                    note = "Real user data from NguoiDung table",
                    sample = usersList.Take(10).Select(u => new {
                        id = u.Id,
                        email = u.Email?.ToString(),
                        hoTen = u.HoTen?.ToString(),
                        soDienThoai = u.SoDienThoai?.ToString(),
                        vaiTro = u.VaiTro?.ToString(),
                        trangThaiTaiKhoan = u.TrangThaiTaiKhoan?.ToString(),
                        ngayTao = u.NgayTao,
                        hasAvatar = !string.IsNullOrEmpty(u.AnhDaiDien?.ToString())
                    }).ToList()
                },

                // 3. ACCOMMODATIONS - Dữ liệu thật từ bảng CoSoLuuTru (5 properties) 
                realAccommodations = new
                {
                    total = accommodationsList.Count,
                    note = "Real accommodation data from CoSoLuuTru table",
                    sample = accommodationsList.Take(10).Select(a => new {
                        id = a.Id,
                        tenCoSo = a.TenCoSo?.ToString(),
                        diaChi = a.DiaChi?.ToString(),
                        trangThaiDuyet = a.TrangThaiDuyet?.ToString(),
                        loaiHinh = a.LoaiHinh?.ToString(),
                        giaPhongTu = a.GiaPhongTu,
                        giaPhongDen = a.GiaPhongDen,
                        ngayTao = a.NgayTao
                    }).ToList()
                },

                // 4. BOOKING STATISTICS - Thống kê thật theo trạng thái
                realBookingStats = new
                {
                    note = "Real booking statistics from database",
                    statusBreakdown = statusCountsList.Select(s => new {
                        maTrangThai = s.MaTrangThai?.ToString(),
                        tenTrangThai = s.TenTrangThai?.ToString(), 
                        soLuong = (int)s.SoLuong,
                        tongTien = s.TongTien != null ? (decimal)s.TongTien : 0m
                    }).ToList()
                }
            };

            return Ok(realData);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                success = false,
                message = "Lỗi khi lấy dữ liệu thật từ database",
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetRealDashboard()
    {
        try
        {
            // Lấy tất cả dữ liệu cần thiết cho dashboard admin
            var (bookings, bookingTotal) = await _bookingRepo.ListWithDetailsAsync(1, 100);
            var (users, userTotal) = await _userRepo.ListAsync(1, 100, null);
            var accommodations = await _accommodationRepo.ListAsync(1, 100, null, true, null);
            var statusCounts = await _bookingRepo.GetBookingStatusCountsAsync();

            var bookingsList = bookings.ToList();
            var usersList = users.ToList(); 
            var accommodationsList = accommodations.ToList();
            var statusList = ((IEnumerable<dynamic>)statusCounts).ToList();

            // Tính toán thống kê thật
            var totalRevenue = statusList.Sum(s => s.TongTien != null ? (decimal)s.TongTien : 0m);
            var totalBookingCount = statusList.Sum(s => (int)s.SoLuong);

            var dashboard = new
            {
                success = true,
                message = "🎯 ADMIN DASHBOARD - DỮ LIỆU THẬT 100%",
                dataSource = "Live SQL Server Database",
                
                // Overview metrics
                overview = new
                {
                    totalUsers = userTotal,
                    totalBookings = totalBookingCount,
                    totalRevenue = totalRevenue,
                    totalAccommodations = accommodationsList.Count
                },

                // User breakdown
                userStats = new
                {
                    total = userTotal,
                    byRole = usersList.GroupBy(u => u.VaiTro?.ToString() ?? "Unknown")
                                     .Select(g => new { role = g.Key, count = g.Count() })
                                     .ToList(),
                    byStatus = usersList.GroupBy(u => u.TrangThaiTaiKhoan?.ToString() ?? "Unknown")  
                                       .Select(g => new { status = g.Key, count = g.Count() })
                                       .ToList(),
                    recent = usersList.OrderByDescending(u => u.NgayTao ?? DateTime.MinValue)
                                     .Take(5)
                                     .Select(u => new {
                                         id = u.Id,
                                         hoTen = u.HoTen?.ToString(),
                                         email = u.Email?.ToString(),
                                         ngayTao = u.NgayTao
                                     }).ToList()
                },

                // Booking breakdown  
                bookingStats = new
                {
                    total = totalBookingCount,
                    totalRevenue = totalRevenue,
                    byStatus = statusList.Select(s => new {
                        status = s.TenTrangThai?.ToString(),
                        count = (int)s.SoLuong,
                        revenue = s.TongTien != null ? (decimal)s.TongTien : 0m
                    }).ToList(),
                    recent = bookingsList.OrderByDescending(b => b.NgayDat ?? DateTime.MinValue)
                                        .Take(5)
                                        .Select(b => new {
                                            id = b.Id,
                                            tongTien = b.TongTien,
                                            ngayDat = b.NgayDat,
                                            trangThai = b.TT_TenTrangThai,
                                            khachHang = b.KH_HoTen
                                        }).ToList()
                },

                // Accommodation breakdown
                accommodationStats = new
                {
                    total = accommodationsList.Count,
                    byStatus = accommodationsList.GroupBy(a => a.TrangThaiDuyet?.ToString() ?? "Unknown")
                                                 .Select(g => new { status = g.Key, count = g.Count() })
                                                 .ToList(),
                    byType = accommodationsList.GroupBy(a => a.LoaiHinh?.ToString() ?? "Unknown")
                                               .Select(g => new { type = g.Key, count = g.Count() })
                                               .ToList(),
                    recent = accommodationsList.OrderByDescending(a => a.NgayTao ?? DateTime.MinValue)
                                              .Take(5)
                                              .Select(a => new {
                                                  id = a.Id,
                                                  tenCoSo = a.TenCoSo?.ToString(),
                                                  trangThaiDuyet = a.TrangThaiDuyet?.ToString(),
                                                  ngayTao = a.NgayTao
                                              }).ToList()
                },

                lastUpdated = DateTime.Now
            };

            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                success = false,
                message = "Lỗi khi lấy dashboard data",
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }
}
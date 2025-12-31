using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly DatPhongRepository _bookingRepo;
    private readonly NguoiDungRepository _userRepo;
    private readonly ThanhToanRepository _paymentRepo;
    private readonly CoSoLuuTruRepository _accommodationRepo;

    public AdminController(
        DatPhongRepository bookingRepo, 
        NguoiDungRepository userRepo,
        ThanhToanRepository paymentRepo,
        CoSoLuuTruRepository accommodationRepo)
    {
        _bookingRepo = bookingRepo;
        _userRepo = userRepo;
        _paymentRepo = paymentRepo;
        _accommodationRepo = accommodationRepo;
    }

    // API tổng hợp dashboard cho Admin
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            // Lấy dữ liệu tuần tự để tránh lỗi type inference
            var (users, totalUsers) = await _userRepo.ListAsync(1, 1000, null);
            var (bookings, totalBookings) = await _bookingRepo.ListAsync(1, 1000, null, null);
            var statusCounts = await _bookingRepo.GetBookingStatusCountsAsync();
            var accommodations = await _accommodationRepo.ListAsync(1, 100, null, false, null);

            // Tính toán thống kê
            var usersByRole = users.GroupBy(u => u.VaiTro?.ToString() ?? "Unknown")
                                  .Select(g => new { role = g.Key, count = g.Count() })
                                  .ToList();

            var recentBookings = bookings.Take(10)
                                        .Select(b => new {
                                            id = b.Id,
                                            idNguoiDung = b.IdNguoiDung,
                                            tongTien = b.TongTien,
                                            ngayDat = b.NgayDat,
                                            trangThai = b.TrangThai
                                        }).ToList();

            var recentUsers = users.OrderByDescending(u => u.NgayTao ?? DateTime.MinValue)
                                  .Take(5)
                                  .Select(u => new {
                                      id = u.Id,
                                      hoTen = u.HoTen?.ToString(),
                                      email = u.Email?.ToString(),
                                      vaiTro = u.VaiTro?.ToString(),
                                      ngayTao = u.NgayTao
                                  }).ToList();

            var dashboard = new
            {
                overview = new
                {
                    totalUsers = totalUsers,
                    totalBookings = totalBookings,
                    totalAccommodations = accommodations?.Count() ?? 0,
                    lastUpdated = DateTime.Now
                },
                
                userStats = new
                {
                    total = totalUsers,
                    byRole = usersByRole,
                    recent = recentUsers
                },

                bookingStats = new
                {
                    total = totalBookings,
                    statusBreakdown = statusCounts,
                    recent = recentBookings
                },

                accommodationStats = new
                {
                    total = accommodations?.Count() ?? 0,
                    recent = accommodations?.Take(5).Select(a => new {
                        id = a.Id,
                        tenCoSo = a.TenCoSo?.ToString(),
                        diaChi = a.DiaChi?.ToString()
                    }).ToList()
                }
            };

            return Ok(new { 
                success = true, 
                message = "Dashboard data cho Admin", 
                data = dashboard 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi server khi lấy dashboard data", 
                error = ex.Message,
                stackTrace = ex.StackTrace?.Split('\n').Take(5).ToArray()
            });
        }
    }

    // API health check cho admin endpoints
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { 
            success = true, 
            message = "Admin API hoạt động bình thường",
            timestamp = DateTime.Now,
            server = "HotelBooking Admin API v2.0"
        });
    }

    // API overview stats nhanh
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        try
        {
            // Parallel calls for fast overview
            var userTask = _userRepo.ListAsync(1, 1, null);
            var bookingTask = _bookingRepo.ListAsync(1, 1, null, null); 
            var accommodationTask = _accommodationRepo.ListAsync(1, 1, null, false, null);

            await Task.WhenAll(userTask, bookingTask, accommodationTask);

            var (_, totalUsers) = await userTask;
            var (_, totalBookings) = await bookingTask;
            var accommodationList = await accommodationTask;
            var totalAccommodations = accommodationList?.Count() ?? 0;

            return Ok(new { 
                success = true, 
                data = new {
                    totalUsers,
                    totalBookings,
                    totalAccommodations,
                    lastUpdated = DateTime.Now
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi lấy overview", 
                error = ex.Message 
            });
        }
    }
}
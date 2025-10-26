using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly DatPhongRepository _repo;
    public BookingsController(DatPhongRepository repo) => _repo = repo;

    [HttpGet]
    public IActionResult Health() => Ok(new { success=true, message="API đặt phòng hoạt động" });

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] dynamic body)
    {
        if (!int.TryParse(User?.FindFirst("id")?.Value, out var idNguoiDung))
            return Unauthorized(new { success=false, message="Vui lòng đăng nhập để đặt phòng" });
        int idPhong = (int)(body?.idPhong ?? 0);
        
        if (!DateTime.TryParse(body?.ngayNhanPhong?.ToString(), out DateTime ngayNhan))
            return BadRequest(new { success=false, message="Ngày nhận phòng không hợp lệ" });
        if (!DateTime.TryParse(body?.ngayTraPhong?.ToString(), out DateTime ngayTra))
            return BadRequest(new { success=false, message="Ngày trả phòng không hợp lệ" });
        decimal tong = (decimal)(body?.tongTien ?? 3000000);
        if (idPhong<=0 || ngayNhan==default || ngayTra==default)
            return BadRequest(new { success=false, message="Thiếu thông tin phòng cần đặt (idPhong, ngayNhanPhong, ngayTraPhong)" });
        var id = await _repo.CreateAsync(idNguoiDung, idPhong, ngayNhan, ngayTra, tong);
        var dp = await _repo.GetByIdAsync(id);
        var response = new {
            dp.Id,
            dp.IdNguoiDung,
            dp.IdPhong,
            dp.NgayNhanPhong,
            dp.NgayTraPhong,
            dp.TongTienTamTinh,
            nguoiDung = new { id = dp.IdNguoiDung, hoTen = dp.ND_HoTen, email = dp.ND_Email, soDienThoai = dp.ND_SoDienThoai },
            phong = new { id = dp.IdPhong, tenPhong = dp.P_TenPhong },
            coSo = new { tenCoSo = dp.CS_TenCoSo },
            trangThai = new { id = dp.IdTrangThai, ma = dp.TT_MaTrangThai, ten = dp.TT_TenTrangThai }
        };
        return StatusCode(201, new { success=true, message="Đặt phòng thành công", data = response });
    }

    [Authorize]
    [HttpGet("user")]
    public async Task<IActionResult> UserBookings()
    {
        int idNguoiDung = int.Parse(User.FindFirst("id")!.Value);
        var bookings = await _repo.ListByUserAsync(idNguoiDung);
        return Ok(new { success=true, message="Lấy danh sách đặt phòng thành công", data = bookings });
    }

    [HttpGet("check-availability")]
    public async Task<IActionResult> CheckAvailability([FromQuery] int idPhong, [FromQuery] DateTime ngayNhanPhong, [FromQuery] DateTime ngayTraPhong)
    {
        if (idPhong<=0 || ngayNhanPhong==default || ngayTraPhong==default)
            return BadRequest(new { success=false, message="Vui lòng cung cấp đầy đủ thông tin" });
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
}

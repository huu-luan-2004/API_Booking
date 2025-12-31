using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/holds")]
public class HoldsController : ControllerBase
{
    private readonly PreBookingHoldRepository _repo;
    public HoldsController(PreBookingHoldRepository repo) { _repo = repo; }

    public class AcquireRequest
    {
        public int idPhong { get; set; }
        public DateTime ngayNhanPhong { get; set; }
        public DateTime ngayTraPhong { get; set; }
        public int ttlMinutes { get; set; } = 15;
    }

    [Authorize]
    [HttpPost("acquire")]
    public async Task<IActionResult> Acquire([FromBody] AcquireRequest body)
    {
        if (!int.TryParse(User?.FindFirst("id")?.Value, out var idNguoiDung))
            return Unauthorized(new { success=false, message="Vui lòng đăng nhập" });
        if (body == null || body.idPhong <= 0 || body.ngayNhanPhong == default || body.ngayTraPhong == default || body.ngayNhanPhong >= body.ngayTraPhong)
            return BadRequest(new { success=false, message="Thiếu/sai tham số" });
        try
        {
            var (token, exp) = await _repo.AcquireAsync(idNguoiDung, body.idPhong, body.ngayNhanPhong, body.ngayTraPhong, body.ttlMinutes);
            return Ok(new { success=true, data = new { holdToken = token, expiresAt = exp } });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success=false, message=ex.Message });
        }
    }

    public class RenewReleaseRequest { public string holdToken { get; set; } = string.Empty; public int ttlMinutes { get; set; } = 15; }

    [Authorize]
    [HttpPost("renew")]
    public async Task<IActionResult> Renew([FromBody] RenewReleaseRequest body)
    {
        if (string.IsNullOrWhiteSpace(body?.holdToken)) return BadRequest(new { success=false, message="Thiếu holdToken" });
        var ok = await _repo.RenewAsync(body.holdToken, body.ttlMinutes);
        return Ok(new { success=ok, message = ok ? "Gia hạn thành công" : "Hold hết hạn hoặc không tồn tại" });
    }

    [Authorize]
    [HttpPost("release")]
    public async Task<IActionResult> Release([FromBody] RenewReleaseRequest body)
    {
        if (string.IsNullOrWhiteSpace(body?.holdToken)) return BadRequest(new { success=false, message="Thiếu holdToken" });
        var ok = await _repo.ReleaseAsync(body.holdToken);
        return Ok(new { success=ok, message = ok ? "Đã mở khóa phòng" : "Hold không tồn tại" });
    }

    [Authorize(Roles="Admin")]
    [HttpPost("purge")]
    public async Task<IActionResult> Purge([FromQuery] int minutes = 0)
    {
        var n = await _repo.PurgeExpiredAsync(minutes);
        return Ok(new { success=true, message=$"Đã xóa {n} hold hết hạn" });
    }
}

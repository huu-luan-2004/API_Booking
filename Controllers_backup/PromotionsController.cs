using HotelBookingApi.Data;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/promotions")]
public class PromotionsController : ControllerBase
{
    private readonly PhongRepository _phongRepo;
    private readonly KhuyenMaiRepository _kmRepo;
    public PromotionsController(PhongRepository phongRepo, KhuyenMaiRepository kmRepo)
    { _phongRepo = phongRepo; _kmRepo = kmRepo; }

    [HttpGet("rooms/{id:int}/preview")]
    public async Task<IActionResult> Preview([FromRoute] int id, [FromQuery] string? checkin, [FromQuery] string? checkout, [FromQuery] string? ma)
    {
        var room = await _phongRepo.GetByIdAsync(id);
        if (room is null) return NotFound(new { success=false, message="Không tìm thấy phòng" });
        decimal? giaGoc = null;
        try { giaGoc = (decimal?)room.Gia; } catch {}
        if (giaGoc is null) return Ok(new { success=true, data = new { GiaGoc = (decimal?)null, GiaKhuyenMai = (decimal?)null, GiaApDung = (decimal?)null, CoKhuyenMai=false, SoDem = (int?)null, KhuyenMai = (object?)null } });

        int SoDem()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(checkin) && !string.IsNullOrWhiteSpace(checkout))
                {
                    var d1 = DateTime.Parse(checkin!);
                    var d2 = DateTime.Parse(checkout!);
                    var days = (int)Math.Max(1, Math.Round((d2 - d1).TotalDays));
                    return days;
                }
            }
            catch {}
            return 1;
        }

        var soDem = SoDem();
        decimal giaApDung = giaGoc.Value;
        object? kmApplied = null;

        if (!string.IsNullOrWhiteSpace(ma))
        {
            var km = await _kmRepo.GetByCodeForRoomAsync(id, ma!);
            if (km != null)
            {
                var loai = (km.LoaiGiamGia ?? string.Empty).ToString().ToLowerInvariant();
                decimal giaTri = 0m; try { giaTri = (decimal)km.GiaTriGiam; } catch {}
                if (loai.Contains("percent") || loai.Contains("phantram") || loai == "pct")
                    giaApDung = Math.Round(giaGoc.Value * (1 - giaTri/100));
                else
                    giaApDung = Math.Max(0, giaGoc.Value - giaTri);
                kmApplied = km;
            }
        }
        else
        {
            var autos = await _kmRepo.GetActiveAutoForRoomsAsync(new []{ id });
            var km = autos.FirstOrDefault();
            if (km != null)
            {
                var loai = (km.LoaiGiamGia ?? string.Empty).ToString().ToLowerInvariant();
                decimal giaTri = 0m; try { giaTri = (decimal)km.GiaTriGiam; } catch {}
                if (loai.Contains("percent") || loai.Contains("phantram") || loai == "pct")
                    giaApDung = Math.Round(giaGoc.Value * (1 - giaTri/100));
                else
                    giaApDung = Math.Max(0, giaGoc.Value - giaTri);
                kmApplied = km;
            }
        }

        var coKm = giaApDung < giaGoc.Value;
        return Ok(new { success=true, data = new { GiaGoc = giaGoc, GiaKhuyenMai = coKm ? giaApDung : (decimal?)null, GiaApDung = coKm ? giaApDung : giaGoc, CoKhuyenMai = coKm, SoDem = soDem, KhuyenMai = kmApplied } });
    }
}

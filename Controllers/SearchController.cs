using Dapper;
using Microsoft.AspNetCore.Mvc;
using HotelBookingApi.Data;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly SqlConnectionFactory _connectionFactory;

    public SearchController(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Tìm kiếm phòng theo địa chỉ và tên khách sạn
    // GET /api/search/rooms?q=&address=&hotelName=&page=&limit=
    [HttpGet("rooms")]
    public async Task<IActionResult> SearchRooms(
        [FromQuery] string? q = null,
        [FromQuery] string? address = null,
        [FromQuery] string? hotelName = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10)
    {
        try
        {
            using var db = _connectionFactory.Create();

            var whereParts = new List<string>();
            var p = new DynamicParameters();

            // Chuẩn hoá tham số
            var kw = (q ?? string.Empty).Trim();
            var addr = (address ?? string.Empty).Trim();
            var hotel = (hotelName ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(kw))
            {
                whereParts.Add("(p.TenPhong LIKE @kw OR c.TenCoSo LIKE @kw OR d.ChiTiet LIKE @kw OR d.Pho LIKE @kw OR d.Phuong LIKE @kw OR d.Nuoc LIKE @kw)");
                p.Add("kw", $"%{kw}%");
            }
            if (!string.IsNullOrEmpty(addr))
            {
                whereParts.Add("(d.ChiTiet LIKE @addr OR d.Pho LIKE @addr OR d.Phuong LIKE @addr OR d.Nuoc LIKE @addr)");
                p.Add("addr", $"%{addr}%");
            }
            if (!string.IsNullOrEmpty(hotel))
            {
                whereParts.Add("(c.TenCoSo LIKE @hotel)");
                p.Add("hotel", $"%{hotel}%");
            }

            var whereSql = whereParts.Any() ? (" WHERE " + string.Join(" AND ", whereParts)) : string.Empty;

            var offset = (Math.Max(1, page) - 1) * Math.Max(1, limit);
            var size = Math.Max(1, limit);
            p.Add("offset", offset);
            p.Add("size", size);

            // Đếm tổng để phân trang
            var countSql = $@"
                SELECT COUNT(1)
                FROM Phong p
                LEFT JOIN CoSoLuuTru c ON c.Id = p.IdCoSoLuuTru
                LEFT JOIN DiaChiChiTiet d ON d.Id = c.IdDiaChi
                {whereSql}";

            var total = await db.ExecuteScalarAsync<int>(countSql, p);

            var sql = $@"
                SELECT 
                    p.Id AS RoomId,
                    p.TenPhong AS RoomName,
                    p.MoTa AS RoomDescription,
                    p.Anh AS RoomImage,
                    p.SoNguoiToiDa,
                    c.Id AS AccommodationId,
                    c.TenCoSo AS HotelName,
                    c.Anh AS HotelImage,
                    d.ChiTiet AS AddressDetail,
                    d.Pho AS Street,
                    d.Phuong AS Ward,
                    d.Nuoc AS Country,
                    gp.GiaPhong AS GiaPhong,
                    gp.NgayApDung AS NgayApDungGia
                FROM Phong p
                LEFT JOIN CoSoLuuTru c ON c.Id = p.IdCoSoLuuTru
                LEFT JOIN DiaChiChiTiet d ON d.Id = c.IdDiaChi
                OUTER APPLY (
                    SELECT TOP 1 g.GiaPhong, g.NgayApDung
                    FROM GiaPhong g
                    WHERE g.IdPhong = p.Id
                    ORDER BY g.NgayApDung DESC
                ) gp
                {whereSql}
                ORDER BY p.Id DESC
                OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";

            var rows = await db.QueryAsync(sql, p);

            // Chuẩn hoá URL ảnh
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var items = rows.Select(r =>
            {
                var dict = r as IDictionary<string, object> ?? new Dictionary<string, object>();
                string ImgUrl(string? path, string sub) => string.IsNullOrEmpty(path) ? null! : (path.StartsWith("http") ? path : $"{baseUrl}/uploads/{sub}/{path}");
                if (dict.TryGetValue("RoomImage", out var ri) && ri != null)
                    dict["RoomImageUrl"] = ImgUrl(ri?.ToString(), "rooms");
                if (dict.TryGetValue("HotelImage", out var hi) && hi != null)
                    dict["HotelImageUrl"] = ImgUrl(hi?.ToString(), "accommodations");
                return dict;
            }).ToList();

            return Ok(new
            {
                success = true,
                message = "Kết quả tìm kiếm phòng",
                data = new
                {
                    items,
                    pagination = new
                    {
                        page,
                        limit = size,
                        total,
                        totalPages = (int)Math.Ceiling(total / (double)size)
                    }
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server khi tìm kiếm", error = ex.Message });
        }
    }
}

using Dapper;

namespace HotelBookingApi.Data;

public class KhuyenMaiRepository
{
    private readonly SqlConnectionFactory _factory;
    public KhuyenMaiRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<IEnumerable<dynamic>> GetActiveAutoForRoomsAsync(IEnumerable<int> roomIds)
    {
        var ids = roomIds.Distinct().Where(i => i > 0).ToArray();
        if (ids.Length == 0) return Array.Empty<dynamic>();
        var inList = string.Join(',', ids);
        using var db = _factory.Create();
        var sql = $@"
            SELECT * FROM (
              SELECT km.*, kmdp.IdPhong,
                     ROW_NUMBER() OVER (PARTITION BY kmdp.IdPhong ORDER BY km.NgayBatDau DESC, km.Id DESC) rn
              FROM KhuyenMai km
              JOIN KhuyenMaiDatPhong kmdp ON kmdp.IdKhuyenMai = km.Id
              WHERE (km.YeuCauMaKhuyenMai = 0 OR km.YeuCauMaKhuyenMai IS NULL)
                AND km.NgayBatDau <= GETDATE()
                AND (km.NgayKetThuc IS NULL OR km.NgayKetThuc >= GETDATE())
                AND kmdp.IdPhong IN ({inList})
            ) x WHERE x.rn = 1";
        var rows = await db.QueryAsync(sql);
        return rows;
    }

    public async Task<dynamic?> GetByCodeForRoomAsync(int idPhong, string ma)
    {
        using var db = _factory.Create();
        var sql = @"
          SELECT TOP 1 km.*, kmdp.IdPhong
          FROM KhuyenMai km
          JOIN KhuyenMaiDatPhong kmdp ON kmdp.IdKhuyenMai = km.Id
          WHERE km.MaKhuyenMai = @ma AND kmdp.IdPhong = @id
            AND km.NgayBatDau <= GETDATE()
            AND (km.NgayKetThuc IS NULL OR km.NgayKetThuc >= GETDATE())";
        var rows = await db.QueryAsync(sql, new { ma, id = idPhong });
        return rows.FirstOrDefault();
    }
}

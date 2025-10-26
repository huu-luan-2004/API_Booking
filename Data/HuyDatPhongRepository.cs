using Dapper;

namespace HotelBookingApi.Data;

public class HuyDatPhongRepository
{
    private readonly SqlConnectionFactory _factory;
    private readonly DatPhongRepository _bookingRepo;
    private readonly ThanhToanRepository _payRepo;
    public HuyDatPhongRepository(SqlConnectionFactory factory, DatPhongRepository bookingRepo, ThanhToanRepository payRepo)
    { _factory = factory; _bookingRepo = bookingRepo; _payRepo = payRepo; }

    public async Task<dynamic?> GetByDatPhongIdAsync(int idDatPhong)
    {
        using var db = _factory.Create();
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM HuyDatPhong WHERE IdDatPhong=@id ORDER BY CreatedAt DESC", new { id = idDatPhong });
        return rows.FirstOrDefault();
    }

    public async Task<dynamic?> GetByIdAsync(int id)
    {
        using var db = _factory.Create();
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM HuyDatPhong WHERE Id=@id", new { id });
        return rows.FirstOrDefault();
    }

    public async Task<int> CreateAsync(int idDatPhong, string lyDo, decimal tienHoanLai, decimal tienPhat, string trangThai, int idNguoiDung)
    {
        using var db = _factory.Create();

        // Phát hiện cột theo schema hiện có
        var hasTienHoanLai = await HasColumn(db, "TienHoanLai");
        var hasSoTienHoan = !hasTienHoanLai && await HasColumn(db, "SoTienHoan");
        var hasTienPhat = await HasColumn(db, "TienPhat");
        var hasTrangThai = await HasColumn(db, "TrangThai");
        var hasIdNguoiDung = await HasColumn(db, "IdNguoiDung");
        var hasCreatedAt = await HasColumn(db, "CreatedAt");
        var hasNgayHuy = !hasCreatedAt && await HasColumn(db, "NgayHuy");

        var columns = new List<string> { "IdDatPhong", "LyDo" };
        var values = new List<string> { "@IdDatPhong", "@LyDo" };

        if (hasTienHoanLai)
        { columns.Add("TienHoanLai"); values.Add("@TienHoanLai"); }
        else if (hasSoTienHoan)
        { columns.Add("SoTienHoan"); values.Add("@TienHoanLai"); }

        if (hasTienPhat)
        { columns.Add("TienPhat"); values.Add("@TienPhat"); }

        if (hasTrangThai)
        { columns.Add("TrangThai"); values.Add("@TrangThai"); }

        if (hasIdNguoiDung)
        { columns.Add("IdNguoiDung"); values.Add("@IdNguoiDung"); }

        if (hasCreatedAt)
        { columns.Add("CreatedAt"); values.Add("GETDATE()"); }
        else if (hasNgayHuy)
        { columns.Add("NgayHuy"); values.Add("GETDATE()"); }

        var sql = $"INSERT INTO HuyDatPhong ({string.Join(", ", columns)}) OUTPUT INSERTED.Id VALUES ({string.Join(", ", values)})";
        var id = await db.ExecuteScalarAsync<int>(sql, new { IdDatPhong = idDatPhong, LyDo = lyDo, TienHoanLai = tienHoanLai, TienPhat = tienPhat, TrangThai = trangThai, IdNguoiDung = idNguoiDung });
        return id;
    }

    public async Task UpdateTrangThaiAsync(int id, string trangThai, string? ghiChu)
    {
        using var db = _factory.Create();
        var hasTrangThai = await HasColumn(db, "TrangThai");
        var hasGhiChu = await HasColumn(db, "GhiChu");
        var hasUpdatedAt = await HasColumn(db, "UpdatedAt");

        var sets = new List<string>();
        var param = new Dapper.DynamicParameters();
        param.Add("id", id);

        if (hasTrangThai)
        { sets.Add("TrangThai=@tt"); param.Add("tt", trangThai); }
        if (hasGhiChu)
        { sets.Add("GhiChu=@gc"); param.Add("gc", ghiChu); }
        if (hasUpdatedAt)
        { sets.Add("UpdatedAt=GETDATE()"); }

        if (sets.Count == 0)
        {
            // Không có cột nào phù hợp để update; bỏ qua
            return;
        }

        var sql = $"UPDATE HuyDatPhong SET {string.Join(", ", sets)} WHERE Id=@id";
        await db.ExecuteAsync(sql, param);
    }

    private static async Task<bool> HasColumn(System.Data.IDbConnection db, string column)
    {
        var sql = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='HuyDatPhong' AND COLUMN_NAME=@c";
        var cnt = await db.ExecuteScalarAsync<int>(sql, new { c = column });
        return cnt > 0;
    }

    public async Task<(decimal tongTien, decimal daThanhToan, decimal tienHoanLai, decimal tienPhat, decimal tyLePhat, int soNgayConLai, int soGioConLai)> TinhTienHoanTraVaTienPhatAsync(int idDatPhong)
    {
        var dp = await _bookingRepo.GetByIdAsync(idDatPhong);
        if (dp is null) return (0,0,0,0,0,0,0);
        decimal tong = 0m;
        DateTime ngayNhan = DateTime.UtcNow;
        try { tong = (decimal)(dp?.TongTienTamTinh ?? 0m); } catch { }
        if (tong == 0m)
        {
            try { tong = (decimal)(dp?.TongTien ?? 0m); } catch { }
        }
        try
        {
            if (dp is IDictionary<string, object> d && d.TryGetValue("NgayNhanPhong", out var nn) && nn is DateTime dt)
            {
                ngayNhan = dt;
            }
        }
        catch { }
        var now = DateTime.UtcNow;
        var timeLeft = ngayNhan - now;
        var soGioConLai = (int)Math.Floor(timeLeft.TotalHours);
        var soNgayConLai = (int)Math.Floor(timeLeft.TotalDays);
        // Chính sách: 
        // - >= 24h: miễn phí (0%)
        // - 12h..24h: phạt 30%
        // - < 12h: phạt 100% (mất hết)
        decimal tyLePhat;
        if (soGioConLai >= 24) tyLePhat = 0m;
        else if (soGioConLai >= 12) tyLePhat = 0.3m;
        else tyLePhat = 1m;
        var tienPhat = Math.Round(tong * tyLePhat, 0);
        var daThanhToan = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
        var tienHoanLai = Math.Max(0, daThanhToan - tienPhat);
        return (tong, daThanhToan, tienHoanLai, tienPhat, tyLePhat, soNgayConLai, soGioConLai);
    }
}

using Dapper;
using System.Data;

namespace HotelBookingApi.Data;

public class ThanhToanRepository
{
    private readonly SqlConnectionFactory _factory;
    public ThanhToanRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task CreateAsync(int idDatPhong, string maGiaoDich, decimal soTien, string phuongThuc, string trangThai, string noiDung, string maDonHang, string loaiGiaoDich)
    {
        using var db = _factory.Create();
        // Thử đầy đủ cột (bao gồm CreatedAt, MaDonHang, LoaiGiaoDich)
        try
        {
            var sqlFull = @"INSERT INTO ThanhToan (IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung, MaDonHang, LoaiGiaoDich, CreatedAt)
                           VALUES (@IdDatPhong, @MaGiaoDich, @SoTien, @PhuongThuc, @TrangThai, @NoiDung, @MaDonHang, @LoaiGiaoDich, GETDATE())";
            await db.ExecuteAsync(sqlFull, new { IdDatPhong = idDatPhong, MaGiaoDich = maGiaoDich, SoTien = soTien, PhuongThuc = phuongThuc, TrangThai = trangThai, NoiDung = noiDung, MaDonHang = maDonHang, LoaiGiaoDich = loaiGiaoDich });
            return;
        }
        catch
        {
            // Bảng có MaDonHang/LoaiGiaoDich nhưng không có CreatedAt
            try
            {
                var sqlNoCreatedAt = @"INSERT INTO ThanhToan (IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung, MaDonHang, LoaiGiaoDich)
                                       VALUES (@IdDatPhong, @MaGiaoDich, @SoTien, @PhuongThuc, @TrangThai, @NoiDung, @MaDonHang, @LoaiGiaoDich)";
                await db.ExecuteAsync(sqlNoCreatedAt, new { IdDatPhong = idDatPhong, MaGiaoDich = maGiaoDich, SoTien = soTien, PhuongThuc = phuongThuc, TrangThai = trangThai, NoiDung = noiDung, MaDonHang = maDonHang, LoaiGiaoDich = loaiGiaoDich });
                return;
            }
            catch
            {
                // Bảng tối giản: không có CreatedAt, không có MaDonHang/LoaiGiaoDich
                var sqlMinimal = @"INSERT INTO ThanhToan (IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung)
                                   VALUES (@IdDatPhong, @MaGiaoDich, @SoTien, @PhuongThuc, @TrangThai, @NoiDung)";
                await db.ExecuteAsync(sqlMinimal, new { IdDatPhong = idDatPhong, MaGiaoDich = maGiaoDich, SoTien = soTien, PhuongThuc = phuongThuc, TrangThai = trangThai, NoiDung = noiDung });
            }
        }
    }

    public async Task CancelAllPendingForBookingAsync(int idDatPhong)
    {
        using var db = _factory.Create();
        await db.ExecuteAsync("UPDATE ThanhToan SET TrangThai='Đã hủy' WHERE IdDatPhong=@id AND TrangThai='Chờ thanh toán'", new { id = idDatPhong });
    }

    public async Task<decimal> GetTongDaThanhToanAsync(int idDatPhong)
    {
        using var db = _factory.Create();
        return await db.ExecuteScalarAsync<decimal>("SELECT ISNULL(SUM(SoTien),0) FROM ThanhToan WHERE IdDatPhong=@id AND TrangThai='Thành công'", new { id = idDatPhong });
    }

    public async Task<dynamic?> GetByMaGiaoDichAsync(string maGiaoDich)
    {
        using var db = _factory.Create();
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM ThanhToan WHERE MaGiaoDich=@m", new { m = maGiaoDich });
        return rows.FirstOrDefault();
    }

    

    public async Task<IEnumerable<dynamic>> ListByBookingAsync(int idDatPhong)
    {
        using var db = _factory.Create();
        try
        {
            return await db.QueryAsync("SELECT * FROM ThanhToan WHERE IdDatPhong=@id ORDER BY CreatedAt DESC", new { id = idDatPhong });
        }
        catch
        {
            // Fallback nếu không có cột CreatedAt
            return await db.QueryAsync("SELECT * FROM ThanhToan WHERE IdDatPhong=@id ORDER BY Id DESC", new { id = idDatPhong });
        }
    }

    public async Task<dynamic?> GetLatestSuccessPaymentAsync(int idDatPhong)
    {
        using var db = _factory.Create();
        try
        {
            var rows = await db.QueryAsync("SELECT TOP 1 * FROM ThanhToan WHERE IdDatPhong=@id AND TrangThai=N'Thành công' AND (LoaiGiaoDich IS NULL OR LoaiGiaoDich IN (N'Thanh toán', N'Thanh toán cọc', N'Thanh toán bổ sung')) ORDER BY CreatedAt DESC", new { id = idDatPhong });
            return rows.FirstOrDefault();
        }
        catch
        {
            var rows = await db.QueryAsync("SELECT TOP 1 * FROM ThanhToan WHERE IdDatPhong=@id AND TrangThai=N'Thành công' ORDER BY Id DESC", new { id = idDatPhong });
            return rows.FirstOrDefault();
        }
    }

    public async Task<dynamic> CreateRefundAsync(int idDatPhong, string maGiaoDich, decimal soTien, string noiDung, string maDonHang)
    {
        using var db = _factory.Create();
        // Cố gắng chèn với đầy đủ cột
        try
        {
            var sqlFull = @"INSERT INTO ThanhToan (IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung, MaDonHang, LoaiGiaoDich, CreatedAt)
                            OUTPUT INSERTED.*
                            VALUES (@IdDatPhong, @MaGiaoDich, @SoTien, N'VNPAY Refund', N'Chờ xử lý', @NoiDung, @MaDonHang, N'Hoàn tiền', GETDATE())";
            var rows = await db.QueryAsync(sqlFull, new { IdDatPhong = idDatPhong, MaGiaoDich = maGiaoDich, SoTien = soTien, NoiDung = noiDung, MaDonHang = maDonHang });
            var tx = rows.First();
            // Ghi nhận lịch sử hoàn tiền nếu bảng tồn tại
            await TryInsertRefundHistoryAsync(db, tx, soTien, noiDung, isMockRefund: true);
            return tx;
        }
        catch
        {
            // Không có CreatedAt nhưng có MaDonHang
            try
            {
                var sqlNoCreatedAt = @"INSERT INTO ThanhToan (IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung, MaDonHang, LoaiGiaoDich)
                                       OUTPUT INSERTED.*
                                       VALUES (@IdDatPhong, @MaGiaoDich, @SoTien, N'VNPAY Refund', N'Chờ xử lý', @NoiDung, @MaDonHang, N'Hoàn tiền')";
                var rows = await db.QueryAsync(sqlNoCreatedAt, new { IdDatPhong = idDatPhong, MaGiaoDich = maGiaoDich, SoTien = soTien, NoiDung = noiDung, MaDonHang = maDonHang });
                var tx = rows.First();
                await TryInsertRefundHistoryAsync(db, tx, soTien, noiDung, isMockRefund: true);
                return tx;
            }
            catch
            {
                // Bảng tối giản không có CreatedAt/MaDonHang/LoaiGiaoDich
                var sqlMinimal = @"INSERT INTO ThanhToan (IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung)
                                   OUTPUT INSERTED.*
                                   VALUES (@IdDatPhong, @MaGiaoDich, @SoTien, N'VNPAY Refund', N'Chờ xử lý', @NoiDung)";
                var rows = await db.QueryAsync(sqlMinimal, new { IdDatPhong = idDatPhong, MaGiaoDich = maGiaoDich, SoTien = soTien, NoiDung = noiDung });
                var tx = rows.First();
                await TryInsertRefundHistoryAsync(db, tx, soTien, noiDung, isMockRefund: true);
                return tx;
            }
        }
    }

    // Khi cập nhật trạng thái giao dịch hoàn tiền, cố gắng đồng bộ sang LichSuHoanTien nếu có
    public async Task UpdateTrangThaiAsync(string maGiaoDich, string trangThai, string? meta)
    {
        using var db = _factory.Create();
        try
        {
            await db.ExecuteAsync("UPDATE ThanhToan SET TrangThai=@tt, Meta=@meta WHERE MaGiaoDich=@m", new { m = maGiaoDich, tt = trangThai, meta });
        }
        catch
        {
            // Fallback if Meta column doesn't exist
            await db.ExecuteAsync("UPDATE ThanhToan SET TrangThai=@tt WHERE MaGiaoDich=@m", new { m = maGiaoDich, tt = trangThai });
        }

        // Đồng bộ trạng thái vào LichSuHoanTien (nếu bảng tồn tại)
        var tx = await GetByMaGiaoDichInternalAsync(db, maGiaoDich);
        if (tx != null)
        {
            try
            {
                int idThanhToan = 0;
                if (tx is IDictionary<string, object> d && d.TryGetValue("Id", out var idObj))
                {
                    int.TryParse(idObj?.ToString(), out idThanhToan);
                }
                if (idThanhToan > 0)
                {
                    await TryUpdateRefundHistoryStatusAsync(db, idThanhToan, trangThai, meta);
                }
            }
            catch { }
        }
    }

    private async Task<dynamic?> GetByMaGiaoDichInternalAsync(IDbConnection db, string maGiaoDich)
    {
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM ThanhToan WHERE MaGiaoDich=@m", new { m = maGiaoDich });
        return rows.FirstOrDefault();
    }

    private static async Task<bool> TableExistsAsync(IDbConnection db, string table)
    {
        var sql = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME=@t";
        var cnt = await db.ExecuteScalarAsync<int>(sql, new { t = table });
        return cnt > 0;
    }

    private static async Task<bool> ColumnExistsAsync(IDbConnection db, string table, string column)
    {
        var sql = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME=@t AND COLUMN_NAME=@c";
        var cnt = await db.ExecuteScalarAsync<int>(sql, new { t = table, c = column });
        return cnt > 0;
    }

    private static int ToInt(object? o)
    {
        if (o == null) return 0;
        if (int.TryParse(o.ToString(), out var v)) return v;
        return 0;
    }

    // Cố gắng ghi vào bảng LichSuHoanTien theo schema hiện có
    private async Task TryInsertRefundHistoryAsync(IDbConnection db, dynamic tx, decimal soTien, string lyDoHoan, bool isMockRefund)
    {
        if (!await TableExistsAsync(db, "LichSuHoanTien")) return; // Bảng không tồn tại -> bỏ qua

        int idThanhToan = 0;
        string? maGiaoDich = null;
        try
        {
            if (tx is IDictionary<string, object> d)
            {
                d.TryGetValue("Id", out var idObj);
                d.TryGetValue("MaGiaoDich", out var mgdObj);
                idThanhToan = ToInt(idObj);
                maGiaoDich = mgdObj?.ToString();
            }
        }
        catch { }

        var cols = new List<string>();
        var vals = new List<string>();
        var param = new DynamicParameters();

        if (await ColumnExistsAsync(db, "LichSuHoanTien", "IdThanhToan"))
        { cols.Add("IdThanhToan"); vals.Add("@IdThanhToan"); param.Add("IdThanhToan", idThanhToan); }
        if (await ColumnExistsAsync(db, "LichSuHoanTien", "SoTienHoan"))
        { cols.Add("SoTienHoan"); vals.Add("@SoTienHoan"); param.Add("SoTienHoan", soTien); }
        if (await ColumnExistsAsync(db, "LichSuHoanTien", "LyDoHoan"))
        { cols.Add("LyDoHoan"); vals.Add("@LyDoHoan"); param.Add("LyDoHoan", lyDoHoan); }
        if (await ColumnExistsAsync(db, "LichSuHoanTien", "TrangThaiHoan"))
        { cols.Add("TrangThaiHoan"); vals.Add("N'Chờ xử lý'"); }
        if (await ColumnExistsAsync(db, "LichSuHoanTien", "NgayHoan"))
        { cols.Add("NgayHoan"); vals.Add("GETDATE()"); }
        if (await ColumnExistsAsync(db, "LichSuHoanTien", "MaGiaoDichHoan"))
        { cols.Add("MaGiaoDichHoan"); vals.Add("@MaGiaoDichHoan"); param.Add("MaGiaoDichHoan", maGiaoDich); }
        if (await ColumnExistsAsync(db, "LichSuHoanTien", "IsMockRefund"))
        { cols.Add("IsMockRefund"); vals.Add("@IsMockRefund"); param.Add("IsMockRefund", isMockRefund ? 1 : 0); }

        if (cols.Count == 0) return; // Không có cột nào phù hợp -> bỏ qua

        var sql = $"INSERT INTO LichSuHoanTien ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})";
        try { await db.ExecuteAsync(sql, param); } catch { /* im lặng nếu schema khác nữa */ }
    }

    // Đồng bộ trạng thái vào nhật ký hoàn tiền (nếu có cột tương ứng)
    private async Task TryUpdateRefundHistoryStatusAsync(IDbConnection db, int idThanhToan, string trangThai, string? ghiChu)
    {
        if (!await TableExistsAsync(db, "LichSuHoanTien")) return;

        var sets = new List<string>();
        var param = new DynamicParameters();
        param.Add("IdThanhToan", idThanhToan);

        if (await ColumnExistsAsync(db, "LichSuHoanTien", "TrangThaiHoan"))
        { sets.Add("TrangThaiHoan=@tt"); param.Add("tt", trangThai); }
        if (await ColumnExistsAsync(db, "LichSuHoanTien", "GhiChu"))
        { sets.Add("GhiChu=@gc"); param.Add("gc", ghiChu); }
        if (await ColumnExistsAsync(db, "LichSuHoanTien", "NgayHoan"))
        { sets.Add("NgayHoan=GETDATE()"); }

        if (sets.Count == 0) return;

        var sql = $"UPDATE LichSuHoanTien SET {string.Join(", ", sets)} WHERE IdThanhToan=@IdThanhToan";
        try { await db.ExecuteAsync(sql, param); } catch { }
    }
}

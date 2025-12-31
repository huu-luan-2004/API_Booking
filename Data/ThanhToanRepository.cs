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

    // Bản mở rộng: tạo giao dịch kèm Meta JSON (nếu bảng có cột Meta)
    public async Task CreateWithMetaAsync(int? idDatPhong, string maGiaoDich, decimal soTien, string phuongThuc, string trangThai, string noiDung, string maDonHang, string loaiGiaoDich, string? meta)
    {
        using var db = _factory.Create();
        try
        {
            // Nếu có cột Meta
            var sqlCheck = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ThanhToan' AND COLUMN_NAME='Meta'";
            var hasMeta = await db.ExecuteScalarAsync<int>(sqlCheck) > 0;
            if (hasMeta)
            {
                var sql = @"INSERT INTO ThanhToan (IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung, MaDonHang, LoaiGiaoDich, Meta, CreatedAt)
                            VALUES (@IdDatPhong, @MaGiaoDich, @SoTien, @PhuongThuc, @TrangThai, @NoiDung, @MaDonHang, @LoaiGiaoDich, @Meta, GETDATE())";
                await db.ExecuteAsync(sql, new { IdDatPhong = idDatPhong, MaGiaoDich = maGiaoDich, SoTien = soTien, PhuongThuc = phuongThuc, TrangThai = trangThai, NoiDung = noiDung, MaDonHang = maDonHang, LoaiGiaoDich = loaiGiaoDich, Meta = meta });
                return;
            }
        }
        catch { }

        // Nếu không có Meta, fallback về CreateAsync thông thường
        await CreateAsync(idDatPhong ?? 0, maGiaoDich, soTien, phuongThuc, trangThai, noiDung, maDonHang, loaiGiaoDich);
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

    public async Task UpdateBookingIdByMaAsync(string maGiaoDich, int idDatPhong)
    {
        using var db = _factory.Create();
        // Cập nhật IdDatPhong nếu cột tồn tại
        try
        {
            await db.ExecuteAsync("UPDATE ThanhToan SET IdDatPhong=@id WHERE MaGiaoDich=@m", new { id = idDatPhong, m = maGiaoDich });
        }
        catch { }
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

    // Lấy danh sách thanh toán theo booking ID cho Admin
    public async Task<IEnumerable<dynamic>> GetByBookingIdAsync(int idDatPhong)
    {
        using var db = _factory.Create();
        try
        {
            var sql = @"
                SELECT 
                    Id, IdDatPhong, MaGiaoDichVnPay as MaGiaoDich, SoTien, 
                    PhuongThucThanhToan as PhuongThuc, NgayThanhToan, TrangThai, 
                    GhiChu, MaDonHang, LoaiGiaoDich, CreatedAt
                FROM ThanhToan 
                WHERE IdDatPhong = @IdDatPhong 
                ORDER BY CreatedAt DESC";
            
            return await db.QueryAsync(sql, new { IdDatPhong = idDatPhong });
        }
        catch
        {
            // Fallback với tên cột khác nếu schema khác
            try
            {
                var sql = @"
                    SELECT 
                        Id, IdDatPhong, MaGiaoDich, SoTien, 
                        PhuongThuc, NgayThanhToan, TrangThai, 
                        NoiDung as GhiChu, MaDonHang, LoaiGiaoDich, CreatedAt
                    FROM ThanhToan 
                    WHERE IdDatPhong = @IdDatPhong 
                    ORDER BY CreatedAt DESC";
                
                return await db.QueryAsync(sql, new { IdDatPhong = idDatPhong });
            }
            catch
            {
                // Fallback cuối cùng - chỉ lấy các cột cơ bản
                var sql = @"
                    SELECT * 
                    FROM ThanhToan 
                    WHERE IdDatPhong = @IdDatPhong 
                    ORDER BY Id DESC";
                
                return await db.QueryAsync(sql, new { IdDatPhong = idDatPhong });
            }
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

    // Lấy báo cáo thanh toán cho Admin
    public async Task<IEnumerable<dynamic>> GetReportAsync(DateTime fromDate, DateTime toDate, string? status = null)
    {
        using var db = _factory.Create();
        
        var whereClause = "WHERE 1=1";
        var param = new DynamicParameters();
        
        // Filter theo ngày tạo (fallback về NgayTao nếu không có CreatedAt)
        if (await ColumnExistsAsync(db, "ThanhToan", "CreatedAt"))
        {
            whereClause += " AND CreatedAt >= @FromDate AND CreatedAt <= @ToDate";
        }
        else if (await ColumnExistsAsync(db, "ThanhToan", "NgayTao"))
        {
            whereClause += " AND NgayTao >= @FromDate AND NgayTao <= @ToDate";
        }
        
        param.Add("FromDate", fromDate);
        param.Add("ToDate", toDate.AddDays(1)); // Include toàn bộ ngày cuối
        
        // Filter theo trạng thái nếu có
        if (!string.IsNullOrWhiteSpace(status))
        {
            whereClause += " AND TrangThai = @Status";
            param.Add("Status", status);
        }
        
        var sql = $@"SELECT Id, IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung, 
                     COALESCE(CreatedAt, NgayTao) as NgayTao
                     FROM ThanhToan {whereClause} 
                     ORDER BY COALESCE(CreatedAt, NgayTao, Id) DESC";
        
        try
        {
            return await db.QueryAsync(sql, param);
        }
        catch
        {
            // Fallback query cho bảng tối giản
            var sqlMinimal = $@"SELECT Id, IdDatPhong, MaGiaoDich, SoTien, PhuongThuc, TrangThai, NoiDung, 
                               NULL as NgayTao FROM ThanhToan {whereClause.Replace("CreatedAt", "Id").Replace("NgayTao", "Id")} 
                               ORDER BY Id DESC";
            return await db.QueryAsync(sqlMinimal, new { FromDate = fromDate, ToDate = toDate, Status = status });
        }
    }

    // Thống kê doanh thu theo tháng
    public async Task<dynamic> GetRevenueStatsAsync(int year, int month)
    {
        using var db = _factory.Create();
        
        var fromDate = new DateTime(year, month, 1);
        var toDate = fromDate.AddMonths(1).AddDays(-1);
        
        var sql = @"SELECT 
                     COUNT(*) as TotalTransactions,
                     COALESCE(SUM(CASE WHEN TrangThai = N'Thanh toán thành công' THEN SoTien ELSE 0 END), 0) as TotalRevenue,
                     COUNT(CASE WHEN TrangThai = N'Thanh toán thành công' THEN 1 END) as SuccessfulTransactions,
                     COUNT(CASE WHEN TrangThai = N'Chờ thanh toán' THEN 1 END) as PendingTransactions,
                     COUNT(CASE WHEN TrangThai = N'Thanh toán thất bại' THEN 1 END) as FailedTransactions,
                     AVG(CASE WHEN TrangThai = N'Thanh toán thành công' THEN SoTien END) as AverageRevenue
                     FROM ThanhToan 
                     WHERE ";
        
        // Sử dụng cột ngày phù hợp
        if (await ColumnExistsAsync(db, "ThanhToan", "CreatedAt"))
        {
            sql += "CreatedAt >= @FromDate AND CreatedAt <= @ToDate";
        }
        else if (await ColumnExistsAsync(db, "ThanhToan", "NgayTao"))
        {
            sql += "NgayTao >= @FromDate AND NgayTao <= @ToDate";
        }
        else
        {
            // Fallback: lấy tất cả và filter về application layer
            sql += "1=1";
        }
        
        try
        {
            var result = await db.QueryFirstOrDefaultAsync(sql, new { FromDate = fromDate, ToDate = toDate.AddDays(1) });
            return new
            {
                year,
                month,
                fromDate,
                toDate,
                totalTransactions = result?.TotalTransactions ?? 0,
                totalRevenue = result?.TotalRevenue ?? 0,
                successfulTransactions = result?.SuccessfulTransactions ?? 0,
                pendingTransactions = result?.PendingTransactions ?? 0,
                failedTransactions = result?.FailedTransactions ?? 0,
                averageRevenue = result?.AverageRevenue ?? 0,
                successRate = (result?.TotalTransactions ?? 0) > 0 ? 
                    (double)(result?.SuccessfulTransactions ?? 0) / (result?.TotalTransactions ?? 0) * 100 : 0
            };
        }
        catch
        {
            // Return empty stats on error
            return new
            {
                year, month, fromDate, toDate,
                totalTransactions = 0, totalRevenue = 0, successfulTransactions = 0,
                pendingTransactions = 0, failedTransactions = 0, averageRevenue = 0, successRate = 0.0
            };
        }
    }

    // 💰 Lấy dữ liệu giao dịch cho tính doanh thu app (10% hoa hồng)
    public async Task<List<dynamic>> GetAppRevenueAsync(DateTime fromDate, DateTime toDate)
    {
        using var db = _factory.Create();
        
        try
        {
            // Kiểm tra schema database trước
            bool hasCreatedAt = await ColumnExistsAsync(db, "ThanhToan", "CreatedAt");
            bool hasNgayTao = await ColumnExistsAsync(db, "ThanhToan", "NgayTao");
            bool hasNgayThanhToan = await ColumnExistsAsync(db, "ThanhToan", "NgayThanhToan");
            bool hasMaDonHang = await ColumnExistsAsync(db, "ThanhToan", "MaDonHang");
            bool hasLoaiGiaoDich = await ColumnExistsAsync(db, "ThanhToan", "LoaiGiaoDich");
            
            // Kiểm tra cột trong bảng DatPhong
            bool hasIdCoSoInDatPhong = await ColumnExistsAsync(db, "DatPhong", "IdCoSoLuuTru");

            // Xây dựng câu SQL linh hoạt theo schema
            var selectColumns = "t.Id, t.IdDatPhong, t.SoTien, t.TrangThai, t.PhuongThuc";
            
            // Ưu tiên NgayTao (từ ảnh thấy bảng ThanhToan có NgayTao)
            if (hasNgayTao)
                selectColumns += ", t.NgayTao AS NgayThanhToan";
            else if (hasNgayThanhToan)
                selectColumns += ", t.NgayThanhToan";
            else if (hasCreatedAt)
                selectColumns += ", t.CreatedAt AS NgayThanhToan";
            else 
                selectColumns += ", GETDATE() AS NgayThanhToan";

            if (hasMaDonHang)
                selectColumns += ", t.MaDonHang";
            else
                selectColumns += ", 'N/A' AS MaDonHang";

            if (hasLoaiGiaoDich)
                selectColumns += ", t.LoaiGiaoDich";
            else
                selectColumns += ", 'Booking' AS LoaiGiaoDich";

            string sql;
            if (hasIdCoSoInDatPhong)
            {
                // Có thể JOIN với DatPhong và CoSoLuuTru
                selectColumns += @", d.IdCoSoLuuTru,
                                  COALESCE(c.TenCoSo, 'Không xác định') AS TenCoSo";

                sql = $@"
                    SELECT {selectColumns}
                    FROM ThanhToan t
                    INNER JOIN DatPhong d ON t.IdDatPhong = d.Id  
                    LEFT JOIN CoSoLuuTru c ON d.IdCoSoLuuTru = c.Id
                    WHERE t.TrangThai IN ('Thành công', 'Đã thanh toán', 'Completed', 'SUCCESS')
                ";
            }
            else
            {
                // Không JOIN, chỉ lấy từ ThanhToan
                selectColumns += ", NULL AS IdCoSoLuuTru, 'Không xác định' AS TenCoSo";
                sql = $@"
                    SELECT {selectColumns}
                    FROM ThanhToan t
                    WHERE t.TrangThai IN ('Thành công', 'Đã thanh toán', 'Completed', 'SUCCESS')
                ";
            }

            // Thêm điều kiện thời gian - ưu tiên NgayTao
            if (hasNgayTao)
            {
                sql += " AND t.NgayTao >= @FromDate AND t.NgayTao < @ToDateEnd";
            }
            else if (hasNgayThanhToan)
            {
                sql += " AND t.NgayThanhToan >= @FromDate AND t.NgayThanhToan < @ToDateEnd";
            }
            else if (hasCreatedAt)
            {
                sql += " AND t.CreatedAt >= @FromDate AND t.CreatedAt < @ToDateEnd";
            }
            
            sql += " ORDER BY ";
            if (hasNgayTao)
                sql += "t.NgayTao DESC";
            else if (hasNgayThanhToan)
                sql += "t.NgayThanhToan DESC";
            else if (hasCreatedAt)
                sql += "t.CreatedAt DESC";
            else
                sql += "t.Id DESC";

            var result = await db.QueryAsync(sql, new { 
                FromDate = fromDate, 
                ToDateEnd = toDate.AddDays(1) // Include toDate
            });
            
            return result.ToList();
        }
        catch (Exception ex)
        {
            // Fallback: chỉ lấy từ bảng ThanhToan, không JOIN
            Console.WriteLine($"⚠️ GetAppRevenueAsync error: {ex.Message}");
            
            var simpleSql = @"
                SELECT t.Id, t.IdDatPhong, t.SoTien, t.TrangThai, t.PhuongThuc,
                       GETDATE() AS NgayThanhToan, 'N/A' AS MaDonHang, 'Booking' AS LoaiGiaoDich,
                       NULL AS IdCoSoLuuTru, 'Không xác định' AS TenCoSo
                FROM ThanhToan t
                WHERE t.TrangThai IN ('Thành công', 'Đã thanh toán', 'Completed', 'SUCCESS')
                ORDER BY t.Id DESC";
            
            var fallbackResult = await db.QueryAsync(simpleSql);
            return fallbackResult.ToList();
        }
    }
}

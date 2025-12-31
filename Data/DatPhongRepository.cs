using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace HotelBookingApi.Data;

public class DatPhongRepository
{
    private readonly SqlConnectionFactory _factory;
    public DatPhongRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<int> CreateAsync(int idNguoiDung, int idPhong, DateTime ngayNhanPhong, DateTime ngayTraPhong, decimal tongTien)
    {
        using var conn = (SqlConnection)_factory.Create();
        await conn.OpenAsync();
    using var tx = conn.BeginTransaction(IsolationLevel.Serializable);

        // 1) Khoá theo phòng
        //    - Ưu tiên dùng sp_getapplock để có lock tên theo IdPhong.
        //    - Nếu không khả dụng (thiếu quyền/timeout), fallback về khoá hàng trên bảng Phong (UPDLOCK, HOLDLOCK) theo Id phòng.
        var resource = $"room-{idPhong}";
        var lockSql = @"DECLARE @r INT; EXEC @r = sp_getapplock @Resource=@res, @LockMode='Exclusive', @LockOwner='Transaction', @Timeout=60000; SELECT @r";
        int lockRes;
        try
        {
            lockRes = await conn.ExecuteScalarAsync<int>(lockSql, new { res = resource }, tx);
        }
        catch
        {
            // Nếu không thể gọi sp_getapplock (không tồn tại/thiếu quyền), coi như thất bại để dùng fallback
            lockRes = -999;
        }
        if (lockRes < 0)
        {
            // Fallback: khóa cục bộ trên bản ghi phòng để tuần tự hoá các giao dịch theo phòng
            await conn.ExecuteAsync("SELECT 1 FROM Phong WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id", new { id = idPhong }, tx);
        }

        // 2) Xác định trạng thái khởi tạo: ChoThanhToanCoc
        int statusId = 1; // fallback mặc định
        try
        {
            var maybe = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM TrangThaiDatPhong WHERE MaTrangThai=@m OR TenTrangThai=@m",
                new { m = "ChoThanhToanCoc" }, tx
            );
            if (maybe.HasValue) statusId = maybe.Value;
        }
        catch { /* nếu bảng không tồn tại hoặc khác schema, giữ mặc định */ }

        // 3) Kiểm tra trùng lịch NGAY TRONG TRANSACTION với quy tắc hold 15 phút
                var conflictSql = @"
                        SELECT COUNT(1)
                        FROM DatPhong dp WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
            WHERE dp.IdPhong = @idPhong
              AND (
                    tt.MaTrangThai IN (N'DaCoc', N'ChoCheckIn', N'DaThanhToanDayDu', N'DaNhanPhong')
                    OR (tt.MaTrangThai = N'ChoThanhToanCoc' AND dp.CreatedAt >= DATEADD(MINUTE, -15, GETDATE()))
                  )
              AND NOT (
                    @tra <= dp.NgayNhanPhong
                    OR @nhan >= dp.NgayTraPhong
                  )";

        int conflicts;
        try
        {
            conflicts = await conn.ExecuteScalarAsync<int>(conflictSql, new { idPhong, nhan = ngayNhanPhong, tra = ngayTraPhong }, tx);
        }
        catch
        {
            // Fallback nếu không có CreatedAt
                        var conflictNoHold = @"
                                SELECT COUNT(1)
                                FROM DatPhong dp WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
                WHERE dp.IdPhong = @idPhong
                  AND tt.MaTrangThai IN (N'DaCoc', N'ChoCheckIn', N'DaThanhToanDayDu', N'DaNhanPhong')
                  AND NOT (
                        @tra <= dp.NgayNhanPhong
                        OR @nhan >= dp.NgayTraPhong
                      )";
            conflicts = await conn.ExecuteScalarAsync<int>(conflictNoHold, new { idPhong, nhan = ngayNhanPhong, tra = ngayTraPhong }, tx);
        }

        if (conflicts > 0)
            throw new Exception("Phòng vừa được giữ/đặt trong cùng khoảng thời gian. Vui lòng chọn thời gian khác.");

        // 4) Chèn bản ghi đặt phòng
        var sql = @"INSERT INTO DatPhong (IdNguoiDung, IdPhong, NgayDat, NgayNhanPhong, NgayTraPhong, IdTrangThai, CreatedAt, TongTienTamTinh)
                    OUTPUT INSERTED.Id
                    VALUES (@idNguoiDung, @idPhong, GETDATE(), @ngayNhan, @ngayTra, @statusId, GETDATE(), @tong)";
        var newId = await conn.ExecuteScalarAsync<int>(sql, new { idNguoiDung, idPhong, ngayNhan = ngayNhanPhong, ngayTra = ngayTraPhong, statusId, tong = tongTien }, tx);

        await tx.CommitAsync();
        return newId;
    }

    public async Task<dynamic?> GetByIdAsync(int id)
    {
        using var db = _factory.Create();
        var sql = @"SELECT dp.*, nd.HoTen AS ND_HoTen, nd.Email AS ND_Email, nd.SoDienThoai AS ND_SoDienThoai,
                           p.TenPhong AS P_TenPhong, cs.TenCoSo AS CS_TenCoSo, tt.TenTrangThai AS TT_TenTrangThai, tt.MaTrangThai AS TT_MaTrangThai
                    FROM DatPhong dp
                    LEFT JOIN NguoiDung nd ON dp.IdNguoiDung = nd.Id
                    LEFT JOIN Phong p ON dp.IdPhong = p.Id
                    LEFT JOIN CoSoLuuTru cs ON p.IdCoSoLuuTru = cs.Id
                    LEFT JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
                    WHERE dp.Id=@id";
        var rows = await db.QueryAsync(sql, new { id });
        return rows.FirstOrDefault();
    }

    public async Task<IEnumerable<dynamic>> ListByUserAsync(int idNguoiDung)
    {
        using var db = _factory.Create();
        var sql = @"SELECT dp.*, p.TenPhong, p.SoNguoiToiDa, tt.TenTrangThai, tt.MaTrangThai, cs.TenCoSo
                    FROM DatPhong dp
                    JOIN Phong p ON dp.IdPhong=p.Id
                    JOIN TrangThaiDatPhong tt ON dp.IdTrangThai=tt.Id
                    JOIN CoSoLuuTru cs ON p.IdCoSoLuuTru = cs.Id
                    WHERE dp.IdNguoiDung=@id
                    ORDER BY dp.CreatedAt DESC";
        return await db.QueryAsync(sql, new { id = idNguoiDung });
    }

    public async Task UpdateTrangThaiAsync(int id, object trangThai)
    {
        using var db = _factory.Create();
        // Ưu tiên map mã/tên sang IdTrangThai để phù hợp schema chuẩn
        if (trangThai is int idTT)
        {
            await db.ExecuteAsync("UPDATE DatPhong SET IdTrangThai=@tt WHERE Id=@id", new { id, tt = idTT });
            return;
        }

        var text = trangThai?.ToString() ?? string.Empty;
        // Thử map trực tiếp theo Ma/Ten
        var mapId = await db.ExecuteScalarAsync<int?>(
            "SELECT TOP 1 Id FROM TrangThaiDatPhong WHERE TenTrangThai=@t OR MaTrangThai=@t",
            new { t = text }
        );
        if (mapId.HasValue)
        {
            await db.ExecuteAsync("UPDATE DatPhong SET IdTrangThai=@tt WHERE Id=@id", new { id, tt = mapId.Value });
            return;
        }

        // Fallback: cố gắng map bằng bộ từ đồng nghĩa (không dấu, có dấu)
        static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var noDiacritics = s.Replace("đ","d").Replace("Đ","D").Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var ch in noDiacritics)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return new string(sb.ToString().Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        }

        var key = Normalize(text);
        var candidates = new List<string> { text };
        void Add(params string[] more) { foreach (var m in more) if (!string.IsNullOrWhiteSpace(m)) candidates.Add(m); }

        if (key.Contains("coc")) Add("DaCoc", "Đã cọc", "Da Coc", "Đa coc");
        if (key.Contains("thanh toan") && (key.Contains("day du") || key.Contains("du"))) Add("DaThanhToanDayDu", "Đã thanh toán đầy đủ", "Da thanh toan day du");
        if (key.Contains("cho checkin") || key.Contains("cho nhan phong")) Add("ChoCheckIn", "Chờ check-in", "Chờ nhận phòng");
        if (key.Contains("da nhan phong") || key.Contains("nhan phong")) Add("DaNhanPhong", "Đã nhận phòng");
        if (key.Contains("huy phong") || key.Contains("da huy") || key.Contains("huy")) Add("HuyPhong", "Đã hủy", "Hủy phòng");
        if (key.Contains("hoan tat") || key.Contains("checkout") || key.Contains("tra phong")) Add("HoanTat", "Hoàn tất");
        if (key.Contains("yeu cau huy") || key.Contains("yeu cau huy phong")) Add("YEU_CAU_HUY", "Yêu cầu hủy");
        if (key.Contains("no show") || key.Contains("khong den")) Add("NoShow", "No Show");

        candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var mapId2 = await db.ExecuteScalarAsync<int?>(
            "SELECT TOP 1 Id FROM TrangThaiDatPhong WHERE MaTrangThai IN @list OR TenTrangThai IN @list",
            new { list = candidates }
        );
        if (mapId2.HasValue)
        {
            await db.ExecuteAsync("UPDATE DatPhong SET IdTrangThai=@tt WHERE Id=@id", new { id, tt = mapId2.Value });
            return;
        }

        // Không map được -> bỏ qua để tránh lỗi ở schema khác nhau; có thể log lại nếu cần
    }

    public async Task<bool> CheckAvailabilityAsync(int idPhong, DateTime nhan, DateTime tra)
    {
        using var db = _factory.Create();
        // Quy tắc chặn trùng lịch:
        // - Luôn chặn nếu trạng thái đã cọc / chờ check-in / đã thanh toán đủ / đã nhận phòng
        // - Tạm giữ (hold) khi trạng thái 'ChoThanhToanCoc' TRONG VÒNG 15 PHÚT kể từ khi tạo (CreatedAt)
        //   → nếu chưa quá 15 phút, coi như đang khoá lịch để chờ thanh toán; quá 15 phút thì bỏ qua.

        var sqlWithHold = @"
            SELECT COUNT(1)
            FROM DatPhong dp
            INNER JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
            WHERE dp.IdPhong = @idPhong
              AND (
                    tt.MaTrangThai IN (N'DaCoc', N'ChoCheckIn', N'DaThanhToanDayDu', N'DaNhanPhong')
                    OR (tt.MaTrangThai = N'ChoThanhToanCoc' AND dp.CreatedAt >= DATEADD(MINUTE, -15, GETDATE()))
                  )
              AND NOT (
                    @tra <= dp.NgayNhanPhong -- khoảng [nhan,tra) không giao nhau
                    OR @nhan >= dp.NgayTraPhong
                  )";

        try
        {
            var conflictCount = await db.ExecuteScalarAsync<int>(sqlWithHold, new { idPhong, nhan, tra });
            // Bổ sung: Kiểm tra hold chủ động từ bảng PreBookingHold
            int holdCnt = 0;
            try
            {
                var holdSql = @"
                    SELECT COUNT(1)
                    FROM PreBookingHold
                    WHERE IdPhong=@idPhong AND ExpiresAt > GETDATE()
                      AND NOT (@tra <= NgayNhanPhong OR @nhan >= NgayTraPhong)";
                holdCnt = await db.ExecuteScalarAsync<int>(holdSql, new { idPhong, nhan, tra });
            }
            catch { }
            return (conflictCount + holdCnt) == 0;
        }
        catch
        {
            // Fallback nếu schema không có CreatedAt: bỏ điều kiện hold theo thời gian, chỉ chặn theo các trạng thái cứng
            var sqlNoHold = @"
                SELECT COUNT(1)
                FROM DatPhong dp
                INNER JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
                WHERE dp.IdPhong = @idPhong
                  AND tt.MaTrangThai IN (N'DaCoc', N'ChoCheckIn', N'DaThanhToanDayDu', N'DaNhanPhong')
                  AND NOT (
                        @tra <= dp.NgayNhanPhong
                        OR @nhan >= dp.NgayTraPhong
                      )";
            var conflictCount = await db.ExecuteScalarAsync<int>(sqlNoHold, new { idPhong, nhan, tra });
            int holdCnt = 0;
            try
            {
                var holdSql = @"
                    SELECT COUNT(1)
                    FROM PreBookingHold
                    WHERE IdPhong=@idPhong AND ExpiresAt > GETDATE()
                      AND NOT (@tra <= NgayNhanPhong OR @nhan >= NgayTraPhong)";
                holdCnt = await db.ExecuteScalarAsync<int>(holdSql, new { idPhong, nhan, tra });
            }
            catch { }
            return (conflictCount + holdCnt) == 0;
        }
    }

    // Xoá các đơn đặt phòng chưa thanh toán đã quá thời gian giữ chỗ (mặc định 15 phút)
    // Điều kiện xoá:
    // - Trạng thái 'ChoThanhToanCoc'
    // - CreatedAt < NOW - @minutes
    // - Chưa có giao dịch thanh toán thành công liên quan
    public async Task<int> PurgeExpiredUnpaidAsync(int minutes = 15)
    {
        using var db = _factory.Create();
        var sql = @"
            DELETE dp
            FROM DatPhong dp
            LEFT JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
            LEFT JOIN (
                SELECT DISTINCT IdDatPhong
                FROM ThanhToan
                WHERE TrangThai = N'Thành công'
            ) pay ON pay.IdDatPhong = dp.Id
            WHERE tt.MaTrangThai = N'ChoThanhToanCoc'
              AND dp.CreatedAt < DATEADD(MINUTE, -@minutes, GETDATE())
              AND pay.IdDatPhong IS NULL";
        try
        {
            var affected = await db.ExecuteAsync(sql, new { minutes });
            return affected;
        }
        catch
        {
            // Nếu schema khác (không có CreatedAt hoặc cột TrangThai dạng text), thử bản fall-back:
            var sqlFallback = @"
                DELETE dp
                FROM DatPhong dp
                LEFT JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
                LEFT JOIN (
                    SELECT DISTINCT IdDatPhong FROM ThanhToan WHERE TrangThai = N'Thành công'
                ) pay ON pay.IdDatPhong = dp.Id
                WHERE (tt.MaTrangThai = N'ChoThanhToanCoc' OR tt.TenTrangThai = N'Chờ thanh toán cọc')
                  AND pay.IdDatPhong IS NULL";
            return await db.ExecuteAsync(sqlFallback);
        }
    }

    // Lấy danh sách đặt phòng với phân trang và bộ lọc
    public async Task<(IEnumerable<dynamic> bookings, int total)> ListAsync(int page = 1, int pageSize = 20, string? status = null, int? userId = null)
    {
        using var db = _factory.Create();
        
        try 
        {
            // Đơn giản hóa query để debug - chỉ lấy từ DatPhong
            var whereClause = "WHERE 1=1";
            var param = new DynamicParameters();
            
            // Filter theo user ID nếu có
            if (userId.HasValue)
            {
                whereClause += " AND IdNguoiDung = @UserId";
                param.Add("UserId", userId.Value);
            }
            
            // Count total records
            var countSql = $"SELECT COUNT(*) FROM DatPhong {whereClause}";
            var total = await db.QuerySingleAsync<int>(countSql, param);
            
            // Get paginated data - sử dụng query đơn giản
            var offset = (page - 1) * pageSize;
            param.Add("Offset", offset);
            param.Add("PageSize", pageSize);
            
            var dataSql = $@"SELECT Id, IdPhong, IdNguoiDung, NgayNhanPhong, NgayTraPhong, 
                            TongTienTamTinh as TongTien, IdTrangThai as TrangThai, NgayDat
                            FROM DatPhong 
                            {whereClause}
                            ORDER BY Id DESC
                            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
            
            var bookings = await db.QueryAsync(dataSql, param);
            return (bookings, total);
        }
        catch (Exception ex)
        {
            // Log error và return empty thay vì crash
            System.Diagnostics.Debug.WriteLine($"DatPhong ListAsync error: {ex.Message}");
            return (new List<dynamic>(), 0);
        }
    }

    // Lấy danh sách đặt phòng với thông tin chi tiết cho Admin
    public async Task<(IEnumerable<dynamic> bookings, int total)> ListWithDetailsAsync(int page = 1, int pageSize = 50, string? status = null, int? userId = null, int? accommodationId = null)
    {
        using var db = _factory.Create();
        
        try 
        {
            var whereClause = "WHERE 1=1";
            var param = new DynamicParameters();
            
            // Filters
            if (userId.HasValue)
            {
                whereClause += " AND dp.IdNguoiDung = @UserId";
                param.Add("UserId", userId.Value);
            }
            
            if (!string.IsNullOrEmpty(status))
            {
                whereClause += " AND (tt.MaTrangThai = @Status OR tt.TenTrangThai = @Status OR CAST(dp.IdTrangThai AS NVARCHAR) = @Status)";
                param.Add("Status", status);
            }
            
            if (accommodationId.HasValue)
            {
                whereClause += " AND p.IdCoSoLuuTru = @AccommodationId";
                param.Add("AccommodationId", accommodationId.Value);
            }
            
            // Count total records
            var countSql = $@"
                SELECT COUNT(*) 
                FROM DatPhong dp
                LEFT JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
                LEFT JOIN Phong p ON dp.IdPhong = p.Id
                {whereClause}";
            var total = await db.QuerySingleAsync<int>(countSql, param);
            
            // Get paginated data with joins
            var offset = (page - 1) * pageSize;
            param.Add("Offset", offset);
            param.Add("PageSize", pageSize);
            
            var dataSql = $@"
                SELECT 
                    dp.Id, dp.IdPhong, dp.IdNguoiDung, dp.NgayNhanPhong, dp.NgayTraPhong,
                    dp.TongTienTamTinh as TongTien, dp.NgayDat, dp.GhiChu, dp.CreatedAt, dp.UpdatedAt,
                    dp.IdTrangThai, 
                    tt.MaTrangThai as TT_MaTrangThai, tt.TenTrangThai as TT_TenTrangThai,
                    
                    -- Thông tin khách hàng
                    nd.HoTen as ND_HoTen, nd.Email as ND_Email, nd.SoDienThoai as ND_SoDienThoai, 
                    nd.Avatar as ND_Avatar, nd.NgayTao as ND_NgayTao, nd.TrangThai as ND_TrangThai,
                    
                    -- Thông tin phòng
                    p.TenPhong as P_TenPhong, p.LoaiPhong as P_LoaiPhong, p.DienTich as P_DienTich,
                    p.SoLuongKhach as P_SoLuongKhach, p.Gia as P_Gia, p.MoTa as P_MoTa, p.HinhAnh as P_HinhAnh,
                    p.IdCoSoLuuTru,
                    
                    -- Thông tin cơ sở lưu trú
                    cs.TenCoSo as CS_TenCoSo, cs.DiaChi as CS_DiaChi, cs.SoDienThoai as CS_SoDienThoai,
                    cs.Email as CS_Email, cs.Website as CS_Website, cs.MoTa as CS_MoTa,
                    
                    -- Thông tin thanh toán (nếu có)
                    CASE WHEN EXISTS (SELECT 1 FROM ThanhToan tt2 WHERE tt2.IdDatPhong = dp.Id AND tt2.TrangThai = N'Thành công') 
                         THEN 1 ELSE 0 END as DaThanhToan,
                    (SELECT TOP 1 PhuongThucThanhToan FROM ThanhToan tt3 WHERE tt3.IdDatPhong = dp.Id ORDER BY NgayThanhToan DESC) as PhuongThucThanhToan,
                    (SELECT TOP 1 NgayThanhToan FROM ThanhToan tt4 WHERE tt4.IdDatPhong = dp.Id AND tt4.TrangThai = N'Thành công' ORDER BY NgayThanhToan DESC) as NgayThanhToan
                FROM DatPhong dp
                LEFT JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
                LEFT JOIN NguoiDung nd ON dp.IdNguoiDung = nd.Id
                LEFT JOIN Phong p ON dp.IdPhong = p.Id
                LEFT JOIN CoSoLuuTru cs ON p.IdCoSoLuuTru = cs.Id
                {whereClause}
                ORDER BY dp.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
            
            var bookings = await db.QueryAsync(dataSql, param);
            return (bookings, total);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DatPhong ListWithDetailsAsync error: {ex.Message}");
            return (new List<dynamic>(), 0);
        }
    }

    // Lấy chi tiết một booking với đầy đủ thông tin
    public async Task<dynamic?> GetByIdWithFullDetailsAsync(int id)
    {
        using var db = _factory.Create();
        
        try
        {
            var sql = @"
                SELECT 
                    dp.Id, dp.IdPhong, dp.IdNguoiDung, dp.NgayNhanPhong, dp.NgayTraPhong,
                    dp.TongTienTamTinh as TongTien, dp.NgayDat, dp.GhiChu, dp.CreatedAt, dp.UpdatedAt,
                    dp.IdTrangThai, 
                    tt.MaTrangThai as TT_MaTrangThai, tt.TenTrangThai as TT_TenTrangThai,
                    
                    -- Thông tin khách hàng
                    nd.HoTen as ND_HoTen, nd.Email as ND_Email, nd.SoDienThoai as ND_SoDienThoai, 
                    nd.Avatar as ND_Avatar, nd.NgayTao as ND_NgayTao, nd.TrangThai as ND_TrangThai,
                    
                    -- Thông tin phòng
                    p.TenPhong as P_TenPhong, p.LoaiPhong as P_LoaiPhong, p.DienTich as P_DienTich,
                    p.SoLuongKhach as P_SoLuongKhach, p.Gia as P_Gia, p.MoTa as P_MoTa, p.HinhAnh as P_HinhAnh,
                    p.IdCoSoLuuTru,
                    
                    -- Thông tin cơ sở lưu trú
                    cs.TenCoSo as CS_TenCoSo, cs.DiaChi as CS_DiaChi, cs.SoDienThoai as CS_SoDienThoai,
                    cs.Email as CS_Email, cs.Website as CS_Website, cs.MoTa as CS_MoTa
                FROM DatPhong dp
                LEFT JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
                LEFT JOIN NguoiDung nd ON dp.IdNguoiDung = nd.Id
                LEFT JOIN Phong p ON dp.IdPhong = p.Id
                LEFT JOIN CoSoLuuTru cs ON p.IdCoSoLuuTru = cs.Id
                WHERE dp.Id = @Id";
            
            return await db.QueryFirstOrDefaultAsync(sql, new { Id = id });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DatPhong GetByIdWithFullDetailsAsync error: {ex.Message}");
            return null;
        }
    }

    // Lấy thống kê trạng thái booking cho Admin
    public async Task<dynamic> GetBookingStatusCountsAsync()
    {
        using var db = _factory.Create();
        
        try
        {
            var sql = @"
                SELECT 
                    tt.MaTrangThai,
                    tt.TenTrangThai,
                    COUNT(dp.Id) as SoLuong,
                    SUM(dp.TongTienTamTinh) as TongTien
                FROM TrangThaiDatPhong tt
                LEFT JOIN DatPhong dp ON tt.Id = dp.IdTrangThai
                GROUP BY tt.Id, tt.MaTrangThai, tt.TenTrangThai
                ORDER BY COUNT(dp.Id) DESC";
            
            var results = await db.QueryAsync(sql);
            return results;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DatPhong GetBookingStatusCountsAsync error: {ex.Message}");
            return new List<dynamic>();
        }
    }
}

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
            return conflictCount == 0;
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
            return conflictCount == 0;
        }
    }
}

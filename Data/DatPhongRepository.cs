using Dapper;

namespace HotelBookingApi.Data;

public class DatPhongRepository
{
    private readonly SqlConnectionFactory _factory;
    public DatPhongRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<int> CreateAsync(int idNguoiDung, int idPhong, DateTime ngayNhanPhong, DateTime ngayTraPhong, decimal tongTien)
    {
        using var db = _factory.Create();
        // Xác định trạng thái khởi tạo: Chờ thanh toán cọc (theo bảng TrangThaiDatPhong)
        int statusId = 1; // fallback mặc định
        try
        {
            var maybe = await db.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM TrangThaiDatPhong WHERE MaTrangThai=@m OR TenTrangThai=@m",
                new { m = "ChoThanhToanCoc" }
            );
            if (maybe.HasValue) statusId = maybe.Value;
        }
        catch { /* nếu bảng không tồn tại hoặc khác schema, giữ mặc định */ }

        var sql = @"INSERT INTO DatPhong (IdNguoiDung, IdPhong, NgayDat, NgayNhanPhong, NgayTraPhong, IdTrangThai, CreatedAt, TongTienTamTinh)
                    OUTPUT INSERTED.Id
                    VALUES (@idNguoiDung, @idPhong, GETDATE(), @ngayNhan, @ngayTra, @statusId, GETDATE(), @tong)";
        return await db.ExecuteScalarAsync<int>(sql, new { idNguoiDung, idPhong, ngayNhan = ngayNhanPhong, ngayTra = ngayTraPhong, statusId, tong = tongTien });
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
        // Trùng lịch nếu đã cọc/đang chờ check-in/đã thanh toán đủ/đã nhận phòng
        // Các mã trạng thái lấy theo bảng do bạn cung cấp
        var sql = @"SELECT COUNT(1) FROM DatPhong dp
                    INNER JOIN TrangThaiDatPhong tt ON dp.IdTrangThai = tt.Id
                    WHERE dp.IdPhong = @idPhong 
                    AND tt.MaTrangThai IN (N'DaCoc', N'ChoCheckIn', N'DaThanhToanDayDu', N'DaNhanPhong')
                    AND NOT (
                        @tra <= dp.NgayNhanPhong OR -- Trả trước ngày nhận của booking khác
                        @nhan >= dp.NgayTraPhong    -- Nhận sau ngày trả của booking khác
                    )";
        var conflictCount = await db.ExecuteScalarAsync<int>(sql, new { idPhong, nhan, tra });
        return conflictCount == 0;
    }
}

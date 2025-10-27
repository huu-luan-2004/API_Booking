using Dapper;

namespace HotelBookingApi.Data;

public class PhongRepository
{
    private readonly SqlConnectionFactory _factory;
    public PhongRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<IEnumerable<dynamic>> GetAllAsync(int page, int pageSize, string? q)
    {
        using var db = _factory.Create();
        var offset = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
        var size = Math.Max(1, pageSize);
        var kw = (q ?? string.Empty).Trim();
        var where = string.IsNullOrEmpty(kw) ? string.Empty : " WHERE p.TenPhong LIKE @kw ";
        var sql = $@"
            SELECT 
                p.Id, p.TenPhong, p.MoTa, p.SoNguoiToiDa, p.IdCoSoLuuTru, p.IdLoaiPhong,
                p.Anh,
                gp.GiaPhong AS Gia, gp.NgayApDung AS NgayApDungGia,
                d.Id AS IdDiaChi,
                d.ChiTiet, d.Pho, d.Phuong, d.Nuoc, d.KinhDo, d.ViDo
            FROM Phong p
            LEFT JOIN CoSoLuuTru c ON c.Id = p.IdCoSoLuuTru
            LEFT JOIN DiaChiChiTiet d ON d.Id = c.IdDiaChi
            OUTER APPLY (
                SELECT TOP 1 g.GiaPhong, g.NgayApDung
                FROM GiaPhong g
                WHERE g.IdPhong = p.Id
                ORDER BY g.NgayApDung DESC
            ) gp
            {where}
            ORDER BY p.Id DESC
            OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";
        return await db.QueryAsync(sql, new { kw = $"%{kw}%", offset, size });
    }

    public async Task<dynamic?> GetByIdAsync(int id)
    {
        using var db = _factory.Create();
        var sql = @"
            SELECT TOP 1 
                p.Id, p.TenPhong, p.MoTa, p.SoNguoiToiDa, p.IdCoSoLuuTru, p.IdLoaiPhong,
                p.Anh,
                gp.GiaPhong AS Gia, gp.NgayApDung AS NgayApDungGia,
                d.Id AS IdDiaChi,
                d.ChiTiet, d.Pho, d.Phuong, d.Nuoc, d.KinhDo, d.ViDo
            FROM Phong p
            LEFT JOIN CoSoLuuTru c ON c.Id = p.IdCoSoLuuTru
            LEFT JOIN DiaChiChiTiet d ON d.Id = c.IdDiaChi
            OUTER APPLY (
                SELECT TOP 1 g.GiaPhong, g.NgayApDung
                FROM GiaPhong g
                WHERE g.IdPhong = p.Id
                ORDER BY g.NgayApDung DESC
            ) gp
            WHERE p.Id=@id";
        var rows = await db.QueryAsync(sql, new { id });
        return rows.FirstOrDefault();
    }

    public async Task<dynamic> CreateAsync(dynamic body)
    {
        using var db = _factory.Create();
        var columns = await db.QueryAsync<string>("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Phong'");
        var names = new HashSet<string>(columns);
        var cols = new List<string>();
        var vals = new List<string>();
        var p = new DynamicParameters();
        void add(string col, object? val)
        {
            if (!names.Contains(col)) return;
            cols.Add(col); vals.Add("@" + col); p.Add(col, val);
        }
        // minimal fields
        add("TenPhong", (string?)body?.tenPhong);
        add("MoTa", (string?)body?.moTa);
        add("SoNguoiToiDa", (int?)body?.soNguoiToiDa);
        add("IdCoSoLuuTru", (int?)body?.idCoSoLuuTru);
        add("IdLoaiPhong", (int?)body?.idLoaiPhong);
        // image field - direct database storage
        try { add("Anh", (string?)body?.anh); } catch { }
        if (names.Contains("CreatedAt")) add("CreatedAt", DateTime.UtcNow);
        else if (names.Contains("NgayTao")) add("NgayTao", DateTime.UtcNow);

        if (!cols.Any()) throw new Exception("Không có cột hợp lệ để chèn vào Phong");
        var isIdentity = await db.ExecuteScalarAsync<int>("SELECT COLUMNPROPERTY(OBJECT_ID('dbo.Phong'),'Id','IsIdentity')");
        int newId;
        if (isIdentity != 1)
        {
            var nextId = await db.ExecuteScalarAsync<int>("SELECT ISNULL(MAX(Id),0)+1 FROM Phong");
            cols.Insert(0, "Id"); vals.Insert(0, "@Id"); p.Add("Id", nextId);
            newId = nextId;
        }
        else
        {
            newId = -1; // will fetch after insert
        }
        var sql = $"INSERT INTO Phong ({string.Join(",", cols)}) VALUES ({string.Join(",", vals)})";
        await db.ExecuteAsync(sql, p);
        var id = isIdentity == 1 ? await db.ExecuteScalarAsync<int>("SELECT TOP 1 Id FROM Phong ORDER BY Id DESC") : newId;

        // If body contains gia, insert into GiaPhong as latest price
        try
        {
            // Support both 'gia' and 'giaPhong' keys
            decimal? giaInsert = null;
            try { if (body?.gia != null) giaInsert = (decimal)body.gia; } catch { }
            try 
            { 
                if (giaInsert == null)
                {
                    var gpObj = (object?)body?.giaPhong;
                    if (gpObj != null) giaInsert = Convert.ToDecimal(gpObj);
                }
            } 
            catch { }
            if (giaInsert.HasValue)
            {
                var gia = giaInsert.Value;
                await db.ExecuteAsync("INSERT INTO GiaPhong (IdPhong, GiaPhong, NgayApDung) VALUES (@id, @gia, GETDATE())", new { id, gia });
            }
        }
        catch
        {
            // ignore if GiaPhong table not exists
        }

    return await GetByIdAsync(id) ?? new { Id = id };
    }

    public async Task<dynamic> UpdateAsync(int id, dynamic body)
    {
        using var db = _factory.Create();
        var columns = await db.QueryAsync<string>("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Phong'");
        var names = new HashSet<string>(columns);
        var sets = new List<string>();
        var p = new DynamicParameters();
        p.Add("Id", id);

        void add(string col, object? val)
        {
            if (!names.Contains(col) || col.Equals("Id", StringComparison.OrdinalIgnoreCase)) return;
            sets.Add($"{col}=@{col}"); 
            p.Add(col, val);
        }

        // Parse JsonElement body
        if (body is System.Text.Json.JsonElement elem)
        {
            if (elem.TryGetProperty("tenPhong", out var tp)) add("TenPhong", tp.GetString());
            if (elem.TryGetProperty("moTa", out var mt)) add("MoTa", mt.GetString());
            if (elem.TryGetProperty("soNguoiToiDa", out var sntd) && sntd.TryGetInt32(out var n1)) add("SoNguoiToiDa", n1);
            if (elem.TryGetProperty("idLoaiPhong", out var idlp) && idlp.TryGetInt32(out var n3)) add("IdLoaiPhong", n3);
            
            // Update price in GiaPhong table if provided (accept both 'gia' and 'giaPhong')
            decimal? giaMoi = null;
            if (elem.TryGetProperty("gia", out var g) && g.TryGetDecimal(out var giaA)) giaMoi = giaA;
            else if (elem.TryGetProperty("giaPhong", out var gp) && gp.TryGetDecimal(out var giaB)) giaMoi = giaB;
            if (giaMoi.HasValue)
            {
                try
                {
                    await db.ExecuteAsync("INSERT INTO GiaPhong (IdPhong, GiaPhong, NgayApDung) VALUES (@id, @gia, GETDATE())", new { id, gia = giaMoi.Value });
                }
                catch { /* ignore if GiaPhong table not exists */ }
            }
        }

        if (sets.Any())
        {
            if (names.Contains("UpdatedAt")) { sets.Add("UpdatedAt=@UpdatedAt"); p.Add("UpdatedAt", DateTime.UtcNow); }
            else if (names.Contains("NgayCapNhat")) { sets.Add("NgayCapNhat=@NgayCapNhat"); p.Add("NgayCapNhat", DateTime.UtcNow); }
            
            var sql = $"UPDATE Phong SET {string.Join(",", sets)} WHERE Id=@Id";
            await db.ExecuteAsync(sql, p);
        }

    return await GetByIdAsync(id) ?? new { Id = id };
    }

    public async Task UpdateImageAsync(int id, string imagePath)
    {
        using var db = _factory.Create();
        var hasAnh = await HasColumnAsync(db, "Anh");
        if (!hasAnh) return;
        var sql = "UPDATE Phong SET Anh=@anh WHERE Id=@id";
        await db.ExecuteAsync(sql, new { id, anh = imagePath });
    }

    public async Task<bool> HasActiveBookingsAsync(int roomId)
    {
        using var db = _factory.Create();
        try
        {
            // Check if DatPhong table exists and has active bookings for this room
            var sql = @"
                SELECT COUNT(1) 
                FROM DatPhong dp 
                WHERE dp.IdPhong = @roomId 
                AND dp.TrangThai NOT IN ('DaHuy', 'DaThanhToan', 'HoanThanh')
                AND dp.NgayTraPhong >= GETDATE()";
            var count = await db.ExecuteScalarAsync<int>(sql, new { roomId });
            return count > 0;
        }
        catch
        {
            // If DatPhong table doesn't exist, assume no active bookings
            return false;
        }
    }

    public async Task DeleteAsync(int id)
    {
        using var db = _factory.Create();
        
        // Delete related records first (if tables exist)
        try
        {
            await db.ExecuteAsync("DELETE FROM GiaPhong WHERE IdPhong=@id", new { id });
        }
        catch { /* ignore if table doesn't exist */ }
        
        try
        {
            // Don't delete active bookings, just the room record
            await db.ExecuteAsync("DELETE FROM Phong WHERE Id=@id", new { id });
        }
        catch (Exception ex)
        {
            throw new Exception($"Không thể xóa phòng: {ex.Message}");
        }
    }

    public async Task ClearImageAsync(int id)
    {
        using var db = _factory.Create();
        var hasAnh = await HasColumnAsync(db, "Anh");
        if (!hasAnh) return;
        var sql = "UPDATE Phong SET Anh=NULL WHERE Id=@id";
        await db.ExecuteAsync(sql, new { id });
    }

    private static async Task<bool> HasColumnAsync(System.Data.IDbConnection db, string column)
    {
        var sql = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Phong' AND COLUMN_NAME=@c";
        var cnt = await db.ExecuteScalarAsync<int>(sql, new { c = column });
        return cnt > 0;
    }
}

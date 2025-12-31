using Dapper;

namespace HotelBookingApi.Data;

public class NguoiDungRepository
{
    private readonly SqlConnectionFactory _factory;
    public NguoiDungRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<(IEnumerable<dynamic> Items, int Total)> ListAsync(int page, int pageSize, string? q)
    {
        using var db = _factory.Create();
        var offset = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
        var size = Math.Max(1, pageSize);
        var kw = (q ?? string.Empty).Trim();
        var where = string.IsNullOrEmpty(kw) ? string.Empty : " WHERE HoTen LIKE @kw OR Email LIKE @kw ";
        var listSql = $"SELECT Id, HoTen, Email, SoDienThoai, VaiTro, TrangThaiTaiKhoan, AnhDaiDien FROM NguoiDung{where} ORDER BY Id DESC OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";
        var countSql = $"SELECT COUNT(*) FROM NguoiDung{where}";
        var items = await db.QueryAsync(listSql, new { kw = $"%{kw}%", offset, size });
        var total = await db.ExecuteScalarAsync<int>(countSql, new { kw = $"%{kw}%" });
        return (items, total);
    }

    public async Task UpdateVaiTroAsync(int id, string vaiTro)
    {
        using var db = _factory.Create();
        await db.ExecuteAsync("UPDATE NguoiDung SET VaiTro=@vt WHERE Id=@id", new { id, vt = vaiTro });
    }

    public async Task<dynamic?> GetByIdAsync(int id)
    {
        using var db = _factory.Create();
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM NguoiDung WHERE Id=@id", new { id });
        return rows.FirstOrDefault();
    }

    public async Task UpdateProfileAsync(int id, string? hoTen, string? soDienThoai, string? anhDaiDien = null)
    {
        using var db = _factory.Create();
        var updates = new List<string>();
        var param = new DynamicParameters();
        param.Add("id", id);

        if (hoTen != null && await HasColumn(db, "HoTen"))
        {
            updates.Add("HoTen=@hoTen");
            param.Add("hoTen", hoTen);
        }

        if (soDienThoai != null && await HasColumn(db, "SoDienThoai"))
        {
            updates.Add("SoDienThoai=@soDienThoai");
            param.Add("soDienThoai", soDienThoai);
        }

        if (anhDaiDien != null && await HasColumn(db, "AnhDaiDien"))
        {
            updates.Add("AnhDaiDien=@anhDaiDien");
            param.Add("anhDaiDien", anhDaiDien);
        }

        if (updates.Any())
        {
            var sql = $"UPDATE NguoiDung SET {string.Join(", ", updates)} WHERE Id=@id";
            await db.ExecuteAsync(sql, param);
        }
    }

    public async Task<dynamic?> FindByEmailAsync(string email)
    {
        using var db = _factory.Create();
        var rows = await db.QueryAsync(new CommandDefinition("SELECT TOP 1 * FROM NguoiDung WHERE Email=@email", new { email }, commandTimeout: 60));
        return rows.FirstOrDefault();
    }

    public async Task UpdateEmailAsync(int id, string email)
    {
        using var db = _factory.Create();
        // Only update when Email column exists
        var hasEmail = await HasColumn(db, "Email");
        if (!hasEmail) return;
        await db.ExecuteAsync(new CommandDefinition("UPDATE NguoiDung SET Email=@email WHERE Id=@id", new { id, email }, commandTimeout: 60));
    }

    public async Task<List<string>> GetRolesAsync(dynamic user)
    {
        using var db = _factory.Create();
        var idVal = Convert.ToInt32(user.Id);
        var rows = await db.QueryAsync<string>(new CommandDefinition("SELECT TOP 1 VaiTro FROM NguoiDung WHERE Id=@Id", new { Id = idVal }, commandTimeout: 60));
        var roleStr = rows.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(roleStr)) return new List<string>();
        // Support single or comma-separated roles in DB (e.g., "Admin" or "Admin,ChuCoSo")
        return roleStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<List<string>> GetPermissionsAsync(IEnumerable<string> roles)
    {
        var map = new Dictionary<string, string[]>
        {
            ["Admin"] = new [] { "USER_READ", "USER_WRITE", "BOOKING_READ", "BOOKING_WRITE", "PAYMENT_READ", "PAYMENT_WRITE" },
            ["ChuCoSo"] = new [] { "ROOM_READ", "ROOM_WRITE", "BOOKING_READ", "BOOKING_WRITE", "PAYMENT_READ" },
            ["KhachHang"] = new [] { "ROOM_READ", "BOOKING_READ", "BOOKING_WRITE", "PAYMENT_READ" },
        };
        var perms = roles.SelectMany(r => map.TryGetValue(r, out var p) ? p : Array.Empty<string>()).Distinct().ToList();
        return Task.FromResult(perms);
    }

    private static async Task<bool> HasColumn(System.Data.IDbConnection db, string column)
    {
        var sql = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='NguoiDung' AND COLUMN_NAME=@c";
        var cnt = await db.ExecuteScalarAsync<int>(sql, new { c = column });
        return cnt > 0;
    }

    // ===== CÁC METHOD CHO DATABASE AUTHENTICATION =====

    // Lấy user theo Email
    public async Task<dynamic?> GetByEmailAsync(string email)
    {
        using var db = _factory.Create();
        var sql = "SELECT TOP 1 * FROM NguoiDung WHERE Email=@Email";
        var result = await db.QueryAsync(new CommandDefinition(sql, new { Email = email }, commandTimeout: 60));
        return result.FirstOrDefault();
    }

    // Tạo user mới với email/password
    public async Task<int> CreateAsync(string email, string hashedPassword, string? hoTen, string? soDienThoai, string vaiTro, string trangThai)
    {
        using var db = _factory.Create();
        
        var columns = new List<string> { "Email", "MatKhau", "VaiTro", "TrangThai" };
        var values = new List<string> { "@Email", "@MatKhau", "@VaiTro", "@TrangThai" };
        var param = new DynamicParameters();
        param.Add("Email", email);
        param.Add("MatKhau", hashedPassword);
        param.Add("VaiTro", vaiTro);
        param.Add("TrangThai", trangThai);

        if (!string.IsNullOrEmpty(hoTen))
        {
            columns.Add("HoTen");
            values.Add("@HoTen");
            param.Add("HoTen", hoTen);
        }

        if (!string.IsNullOrEmpty(soDienThoai))
        {
            columns.Add("SoDienThoai");
            values.Add("@SoDienThoai");
            param.Add("SoDienThoai", soDienThoai);
        }

        // Thêm NgayTao nếu có
        if (await HasColumn(db, "NgayTao"))
        {
            columns.Add("NgayTao");
            values.Add("GETDATE()");
        }

        var sql = $"INSERT INTO NguoiDung ({string.Join(",", columns)}) OUTPUT INSERTED.Id VALUES ({string.Join(",", values)})";
        
        var newId = await db.ExecuteScalarAsync<int>(new CommandDefinition(sql, param, commandTimeout: 60));
        return newId;
    }

    // Cập nhật mật khẩu
    public async Task UpdatePasswordAsync(int userId, string hashedPassword)
    {
        using var db = _factory.Create();
        
        var sql = "UPDATE NguoiDung SET MatKhau=@MatKhau";
        
        // Thêm NgayCapNhat nếu có
        if (await HasColumn(db, "NgayCapNhat"))
        {
            sql += ", NgayCapNhat=GETDATE()";
        }
        
        sql += " WHERE Id=@Id";
        
        await db.ExecuteAsync(new CommandDefinition(
            sql, 
            new { Id = userId, MatKhau = hashedPassword }, 
            commandTimeout: 60));
    }

    // Cập nhật trạng thái tài khoản
    public async Task UpdateStatusAsync(int userId, string status)
    {
        using var db = _factory.Create();
        
        var sql = "UPDATE NguoiDung SET TrangThai=@TrangThai";
        
        if (await HasColumn(db, "NgayCapNhat"))
        {
            sql += ", NgayCapNhat=GETDATE()";
        }
        
        sql += " WHERE Id=@Id";
        
        await db.ExecuteAsync(new CommandDefinition(
            sql, 
            new { Id = userId, TrangThai = status }, 
            commandTimeout: 60));
    }

    // Cập nhật thông tin user
    public async Task UpdateProfileAsync(int userId, string? hoTen, string? soDienThoai)
    {
        using var db = _factory.Create();
        
        var updates = new List<string>();
        var param = new DynamicParameters();
        param.Add("Id", userId);

        if (!string.IsNullOrEmpty(hoTen))
        {
            updates.Add("HoTen=@HoTen");
            param.Add("HoTen", hoTen);
        }

        if (!string.IsNullOrEmpty(soDienThoai))
        {
            updates.Add("SoDienThoai=@SoDienThoai");
            param.Add("SoDienThoai", soDienThoai);
        }

        if (!updates.Any()) return;

        if (await HasColumn(db, "NgayCapNhat"))
        {
            updates.Add("NgayCapNhat=GETDATE()");
        }

        var sql = $"UPDATE NguoiDung SET {string.Join(", ", updates)} WHERE Id=@Id";
        
        await db.ExecuteAsync(new CommandDefinition(sql, param, commandTimeout: 60));
    }

    /// <summary>
    /// Update user avatar specifically
    /// </summary>
    public async Task UpdateAvatarAsync(int userId, string avatarPath)
    {
        using var db = _factory.Create();
        
        // Check if AnhDaiDien column exists
        if (await HasColumn(db, "AnhDaiDien"))
        {
            var updates = new List<string> { "AnhDaiDien=@avatarPath" };
            
            // Add update timestamp if column exists
            if (await HasColumn(db, "NgayCapNhat"))
            {
                updates.Add("NgayCapNhat=GETDATE()");
            }
            
            var sql = $"UPDATE NguoiDung SET {string.Join(", ", updates)} WHERE Id=@userId";
            await db.ExecuteAsync(sql, new { userId, avatarPath });
        }
        else
        {
            throw new InvalidOperationException("Cột AnhDaiDien chưa được tạo trong database");
        }
    }
}

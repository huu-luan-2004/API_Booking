using Dapper;

namespace HotelBookingApi.Data;

public class NguoiDungRepository
{
    private readonly SqlConnectionFactory _factory;
    public NguoiDungRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<dynamic?> FindByFirebaseUidAsync(string firebaseUid)
    {
        using var db = _factory.Create();
        var col = await db.ExecuteScalarAsync<int>("SELECT CASE WHEN EXISTS(SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='NguoiDung' AND COLUMN_NAME='FirebaseUID') THEN 1 ELSE 0 END");
        var column = col == 1 ? "FirebaseUID" : "FirebaseUid";
        var sql = $"SELECT TOP 1 * FROM NguoiDung WHERE {column}=@uid";
        var rows = await db.QueryAsync(sql, new { uid = firebaseUid });
        return rows.FirstOrDefault();
    }

    public async Task<(IEnumerable<dynamic> Items, int Total)> ListAsync(int page, int pageSize, string? q)
    {
        using var db = _factory.Create();
        var offset = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
        var size = Math.Max(1, pageSize);
        var kw = (q ?? string.Empty).Trim();
        var where = string.IsNullOrEmpty(kw) ? string.Empty : " WHERE HoTen LIKE @kw OR Email LIKE @kw ";
        var listSql = $"SELECT Id, HoTen, Email, SoDienThoai, TrangThaiTaiKhoan FROM NguoiDung{where} ORDER BY Id DESC OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";
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
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM NguoiDung WHERE Email=@email", new { email });
        return rows.FirstOrDefault();
    }

    public async Task UpdateEmailAsync(int id, string email)
    {
        using var db = _factory.Create();
        // Only update when Email column exists
        var hasEmail = await HasColumn(db, "Email");
        if (!hasEmail) return;
        await db.ExecuteAsync("UPDATE NguoiDung SET Email=@email WHERE Id=@id", new { id, email });
    }

    public async Task<int> CreateUserAsync(string? email, string? hoTen, string? soDienThoai, string firebaseUid, string vaiTro)
    {
        using var db = _factory.Create();
        // Detect identity on Id
        var isIdentity = await db.ExecuteScalarAsync<int>("SELECT COLUMNPROPERTY(OBJECT_ID('dbo.NguoiDung'),'Id','IsIdentity')");
        int? newId = null;
        if (isIdentity != 1)
        {
            newId = await db.ExecuteScalarAsync<int>("SELECT ISNULL(MAX(Id),0)+1 FROM NguoiDung");
        }

        // Detect type of TrangThaiTaiKhoan
        var type = await db.ExecuteScalarAsync<string>("SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='NguoiDung' AND COLUMN_NAME='TrangThaiTaiKhoan'");
        var useBit = string.Equals(type, "bit", StringComparison.OrdinalIgnoreCase);
        var useNumeric = new[] { "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "float", "real" }.Contains((type ?? string.Empty).ToLower());
        var trangThai = useBit ? (object)true : useNumeric ? 1 : "Bình thường";

        var columns = new List<string>();
        var values = new List<string>();
        var param = new DynamicParameters();

        if (newId.HasValue) { columns.Add("Id"); values.Add("@Id"); param.Add("Id", newId.Value); }
        if (await HasColumn(db, "HoTen")) { columns.Add("HoTen"); values.Add("@HoTen"); param.Add("HoTen", hoTen); }
        if (await HasColumn(db, "Email")) { columns.Add("Email"); values.Add("@Email"); param.Add("Email", email); }
        if (await HasColumn(db, "SoDienThoai")) { columns.Add("SoDienThoai"); values.Add("@SoDienThoai"); param.Add("SoDienThoai", soDienThoai); }
        if (await HasColumn(db, "TrangThaiTaiKhoan")) { columns.Add("TrangThaiTaiKhoan"); values.Add("@TrangThai"); param.Add("TrangThai", trangThai); }
        if (await HasColumn(db, "CreatedAt")) { columns.Add("CreatedAt"); values.Add("@CreatedAt"); param.Add("CreatedAt", DateTime.UtcNow); }
        if (await HasColumn(db, "FirebaseUID")) { columns.Add("FirebaseUID"); values.Add("@Fuid"); param.Add("Fuid", firebaseUid); }
        else if (await HasColumn(db, "FirebaseUid")) { columns.Add("FirebaseUid"); values.Add("@Fuid"); param.Add("Fuid", firebaseUid); }
        if (await HasColumn(db, "VaiTro")) { columns.Add("VaiTro"); values.Add("@VaiTro"); param.Add("VaiTro", vaiTro); }

        if (!columns.Any()) throw new Exception("NguoiDung table has no expected columns");
        var sql = $"INSERT INTO NguoiDung ({string.Join(",", columns)}) VALUES ({string.Join(",", values)})";
        await db.ExecuteAsync(sql, param);

        // return new id if present otherwise fetch
        if (newId.HasValue) return newId.Value;
        var id = await db.ExecuteScalarAsync<int>("SELECT TOP 1 Id FROM NguoiDung WHERE (Email=@Email OR @Email IS NULL) AND (@Fuid IS NULL OR FirebaseUID=@Fuid OR FirebaseUid=@Fuid) ORDER BY Id DESC", new { Email = email, Fuid = firebaseUid });
        return id;
    }

    public async Task<List<string>> GetRolesAsync(dynamic user)
    {
        using var db = _factory.Create();
        var rows = await db.QueryAsync<string>("SELECT TOP 1 VaiTro FROM NguoiDung WHERE Id=@Id", new { Id = (int)user.Id });
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
}

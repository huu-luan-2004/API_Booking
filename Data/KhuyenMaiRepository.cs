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

    public async Task<dynamic?> GetByIdAsync(int id)
    {
        using var db = _factory.Create();
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM KhuyenMai WHERE Id=@id", new { id });
        return rows.FirstOrDefault();
    }

    public async Task<IEnumerable<int>> GetRoomIdsAsync(int idKhuyenMai)
    {
        using var db = _factory.Create();
        try
        {
            var rows = await db.QueryAsync<int>("SELECT IdPhong FROM KhuyenMaiDatPhong WHERE IdKhuyenMai=@id", new { id = idKhuyenMai });
            return rows.ToArray();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    public async Task<dynamic> CreateAsync(System.Text.Json.JsonElement body, IEnumerable<int>? roomIds)
    {
        using var db = _factory.Create();
        var columnNames = (await db.QueryAsync<string>("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='KhuyenMai'"))
            .ToList();
        var names = new HashSet<string>(columnNames, StringComparer.OrdinalIgnoreCase);

        var cols = new List<string>();
        var vals = new List<string>();
        var p = new DynamicParameters();

        object? ReadValue(string col)
        {
            // Accept both PasCalCase and camelCase
            var lc = col.ToLowerInvariant();
            foreach (var prop in body.EnumerateObject())
            {
                var n = prop.Name;
                if (string.Equals(n, col, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, ToCamel(col), StringComparison.OrdinalIgnoreCase))
                {
                    return ConvertJson(prop.Value);
                }
            }
            return null;
        }

        void add(string col)
        {
            if (!names.Contains(col)) return;
            var val = ReadValue(col);
            if (val == null) return;
            cols.Add(col); vals.Add("@" + col); p.Add(col, val);
        }

        // Add all columns that are present in payload except identity Id
        foreach (var col in columnNames.Where(c => !c.Equals("Id", StringComparison.OrdinalIgnoreCase)))
        {
            add(col);
        }

        // common timestamp columns
        if (names.Contains("CreatedAt") && !p.ParameterNames.Contains("CreatedAt"))
        {
            cols.Add("CreatedAt"); vals.Add("@CreatedAt"); p.Add("CreatedAt", DateTime.UtcNow);
        }

        if (!cols.Any()) throw new Exception("Không có dữ liệu hợp lệ để tạo khuyến mãi");

        // handle identity
        var isIdentity = await db.ExecuteScalarAsync<int>("SELECT COLUMNPROPERTY(OBJECT_ID('dbo.KhuyenMai'),'Id','IsIdentity')");
        int newId = -1;
        if (isIdentity != 1)
        {
            newId = await db.ExecuteScalarAsync<int>("SELECT ISNULL(MAX(Id),0)+1 FROM KhuyenMai");
            cols.Insert(0, "Id"); vals.Insert(0, "@Id"); p.Add("Id", newId);
        }

        var sql = $"INSERT INTO KhuyenMai ({string.Join(",", cols)}) VALUES ({string.Join(",", vals)})";
        await db.ExecuteAsync(sql, p);
        var id = isIdentity == 1 ? await db.ExecuteScalarAsync<int>("SELECT TOP 1 Id FROM KhuyenMai ORDER BY Id DESC") : newId;

        if (roomIds != null)
        {
            await UpsertRoomLinksAsync(db, id, roomIds);
        }

        return await GetByIdAsync(id) ?? new { Id = id };
    }

    public async Task<dynamic> UpdateAsync(int id, System.Text.Json.JsonElement body, IEnumerable<int>? roomIds)
    {
        using var db = _factory.Create();
        var columnNames = (await db.QueryAsync<string>("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='KhuyenMai'"))
            .ToList();
        var names = new HashSet<string>(columnNames, StringComparer.OrdinalIgnoreCase);
        var sets = new List<string>();
        var p = new DynamicParameters();
        p.Add("Id", id);

        object? ReadValue(string col)
        {
            foreach (var prop in body.EnumerateObject())
            {
                if (string.Equals(prop.Name, col, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prop.Name, ToCamel(col), StringComparison.OrdinalIgnoreCase))
                {
                    return ConvertJson(prop.Value);
                }
            }
            return null;
        }

        foreach (var col in columnNames)
        {
            if (col.Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;
            var val = ReadValue(col);
            if (val == null) continue;
            if (!names.Contains(col)) continue;
            sets.Add($"{col}=@{col}"); p.Add(col, val);
        }

        if (names.Contains("UpdatedAt") && !p.ParameterNames.Contains("UpdatedAt"))
        {
            sets.Add("UpdatedAt=@UpdatedAt"); p.Add("UpdatedAt", DateTime.UtcNow);
        }

        if (sets.Any())
        {
            var sql = $"UPDATE KhuyenMai SET {string.Join(",", sets)} WHERE Id=@Id";
            await db.ExecuteAsync(sql, p);
        }

        if (roomIds != null)
        {
            await UpsertRoomLinksAsync(db, id, roomIds);
        }

        return await GetByIdAsync(id) ?? new { Id = id };
    }

    public async Task DeleteAsync(int id)
    {
        using var db = _factory.Create();
        try { await db.ExecuteAsync("DELETE FROM KhuyenMaiDatPhong WHERE IdKhuyenMai=@id", new { id }); } catch { }
        await db.ExecuteAsync("DELETE FROM KhuyenMai WHERE Id=@id", new { id });
    }

    private static async Task UpsertRoomLinksAsync(System.Data.IDbConnection db, int idKhuyenMai, IEnumerable<int> roomIds)
    {
        try
        {
            await db.ExecuteAsync("DELETE FROM KhuyenMaiDatPhong WHERE IdKhuyenMai=@id", new { id = idKhuyenMai });
            var distinctIds = roomIds.Where(i => i > 0).Distinct().ToArray();
            foreach (var rid in distinctIds)
            {
                await db.ExecuteAsync("INSERT INTO KhuyenMaiDatPhong (IdKhuyenMai, IdPhong) VALUES (@km, @p)", new { km = idKhuyenMai, p = rid });
            }
        }
        catch
        {
            // ignore if link table does not exist
        }
    }

    private static object? ConvertJson(System.Text.Json.JsonElement value)
    {
        switch (value.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                var s = value.GetString();
                if (DateTime.TryParse(s, out var dt)) return dt;
                return s;
            case System.Text.Json.JsonValueKind.Number:
                if (value.TryGetInt64(out var i64)) return i64;
                if (value.TryGetDecimal(out var dec)) return dec;
                return null;
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                return value.GetBoolean();
            case System.Text.Json.JsonValueKind.Null:
            case System.Text.Json.JsonValueKind.Undefined:
                return null;
            default:
                return value.ToString();
        }
    }

    private static string ToCamel(string pascal)
    {
        if (string.IsNullOrEmpty(pascal) || char.IsLower(pascal[0])) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
    }
}

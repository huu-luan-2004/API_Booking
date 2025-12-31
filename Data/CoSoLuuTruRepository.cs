using Dapper;

namespace HotelBookingApi.Data;

public class CoSoLuuTruRepository
{
    private readonly SqlConnectionFactory _factory;
    public CoSoLuuTruRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<IEnumerable<dynamic>> ListAsync(int page, int pageSize, string? q, bool includeUnapproved, int? ownerId)
    {
        using var db = _factory.Create();
        var offset = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
        var size = Math.Max(1, pageSize);
        var kw = (q ?? string.Empty).Trim();
        var where = new List<string>();
        var param = new DynamicParameters();
        if (!string.IsNullOrEmpty(kw)) { where.Add("(TenCoSo LIKE @kw)"); param.Add("kw", $"%{kw}%"); }
        if (!includeUnapproved && await HasColumnAsync(db, "TrangThaiDuyet")) { where.Add("(TrangThaiDuyet='DaDuyet')"); }
        if (ownerId.HasValue && await HasColumnAsync(db, "IdNguoiDung")) { where.Add("(IdNguoiDung=@ownerId)"); param.Add("ownerId", ownerId.Value); }
        var whereSql = where.Any() ? (" WHERE " + string.Join(" AND ", where)) : string.Empty;
        var sql = $"SELECT * FROM CoSoLuuTru{whereSql} ORDER BY Id DESC OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";
        param.Add("offset", offset); param.Add("size", size);
        return await db.QueryAsync(sql, param);
    }

    public async Task<dynamic?> GetByIdAsync(int id)
    {
        using var db = _factory.Create();
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM CoSoLuuTru WHERE Id=@id", new { id });
        return rows.FirstOrDefault();
    }

    public async Task EnsurePendingApprovalAsync(int id)
    {
        using var db = _factory.Create();
        if (await HasColumnAsync(db, "TrangThaiDuyet"))
        {
            await db.ExecuteAsync("UPDATE CoSoLuuTru SET TrangThaiDuyet='ChoDuyet' WHERE Id=@id AND (TrangThaiDuyet IS NULL OR TrangThaiDuyet='')", new { id });
        }
    }

    public async Task<dynamic> CreateAsync(dynamic body)
    {
        using var db = _factory.Create();
        var columns = await db.QueryAsync<string>("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='CoSoLuuTru'");
        var names = new HashSet<string>(columns);
        var cols = new List<string>(); var vals = new List<string>(); var p = new DynamicParameters();
        void add(string col, object? val) { if (!names.Contains(col)) return; cols.Add(col); vals.Add("@"+col); p.Add(col, val); }
        add("TenCoSo", (string?)body?.tenCoSo);
        add("MoTa", (string?)body?.moTa);
        add("SoTaiKhoan", (string?)body?.soTaiKhoan);
        add("TenTaiKhoan", (string?)body?.tenTaiKhoan);
        add("TenNganHang", (string?)body?.tenNganHang);
        add("IdDiaChi", (int?)body?.idDiaChi);
        add("Anh", (string?)body?.anh);
        add("IdNguoiDung", (int?)body?.idNguoiDung);
        add("TrangThai", 1); // Mặc định hoạt động khi tạo mới
        if (names.Contains("CreatedAt")) add("CreatedAt", DateTime.UtcNow);
        var isIdentity = await db.ExecuteScalarAsync<int>("SELECT COLUMNPROPERTY(OBJECT_ID('dbo.CoSoLuuTru'),'Id','IsIdentity')");
        if (isIdentity != 1) { var nextId = await db.ExecuteScalarAsync<int>("SELECT ISNULL(MAX(Id),0)+1 FROM CoSoLuuTru"); cols.Insert(0,"Id"); vals.Insert(0,"@Id"); p.Add("Id", nextId); }
        var sql = $"INSERT INTO CoSoLuuTru ({string.Join(",", cols)}) VALUES ({string.Join(",", vals)})";
        await db.ExecuteAsync(sql, p);
        var id = isIdentity == 1 ? await db.ExecuteScalarAsync<int>("SELECT TOP 1 Id FROM CoSoLuuTru ORDER BY Id DESC") : (int)p.Get<int>("Id");
        return await GetByIdAsync(id);
    }

    public async Task SetApprovalStatusAsync(int id, string status, string? reason=null)
    {
        using var db = _factory.Create();
        if (await HasColumnAsync(db, "TrangThaiDuyet"))
        {
            await db.ExecuteAsync("UPDATE CoSoLuuTru SET TrangThaiDuyet=@st WHERE Id=@id", new { id, st=status });
        }
        if (!string.IsNullOrWhiteSpace(reason) && await HasColumnAsync(db, "LyDoTuChoi"))
        {
            await db.ExecuteAsync("UPDATE CoSoLuuTru SET LyDoTuChoi=@rs WHERE Id=@id", new { id, rs=reason });
        }
    }

    public async Task UpdateAsync(int id, dynamic body)
    {
        using var db = _factory.Create();
        var columns = await db.QueryAsync<string>("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='CoSoLuuTru'");
        var names = new HashSet<string>(columns);
        var cols = new List<string>();
        var p = new DynamicParameters();
        p.Add("id", id);

        void add(string col, object? val) 
        { 
            if (names.Contains(col) && val != null) 
            { 
                cols.Add($"{col}=@{col}"); 
                p.Add(col, val); 
            } 
        }

        add("TenCoSo", (string?)body?.tenCoSo);
        add("MoTa", (string?)body?.moTa);
        add("SoTaiKhoan", (string?)body?.soTaiKhoan);
        add("TenTaiKhoan", (string?)body?.tenTaiKhoan);
        add("TenNganHang", (string?)body?.tenNganHang);
        add("IdDiaChi", (int?)body?.idDiaChi);
        add("Anh", (string?)body?.anh);
        add("TrangThai", (int?)body?.trangThai);
        if (names.Contains("UpdatedAt")) add("UpdatedAt", DateTime.UtcNow);

        if (cols.Any())
        {
            var sql = $"UPDATE CoSoLuuTru SET {string.Join(", ", cols)} WHERE Id=@id";
            await db.ExecuteAsync(sql, p);
        }
    }

    public async Task DeleteAsync(int id)
    {
        using var db = _factory.Create();
        await db.ExecuteAsync("DELETE FROM CoSoLuuTru WHERE Id=@id", new { id });
    }

    public async Task<int> CreateAddressAsync(dynamic addressData)
    {
        using var db = _factory.Create();
        
        // Kiểm tra bảng DiaChiChiTiet có tồn tại không
        var tableExists = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DiaChiChiTiet'"
        );
        
        if (tableExists == 0)
        {
            throw new InvalidOperationException("Bảng DiaChiChiTiet không tồn tại trong database");
        }

        // Chuẩn hoá dữ liệu đầu vào cho schema mới (ChiTiet, Pho, Phuong, Nuoc, KinhDo, ViDo)
        // Đọc an toàn từ dynamic/anonymous type/IDictionary để tránh RuntimeBinderException
        string? chiTiet = GetValue<string>(addressData, "chiTiet") ?? GetValue<string>(addressData, "soNha");
        string? pho     = GetValue<string>(addressData, "pho")
                  ?? GetValue<string>(addressData, "road")
                  ?? GetValue<string>(addressData, "soNha");
        string? phuong  = GetValue<string>(addressData, "phuong");
        string? nuoc    = GetValue<string>(addressData, "nuoc") ?? GetValue<string>(addressData, "country");

        // Mỗi cơ sở lưu trú có 1 địa chỉ độc lập: luôn tạo bản ghi mới, không tái sử dụng IdDiaChi

        // Tạo địa chỉ mới theo schema mới
        var sql = @"
            INSERT INTO DiaChiChiTiet (ChiTiet, Pho, Phuong, Nuoc, KinhDo, ViDo)
            VALUES (@chiTiet, @pho, @phuong, @nuoc, @kinhDo, @viDo);
            SELECT SCOPE_IDENTITY();
        ";

        // Sanitize ranges and precision to tránh overflow numeric
        double? kd = GetValue<double?>(addressData, "kinhDo");
        double? vd = GetValue<double?>(addressData, "viDo");
        if (kd.HasValue) kd = Math.Max(-180, Math.Min(180, kd.Value));
        if (vd.HasValue) vd = Math.Max(-90, Math.Min(90, vd.Value));
        decimal? kdDec = kd.HasValue ? Math.Round((decimal)kd.Value, 6) : (decimal?)null;
        decimal? vdDec = vd.HasValue ? Math.Round((decimal)vd.Value, 6) : (decimal?)null;

        var newId = await db.ExecuteScalarAsync<int>(sql, new { chiTiet, pho, phuong, nuoc, kinhDo = kdDec, viDo = vdDec });

        return newId;
    }

    public async Task UpdateAddressAsync(int id, dynamic addressData)
    {
        using var db = _factory.Create();

        // Xác nhận bảng tồn tại
        var tableExists = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DiaChiChiTiet'"
        );
        if (tableExists == 0)
        {
            throw new InvalidOperationException("Bảng DiaChiChiTiet không tồn tại trong database");
        }

        // Cập nhật chỉ những cột có giá trị (tránh ghi đè bằng NULL)
        var sql = @"
            UPDATE DiaChiChiTiet SET
                ChiTiet = COALESCE(@chiTiet, ChiTiet),
                Pho     = COALESCE(@pho, Pho),
                Phuong  = COALESCE(@phuong, Phuong),
                Nuoc    = COALESCE(@nuoc, Nuoc),
                KinhDo  = COALESCE(@kinhDo, KinhDo),
                ViDo    = COALESCE(@viDo, ViDo)
            WHERE Id=@id
        ";

        // Sanitize ranges and precision
        double? kd = GetValue<double?>(addressData, "kinhDo");
        double? vd = GetValue<double?>(addressData, "viDo");
        if (kd.HasValue) kd = Math.Max(-180, Math.Min(180, kd.Value));
        if (vd.HasValue) vd = Math.Max(-90, Math.Min(90, vd.Value));
        decimal? kdDec = kd.HasValue ? Math.Round((decimal)kd.Value, 6) : (decimal?)null;
        decimal? vdDec = vd.HasValue ? Math.Round((decimal)vd.Value, 6) : (decimal?)null;

        await db.ExecuteAsync(sql, new {
            id,
            chiTiet = GetValue<string>(addressData, "chiTiet") ?? GetValue<string>(addressData, "soNha"),
            pho     = GetValue<string>(addressData, "pho") ?? GetValue<string>(addressData, "road") ?? GetValue<string>(addressData, "soNha"),
            phuong  = GetValue<string>(addressData, "phuong"),
            nuoc    = GetValue<string>(addressData, "nuoc") ?? GetValue<string>(addressData, "country"),
            kinhDo  = kdDec,
            viDo    = vdDec
        });
    }

    private static async Task<bool> HasColumnAsync(System.Data.IDbConnection db, string column)
    {
        var sql = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='CoSoLuuTru' AND COLUMN_NAME=@c";
        var cnt = await db.ExecuteScalarAsync<int>(sql, new { c = column });
        return cnt > 0;
    }

    public async Task<dynamic?> GetAddressByIdAsync(int id)
    {
        using var db = _factory.Create();
        var tableExists = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DiaChiChiTiet'"
        );
        if (tableExists == 0) return null;
        var rows = await db.QueryAsync("SELECT TOP 1 * FROM DiaChiChiTiet WHERE Id=@id", new { id });
        return rows.FirstOrDefault();
    }

    private static T? GetValue<T>(object? data, string name)
    {
        try
        {
            if (data is null) return default;
            if (data is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue(name, out var v)) return (T?)v;
                return default;
            }
            var prop = data.GetType().GetProperty(name);
            if (prop is null) return default;
            var val = prop.GetValue(data);
            return (T?)val;
        }
        catch { return default; }
    }
}

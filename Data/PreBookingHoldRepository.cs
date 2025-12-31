using Dapper;
using Microsoft.Data.SqlClient;

namespace HotelBookingApi.Data;

public class PreBookingHoldRepository
{
    private readonly SqlConnectionFactory _factory;
    public PreBookingHoldRepository(SqlConnectionFactory factory) => _factory = factory;

    public async Task<int> PurgeExpiredAsync(int minutes = 0)
    {
        using var db = _factory.Create();
        // Nếu minutes > 0, cũng xóa các hold quá hạn tương đối (phòng trường hợp ExpiresAt không được cập nhật)
        var sql = @"DELETE FROM PreBookingHold WHERE ExpiresAt <= GETDATE()";
        var affected = await db.ExecuteAsync(sql);
        if (minutes > 0)
        {
            try
            {
                var sql2 = @"DELETE FROM PreBookingHold WHERE CreatedAt < DATEADD(MINUTE, -@m, GETDATE())";
                affected += await db.ExecuteAsync(sql2, new { m = minutes });
            }
            catch { }
        }
        return affected;
    }

    public async Task<(string token, DateTime expiresAt)> AcquireAsync(int idNguoiDung, int idPhong, DateTime ngayNhan, DateTime ngayTra, int ttlMinutes = 15)
    {
        using var conn = (SqlConnection)_factory.Create();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        // 1) Nếu chính người dùng này đã có hold đang hoạt động cho khoảng thời gian chồng lấn -> gia hạn và trả lại token cũ
        var findOwnSql = @"
            SELECT TOP 1 HoldToken
            FROM PreBookingHold
            WHERE IdPhong=@idPhong AND IdNguoiDung=@uid
              AND ExpiresAt > GETDATE()
              AND NOT (@tra <= NgayNhanPhong OR @nhan >= NgayTraPhong)";
        var existingToken = await conn.ExecuteScalarAsync<string?>(findOwnSql, new { idPhong, uid = idNguoiDung, nhan = ngayNhan, tra = ngayTra }, tx);
        if (!string.IsNullOrEmpty(existingToken))
        {
            var updSql = @"UPDATE PreBookingHold
                            SET NgayNhanPhong=@nhan, NgayTraPhong=@tra,
                                ExpiresAt = DATEADD(MINUTE, @ttl, GETDATE())
                            WHERE HoldToken=@tok";
            await conn.ExecuteAsync(updSql, new { nhan = ngayNhan, tra = ngayTra, ttl = ttlMinutes, tok = existingToken }, tx);
            var expiresRenewed = await conn.ExecuteScalarAsync<DateTime>(
                "SELECT ExpiresAt FROM PreBookingHold WHERE HoldToken=@tok",
                new { tok = existingToken }, tx);
            await tx.CommitAsync();
            return (existingToken, expiresRenewed);
        }

        // 2) Nếu có hold của người khác đang hoạt động cho khoảng thời gian chồng lấn -> chặn
        var conflictSql = @"
            SELECT COUNT(1)
            FROM PreBookingHold
            WHERE IdPhong=@idPhong
              AND ExpiresAt > GETDATE()
              AND NOT (@tra <= NgayNhanPhong OR @nhan >= NgayTraPhong)";
        var cnt = await conn.ExecuteScalarAsync<int>(conflictSql, new { idPhong, nhan = ngayNhan, tra = ngayTra }, tx);
        if (cnt > 0) throw new Exception("Phòng đang được giữ bởi khách khác");

        var token = $"HOLD_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
        // Tính ExpiresAt hoàn toàn trên SQL (cùng múi giờ với GETDATE()) để không vi phạm CHECK (ExpiresAt > CreatedAt)
        var sql = @"
            INSERT INTO PreBookingHold (HoldToken, IdNguoiDung, IdPhong, NgayNhanPhong, NgayTraPhong, CreatedAt, ExpiresAt)
            VALUES (@tok, @uid, @pid, @nhan, @tra, GETDATE(), DATEADD(MINUTE, @ttl, GETDATE()))";
        await conn.ExecuteAsync(sql, new { tok = token, uid = idNguoiDung, pid = idPhong, nhan = ngayNhan, tra = ngayTra, ttl = ttlMinutes }, tx);
        // Lấy ExpiresAt thực tế từ DB để trả về cho client
        var expires = await conn.ExecuteScalarAsync<DateTime>(
            "SELECT ExpiresAt FROM PreBookingHold WHERE HoldToken=@t",
            new { t = token }, tx);
        await tx.CommitAsync();
        return (token, expires);
    }

    public async Task<bool> RenewAsync(string holdToken, int ttlMinutes = 15)
    {
        using var db = _factory.Create();
        var sql = @"UPDATE PreBookingHold SET ExpiresAt = DATEADD(MINUTE, @ttl, GETDATE()) WHERE HoldToken=@t AND ExpiresAt > GETDATE()";
        var rows = await db.ExecuteAsync(sql, new { ttl = ttlMinutes, t = holdToken });
        return rows > 0;
    }

    public async Task<bool> ReleaseAsync(string holdToken)
    {
        using var db = _factory.Create();
        var sql = @"DELETE FROM PreBookingHold WHERE HoldToken=@t";
        var rows = await db.ExecuteAsync(sql, new { t = holdToken });
        return rows > 0;
    }

    public async Task<dynamic?> GetByTokenAsync(string holdToken)
    {
        using var db = _factory.Create();
        var sql = @"SELECT TOP 1 * FROM PreBookingHold WHERE HoldToken=@t";
        var rows = await db.QueryAsync(sql, new { t = holdToken });
        return rows.FirstOrDefault();
    }

    public async Task<bool> ExistsActiveOverlapAsync(int idPhong, DateTime nhan, DateTime tra)
    {
        using var db = _factory.Create();
        var sql = @"
            SELECT COUNT(1)
            FROM PreBookingHold
            WHERE IdPhong=@idPhong AND ExpiresAt > GETDATE()
              AND NOT (@tra <= NgayNhanPhong OR @nhan >= NgayTraPhong)";
        var cnt = await db.ExecuteScalarAsync<int>(sql, new { idPhong, nhan, tra });
        return cnt > 0;
    }
}

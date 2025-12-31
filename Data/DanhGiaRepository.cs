using Dapper;
using System.Data;

namespace HotelBookingApi.Data
{
    public class DanhGiaRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public DanhGiaRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // Thêm đánh giá mới (chèn linh hoạt theo cột hiện có)
        public async Task<int> CreateAsync(
            int idNguoiDung, 
            int idPhong, 
            int diem, 
            string binhLuan, 
            int? datPhongId = null,
            string? media = null,
            bool anDanh = false)
        {
            using var connection = _connectionFactory.Create();

            // Lấy danh sách cột hiện có của bảng DanhGia
            var columns = await connection.QueryAsync<string>(
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='DanhGia'");
            var names = new HashSet<string>(columns);

            var cols = new List<string>();
            var vals = new List<string>();
            var p = new DynamicParameters();

            void add(string col, object? val)
            {
                if (!names.Contains(col)) return;
                cols.Add(col); vals.Add("@" + col); p.Add(col, val);
            }

            // Các cột tối thiểu
            add("IdNguoiDung", idNguoiDung);
            add("IdPhong", idPhong);
            add("Diem", diem);
            add("BinhLuan", binhLuan);
            add("CreatedAt", DateTime.Now);

            // Các cột tuỳ chọn nếu tồn tại
            add("Media", media);
            add("DatPhongId", datPhongId);
            add("AnDanh", anDanh);

            if (!cols.Any())
                throw new InvalidOperationException("Bảng DanhGia không có cột hợp lệ để chèn");

            var sql = $"INSERT INTO DanhGia ({string.Join(",", cols)}) VALUES ({string.Join(",", vals)}); SELECT CAST(SCOPE_IDENTITY() as int);";
            var id = await connection.QuerySingleAsync<int>(sql, p);
            return id;
        }

        // Lấy đánh giá theo phòng
        public async Task<IEnumerable<dynamic>> GetByRoomIdAsync(int idPhong, int page = 1, int limit = 10)
        {
            using var connection = _connectionFactory.Create();
            
            var offset = (page - 1) * limit;
            
            // Kiểm tra cột AnDanh/Media/DatPhongId tồn tại để build SELECT an toàn
            var hasAnDanh = await HasColumnAsync(connection, "AnDanh");
            var hasMedia = await HasColumnAsync(connection, "Media");
            var hasDatPhongId = await HasColumnAsync(connection, "DatPhongId");

            var selectCols = @"d.Id, d.IdNguoiDung, d.IdPhong, d.Diem, d.BinhLuan, d.CreatedAt";
            if (hasMedia) selectCols += ", d.Media"; else selectCols += ", NULL AS Media";
            if (hasDatPhongId) selectCols += ", d.DatPhongId"; else selectCols += ", NULL AS DatPhongId";
            if (hasAnDanh)
                selectCols += ", CASE WHEN d.AnDanh = 1 THEN 'Ẩn danh' ELSE nd.HoTen END AS TenNguoiDung, CASE WHEN d.AnDanh = 1 THEN NULL ELSE nd.Avatar END AS AvatarNguoiDung, d.AnDanh";
            else
                selectCols += ", nd.HoTen AS TenNguoiDung, nd.Avatar AS AvatarNguoiDung, NULL AS AnDanh";

            var sql = $@"
                SELECT {selectCols}
                FROM DanhGia d
                LEFT JOIN NguoiDung nd ON d.IdNguoiDung = nd.Id
                WHERE d.IdPhong = @IdPhong
                ORDER BY d.CreatedAt DESC
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";

            var result = await connection.QueryAsync(sql, new
            {
                IdPhong = idPhong,
                Offset = offset,
                Limit = limit
            });

            return result;
        }

        // Đếm tổng số đánh giá của phòng
        public async Task<int> CountByRoomIdAsync(int idPhong)
        {
            using var connection = _connectionFactory.Create();
            
            var sql = "SELECT COUNT(*) FROM DanhGia WHERE IdPhong = @IdPhong";
            
            return await connection.QuerySingleAsync<int>(sql, new { IdPhong = idPhong });
        }

        // Lấy điểm trung bình của phòng
        public async Task<double> GetAverageRatingAsync(int idPhong)
        {
            using var connection = _connectionFactory.Create();
            
            var sql = @"
                SELECT ISNULL(AVG(CAST(Diem AS FLOAT)), 0) 
                FROM DanhGia 
                WHERE IdPhong = @IdPhong";
            
            return await connection.QuerySingleAsync<double>(sql, new { IdPhong = idPhong });
        }

        // Lấy thống kê đánh giá theo điểm
        public async Task<IEnumerable<dynamic>> GetRatingStatsAsync(int idPhong)
        {
            using var connection = _connectionFactory.Create();
            
            var sql = @"
                SELECT 
                    Diem,
                    COUNT(*) as SoLuong
                FROM DanhGia 
                WHERE IdPhong = @IdPhong
                GROUP BY Diem
                ORDER BY Diem DESC";
            
            return await connection.QueryAsync(sql, new { IdPhong = idPhong });
        }

        // Kiểm tra người dùng đã đánh giá phòng chưa (theo booking)
        public async Task<bool> HasUserRatedRoomAsync(int idNguoiDung, int datPhongId)
        {
            using var connection = _connectionFactory.Create();
            
            var sql = @"
                SELECT COUNT(*) 
                FROM DanhGia 
                WHERE IdNguoiDung = @IdNguoiDung AND DatPhongId = @DatPhongId";
            
            var count = await connection.QuerySingleAsync<int>(sql, new 
            { 
                IdNguoiDung = idNguoiDung, 
                DatPhongId = datPhongId 
            });
            
            return count > 0;
        }

        // Lấy đánh giá theo ID
        public async Task<dynamic?> GetByIdAsync(int id)
        {
            using var connection = _connectionFactory.Create();
            
            var hasAnDanh = await HasColumnAsync(connection, "AnDanh");
            var sql = hasAnDanh
                ? @"SELECT d.*, CASE WHEN d.AnDanh = 1 THEN 'Ẩn danh' ELSE nd.HoTen END AS TenNguoiDung, CASE WHEN d.AnDanh = 1 THEN NULL ELSE nd.Avatar END AS AvatarNguoiDung FROM DanhGia d LEFT JOIN NguoiDung nd ON d.IdNguoiDung = nd.Id WHERE d.Id = @Id"
                : @"SELECT d.*, nd.HoTen AS TenNguoiDung, nd.Avatar AS AvatarNguoiDung FROM DanhGia d LEFT JOIN NguoiDung nd ON d.IdNguoiDung = nd.Id WHERE d.Id = @Id";

            return await connection.QueryFirstOrDefaultAsync(sql, new { Id = id });
        }

        // Cập nhật đánh giá
        public async Task<bool> UpdateAsync(int id, int diem, string binhLuan, string? media = null)
        {
            using var connection = _connectionFactory.Create();
            
            var hasMedia = await HasColumnAsync(connection, "Media");
            var sql = hasMedia
                ? @"UPDATE DanhGia SET Diem = @Diem, BinhLuan = @BinhLuan, Media = @Media WHERE Id = @Id"
                : @"UPDATE DanhGia SET Diem = @Diem, BinhLuan = @BinhLuan WHERE Id = @Id";

            var affected = await connection.ExecuteAsync(sql, new { Id = id, Diem = diem, BinhLuan = binhLuan, Media = media });
            
            return affected > 0;
        }

        // Xóa đánh giá
        public async Task<bool> DeleteAsync(int id)
        {
            using var connection = _connectionFactory.Create();
            
            var sql = "DELETE FROM DanhGia WHERE Id = @Id";
            
            var affected = await connection.ExecuteAsync(sql, new { Id = id });
            
            return affected > 0;
        }

        // Lấy đánh giá của người dùng
        public async Task<IEnumerable<dynamic>> GetByUserIdAsync(int idNguoiDung, int page = 1, int limit = 10)
        {
            using var connection = _connectionFactory.Create();
            
            var offset = (page - 1) * limit;
            
            var sql = @"
                SELECT 
                    d.Id, d.IdNguoiDung, d.IdPhong, d.Diem, d.BinhLuan, d.CreatedAt,
                    d.Media, d.DatPhongId, d.AnDanh,
                    p.TenPhong,
                    p.Anh as AnhPhong,
                    csl.TenCoSo
                FROM DanhGia d
                LEFT JOIN Phong p ON d.IdPhong = p.Id
                LEFT JOIN CoSoLuuTru csl ON p.IdCoSoLuuTru = csl.Id
                WHERE d.IdNguoiDung = @IdNguoiDung
                ORDER BY d.CreatedAt DESC
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY";
            
            return await connection.QueryAsync(sql, new
            {
                IdNguoiDung = idNguoiDung,
                Offset = offset,
                Limit = limit
            });
        }

        // Kiểm tra quyền sở hữu đánh giá
        public async Task<bool> IsOwnerAsync(int id, int idNguoiDung)
        {
            using var connection = _connectionFactory.Create();
            
            var sql = "SELECT COUNT(*) FROM DanhGia WHERE Id = @Id AND IdNguoiDung = @IdNguoiDung";
            
            var count = await connection.QuerySingleAsync<int>(sql, new 
            { 
                Id = id, 
                IdNguoiDung = idNguoiDung 
            });
            
            return count > 0;
        }

        private static async Task<bool> HasColumnAsync(IDbConnection connection, string column)
        {
            var sql = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='DanhGia' AND COLUMN_NAME=@c";
            var cnt = await connection.ExecuteScalarAsync<int>(sql, new { c = column });
            return cnt > 0;
        }
    }
}
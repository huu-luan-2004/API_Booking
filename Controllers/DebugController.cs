using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using HotelBookingApi.Data;
using Dapper;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly SqlConnectionFactory _factory;
    
    public DebugController(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    [HttpPost("reset-accommodation-status")]
    public async Task<IActionResult> ResetAccommodationStatus([FromBody] dynamic body)
    {
        try
        {
            int id = (int)body.id;
            string status = body.status?.ToString() ?? "ChoDuyet";
            
            using var db = _factory.Create();
            await db.ExecuteAsync("UPDATE CoSoLuuTru SET TrangThaiDuyet=@status WHERE Id=@id", new { id, status });
            
            return Ok(new {
                success = true,
                message = $"Đã đặt cơ sở ID {id} về trạng thái {status}",
                data = new { id, status }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    [HttpGet("check-vaitro")]
    public async Task<IActionResult> CheckVaiTroData()
    {
        try 
        {
            using var db = _factory.Create();
            
            // Kiểm tra cấu trúc bảng NguoiDung
            var columns = await db.QueryAsync(@"
                SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'NguoiDung'
                ORDER BY ORDINAL_POSITION
            ");

            // Kiểm tra dữ liệu VaiTro
            var vaiTroData = await db.QueryAsync(@"
                SELECT Id, HoTen, Email, VaiTro, 
                       CASE WHEN VaiTro IS NULL THEN 'NULL' ELSE 'NOT NULL' END as VaiTroStatus
                FROM NguoiDung 
                ORDER BY Id
            ");

            // Kiểm tra các giá trị VaiTro khác nhau
            var vaiTroStats = await db.QueryAsync(@"
                SELECT VaiTro, COUNT(*) as Count
                FROM NguoiDung
                GROUP BY VaiTro
            ");

            return Ok(new {
                success = true,
                message = "Kiểm tra dữ liệu VaiTro trong database",
                tableStructure = columns,
                userData = vaiTroData.Take(10), // 10 users đầu tiên
                vaiTroStatistics = vaiTroStats,
                totalUsers = vaiTroData.Count()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    [HttpGet("database-test")]
    public async Task<IActionResult> TestDatabase()
    {
        try 
        {
            using var db = (SqlConnection)_factory.Create();
            await db.OpenAsync();

            var result = new List<object>();
            
            // Test các bảng chính từ diagram
            var tables = new[] { "NguoiDung", "DatPhong", "Phong", "CoSoLuuTru", "ThanhToan", "TrangThaiDatPhong" };
            
            foreach (var table in tables)
            {
                try 
                {
                    var sql = $"SELECT COUNT(*) FROM {table}";
                    using var cmd = new SqlCommand(sql, db);
                    var count = await cmd.ExecuteScalarAsync();
                    result.Add(new { table, count, status = "OK" });
                }
                catch (Exception ex)
                {
                    result.Add(new { table, count = 0, status = "ERROR", error = ex.Message });
                }
            }

            return Ok(new { 
                success = true, 
                message = "Database connection test completed", 
                data = result 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Database connection failed", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("table-schema/{tableName}")]
    public async Task<IActionResult> GetTableSchema(string tableName)
    {
        try 
        {
            using var db = (SqlConnection)_factory.Create();
            await db.OpenAsync();

            var sql = @"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
                       FROM INFORMATION_SCHEMA.COLUMNS 
                       WHERE TABLE_NAME = @TableName 
                       ORDER BY ORDINAL_POSITION";
            
            using var cmd = new SqlCommand(sql, db);
            cmd.Parameters.AddWithValue("@TableName", tableName);
            
            var columns = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                columns.Add(new {
                    columnName = reader["COLUMN_NAME"]?.ToString(),
                    dataType = reader["DATA_TYPE"]?.ToString(),
                    isNullable = reader["IS_NULLABLE"]?.ToString() == "YES",
                    defaultValue = reader["COLUMN_DEFAULT"]?.ToString()
                });
            }

            return Ok(new { 
                success = true, 
                message = $"Schema for table {tableName}", 
                data = columns 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Failed to get table schema", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("raw-datphong")]
    public async Task<IActionResult> GetRawDatPhong()
    {
        try 
        {
            using var db = (SqlConnection)_factory.Create();
            await db.OpenAsync();

            var sql = "SELECT TOP 5 * FROM DatPhong ORDER BY Id DESC";
            using var cmd = new SqlCommand(sql, db);
            
            var results = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
            }

            return Ok(new { 
                success = true, 
                message = "Raw DatPhong data", 
                data = results 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Failed to get raw data", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("check-table-schema")]
    public async Task<IActionResult> CheckTableSchema()
    {
        try
        {
            using var db = _factory.Create();
            
            // Kiểm tra cấu trúc bảng DatPhong
            var datPhongColumns = await db.QueryAsync(@"
                SELECT COLUMN_NAME, DATA_TYPE 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'DatPhong'
                ORDER BY ORDINAL_POSITION");
            
            // Lấy sample data từ DatPhong
            var sampleDatPhong = await db.QueryAsync(@"
                SELECT TOP 3 * FROM DatPhong 
                ORDER BY Id DESC");
            
            return Ok(new {
                success = true,
                message = "Thông tin schema database",
                data = new {
                    datPhongColumns = datPhongColumns.ToList(),
                    sampleDatPhong = sampleDatPhong.ToList()
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                success = false,
                message = "Lỗi khi kiểm tra schema",
                error = ex.Message
            });
        }
    }
    
    [HttpGet("check-booking-status")]
    public async Task<IActionResult> CheckBookingStatus()
    {
        try
        {
            using var db = _factory.Create();
            
            // Đơn giản hóa - không JOIN với TrangThaiDatPhong
            var sql = @"
                SELECT TOP 20
                    d.Id as IdDatPhong,
                    d.*,
                    tt.Id as IdThanhToan,
                    tt.SoTien,
                    tt.TrangThai as TrangThaiThanhToan,
                    tt.LoaiGiaoDich
                FROM ThanhToan tt
                INNER JOIN DatPhong d ON tt.IdDatPhong = d.Id
                WHERE tt.TrangThai IN ('Thành công', 'Đã thanh toán', 'Completed', 'SUCCESS')
                ORDER BY tt.Id DESC";
            
            var results = await db.QueryAsync(sql);
            
            return Ok(new {
                success = true,
                message = "Kiểm tra trạng thái đặt phòng có thanh toán thành công",
                data = new {
                    chiTiet = results.ToList(),
                    tongSo = results.Count()
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                success = false,
                message = "Lỗi khi kiểm tra",
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }
}
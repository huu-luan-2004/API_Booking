using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace HotelBookingApi.Data;

public class SystemSettingsRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public SystemSettingsRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Lấy tất cả cài đặt hệ thống
    /// </summary>
    public async Task<List<SystemSetting>> GetAllSettingsAsync()
    {
        using var connection = _connectionFactory.Create();
        const string sql = @"
            SELECT SettingKey as [Key], SettingValue as [Value], Description, UpdatedAt
            FROM SystemSettings 
            WHERE IsActive = 1";

        var result = await connection.QueryAsync<SystemSetting>(sql);
        return result.ToList();
    }

    /// <summary>
    /// Lấy giá trị cài đặt theo key
    /// </summary>
    public async Task<string?> GetSettingAsync(string key)
    {
        using var connection = _connectionFactory.Create();
        const string sql = @"
            SELECT SettingValue 
            FROM SystemSettings 
            WHERE SettingKey = @Key AND IsActive = 1";

        return await connection.QuerySingleOrDefaultAsync<string>(sql, new { Key = key });
    }

    /// <summary>
    /// Cập nhật hoặc tạo mới cài đặt
    /// </summary>
    public async Task UpdateSettingAsync(string key, string value, string? description = null)
    {
        using var connection = _connectionFactory.Create();
        
        // Kiểm tra xem setting đã tồn tại chưa
        const string checkSql = "SELECT COUNT(*) FROM SystemSettings WHERE SettingKey = @Key";
        var exists = await connection.QuerySingleAsync<int>(checkSql, new { Key = key }) > 0;

        if (exists)
        {
            const string updateSql = @"
                UPDATE SystemSettings 
                SET SettingValue = @Value, 
                    Description = COALESCE(@Description, Description),
                    UpdatedAt = GETDATE()
                WHERE SettingKey = @Key";

            await connection.ExecuteAsync(updateSql, new { Key = key, Value = value, Description = description });
        }
        else
        {
            const string insertSql = @"
                INSERT INTO SystemSettings (SettingKey, SettingValue, Description, CreatedAt, UpdatedAt, IsActive)
                VALUES (@Key, @Value, @Description, GETDATE(), GETDATE(), 1)";

            await connection.ExecuteAsync(insertSql, new { 
                Key = key, 
                Value = value, 
                Description = description ?? $"Setting for {key}"
            });
        }
    }

    /// <summary>
    /// Log hành động của admin
    /// </summary>
    public async Task LogAdminActionAsync(int adminId, string action, string description)
    {
        using var connection = _connectionFactory.Create();
        const string sql = @"
            INSERT INTO AdminLogs (AdminId, Action, Description, CreatedAt, IpAddress)
            VALUES (@AdminId, @Action, @Description, GETDATE(), @IpAddress)";

        await connection.ExecuteAsync(sql, new { 
            AdminId = adminId, 
            Action = action, 
            Description = description,
            IpAddress = "System" // Có thể lấy từ HttpContext
        });
    }

    /// <summary>
    /// Tạo bản sao lưu
    /// </summary>
    public async Task<int> CreateBackupAsync(string backupName, bool includeUploads)
    {
        using var connection = _connectionFactory.Create();
        
        // Tạo thư mục backups nếu chưa có
        var backupDir = Path.Combine(Directory.GetCurrentDirectory(), "backups");
        Directory.CreateDirectory(backupDir);
        
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"backup_{timestamp}.bak";
        var backupPath = $"backups/{backupFileName}";
        var fullBackupPath = Path.Combine(backupDir, backupFileName);

        const string sql = @"
            INSERT INTO SystemBackups (BackupName, BackupPath, IncludeUploads, CreatedAt, Status, FileSize)
            OUTPUT INSERTED.Id
            VALUES (@BackupName, @BackupPath, @IncludeUploads, GETDATE(), @Status, 0)";

        var backupId = await connection.QuerySingleAsync<int>(sql, new { 
            BackupName = backupName,
            BackupPath = backupPath,
            IncludeUploads = includeUploads,
            Status = "In Progress"
        });

        try
        {
            // Thực hiện backup database thực sự
            await PerformDatabaseBackupAsync(connection, fullBackupPath);
            
            // Nếu bao gồm uploads, copy thư mục wwwroot/uploads
            if (includeUploads)
            {
                await BackupUploadsAsync(backupDir, timestamp);
            }

            // Lấy kích thước file backup
            var fileSize = new FileInfo(fullBackupPath).Length;
            
            // Cập nhật status thành completed
            await UpdateBackupStatusAsync(backupId, "Completed", fileSize);
        }
        catch (Exception ex)
        {
            // Cập nhật status thành failed
            await UpdateBackupStatusAsync(backupId, "Failed", 0);
            await LogErrorAsync($"Backup failed: {ex.Message}");
            throw;
        }

        return backupId;
    }

    /// <summary>
    /// Cập nhật trạng thái backup
    /// </summary>
    private async Task UpdateBackupStatusAsync(int backupId, string status, long fileSize)
    {
        using var connection = _connectionFactory.Create();
        const string sql = @"
            UPDATE SystemBackups 
            SET Status = @Status, FileSize = @FileSize, CompletedAt = CASE WHEN @Status = 'Completed' THEN GETDATE() ELSE CompletedAt END
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new { Id = backupId, Status = status, FileSize = fileSize });
    }

    /// <summary>
    /// Lấy danh sách bản sao lưu
    /// </summary>
    public async Task<List<SystemBackup>> GetBackupsAsync()
    {
        using var connection = _connectionFactory.Create();
        const string sql = @"
            SELECT Id, BackupName, BackupPath, IncludeUploads, CreatedAt, CompletedAt, 
                   Status, FileSize
            FROM SystemBackups 
            ORDER BY CreatedAt DESC";

        var result = await connection.QueryAsync<SystemBackup>(sql);
        return result.ToList();
    }

    /// <summary>
    /// Khôi phục từ bản sao lưu
    /// </summary>
    public async Task RestoreBackupAsync(int backupId)
    {
        using var connection = _connectionFactory.Create();
        
        // Lấy thông tin backup
        const string getBackupSql = "SELECT * FROM SystemBackups WHERE Id = @Id";
        var backup = await connection.QuerySingleOrDefaultAsync<SystemBackup>(getBackupSql, new { Id = backupId });
        
        if (backup == null)
            throw new Exception("Không tìm thấy bản sao lưu");

        if (backup.Status != "Completed")
            throw new Exception("Bản sao lưu chưa hoàn thành hoặc bị lỗi");

        // TODO: Thực hiện restore thực sự ở đây
        // Tạm thời chỉ log
        Console.WriteLine($"Restoring from backup: {backup.BackupPath}");
    }

    /// <summary>
    /// Lấy thống kê hệ thống
    /// </summary>
    public async Task<SystemStats> GetSystemStatsAsync()
    {
        using var connection = _connectionFactory.Create();
        
        const string sql = @"
            SELECT 
                (SELECT COUNT(*) FROM NguoiDung WHERE TrangThai = 'Active') as TotalUsers,
                (SELECT COUNT(*) FROM CoSoLuuTru WHERE TrangThai = 'Hoạt động') as TotalAccommodations,
                (SELECT COUNT(*) FROM Phong) as TotalRooms,
                (SELECT COUNT(*) FROM DatPhong WHERE TrangThai = 'Đã xác nhận') as TotalBookings,
                (SELECT COUNT(*) FROM DatPhong WHERE CAST(NgayTao as date) = CAST(GETDATE() as date)) as TodayBookings,
                (SELECT COUNT(*) FROM NguoiDung WHERE CAST(NgayTao as date) = CAST(GETDATE() as date)) as TodayRegistrations";

        var stats = await connection.QuerySingleAsync<SystemStats>(sql);

        // Thêm thông tin hệ thống
        stats.ServerUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        stats.DatabaseSize = await GetDatabaseSizeAsync();
        stats.LastBackupDate = await GetLastBackupDateAsync();

        return stats;
    }

    /// <summary>
    /// Lấy kích thước database
    /// </summary>
    private async Task<long> GetDatabaseSizeAsync()
    {
        using var connection = _connectionFactory.Create();
        const string sql = @"
            SELECT SUM(CAST(FILEPROPERTY(name, 'SpaceUsed') AS bigint) * 8192) 
            FROM sys.database_files 
            WHERE type = 0"; // Data files only

        var size = await connection.QuerySingleOrDefaultAsync<long?>(sql);
        return size ?? 0;
    }

    /// <summary>
    /// Lấy ngày backup gần nhất
    /// </summary>
    private async Task<DateTime?> GetLastBackupDateAsync()
    {
        using var connection = _connectionFactory.Create();
        const string sql = @"
            SELECT TOP 1 CompletedAt 
            FROM SystemBackups 
            WHERE Status = 'Completed' 
            ORDER BY CompletedAt DESC";

        return await connection.QuerySingleOrDefaultAsync<DateTime?>(sql);
    }

    /// <summary>
    /// Thực hiện backup database
    /// </summary>
    private async Task PerformDatabaseBackupAsync(IDbConnection connection, string backupPath)
    {
        // Lấy tên database từ connection string
        var connectionString = connection.ConnectionString;
        var dbName = ExtractDatabaseNameFromConnectionString(connectionString);
        
        var backupSql = $@"
            BACKUP DATABASE [{dbName}] 
            TO DISK = @BackupPath
            WITH FORMAT, INIT, COMPRESSION;";

        try
        {
            await connection.ExecuteAsync(backupSql, new { BackupPath = backupPath }, commandTimeout: 300);
        }
        catch (Exception ex)
        {
            // Nếu không thể backup qua SQL, tạo export dạng script
            await CreateScriptBackupAsync(connection, backupPath);
        }
    }

    /// <summary>
    /// Tạo backup dạng script SQL
    /// </summary>
    private async Task CreateScriptBackupAsync(IDbConnection connection, string backupPath)
    {
        using var writer = new StreamWriter(backupPath);
        
        // Header
        await writer.WriteLineAsync($"-- Database Backup Script");
        await writer.WriteLineAsync($"-- Created: {DateTime.Now}");
        await writer.WriteLineAsync($"-- Database: QuanLyDatPhong");
        await writer.WriteLineAsync();

        // Export system settings
        await ExportTableDataAsync(connection, writer, "SystemSettings");
        await ExportTableDataAsync(connection, writer, "AdminLogs");
        await ExportTableDataAsync(connection, writer, "SystemBackups");
        
        // Export main data (optional - có thể comment out nếu không muốn backup toàn bộ data)
        await ExportTableDataAsync(connection, writer, "NguoiDung");
        await ExportTableDataAsync(connection, writer, "CoSoLuuTru");
        await ExportTableDataAsync(connection, writer, "Phong");
        await ExportTableDataAsync(connection, writer, "DatPhong");
    }

    /// <summary>
    /// Export dữ liệu của một bảng
    /// </summary>
    private async Task ExportTableDataAsync(IDbConnection connection, StreamWriter writer, string tableName)
    {
        try
        {
            await writer.WriteLineAsync($"-- Table: {tableName}");
            
            // Lấy dữ liệu từ bảng
            var data = await connection.QueryAsync($"SELECT * FROM {tableName}");
            
            foreach (var row in data)
            {
                var dict = (IDictionary<string, object>)row;
                var columns = string.Join(", ", dict.Keys.Select(k => $"[{k}]"));
                var values = string.Join(", ", dict.Values.Select(v => 
                    v == null ? "NULL" : 
                    v is string || v is DateTime ? $"'{v.ToString().Replace("'", "''")}'" : 
                    v.ToString()));
                
                await writer.WriteLineAsync($"INSERT INTO {tableName} ({columns}) VALUES ({values});");
            }
            
            await writer.WriteLineAsync();
        }
        catch (Exception ex)
        {
            await writer.WriteLineAsync($"-- Error exporting {tableName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Backup thư mục uploads
    /// </summary>
    private async Task BackupUploadsAsync(string backupDir, string timestamp)
    {
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        if (!Directory.Exists(uploadsDir))
            return;

        var uploadsBackupDir = Path.Combine(backupDir, $"uploads_{timestamp}");
        Directory.CreateDirectory(uploadsBackupDir);

        // Copy tất cả file trong thư mục uploads
        await Task.Run(() => CopyDirectory(uploadsDir, uploadsBackupDir));
    }

    /// <summary>
    /// Copy directory recursively
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    /// <summary>
    /// Lấy tên database từ connection string
    /// </summary>
    private string ExtractDatabaseNameFromConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return builder.InitialCatalog ?? "QuanLyDatPhong";
    }

    /// <summary>
    /// Log lỗi
    /// </summary>
    private async Task LogErrorAsync(string message)
    {
        try
        {
            using var connection = _connectionFactory.Create();
            const string sql = @"
                INSERT INTO AdminLogs (AdminId, Action, Description, CreatedAt, IpAddress)
                VALUES (0, 'BACKUP_ERROR', @Message, GETDATE(), 'System')";
            
            await connection.ExecuteAsync(sql, new { Message = message });
        }
        catch
        {
            // Ignore logging errors
        }
    }
}

// Models
public class SystemSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SystemBackup
{
    public int Id { get; set; }
    public string BackupName { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public bool IncludeUploads { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "";
    public long FileSize { get; set; }
}

public class SystemStats
{
    public int TotalUsers { get; set; }
    public int TotalAccommodations { get; set; }
    public int TotalRooms { get; set; }
    public int TotalBookings { get; set; }
    public int TodayBookings { get; set; }
    public int TodayRegistrations { get; set; }
    public TimeSpan ServerUptime { get; set; }
    public long DatabaseSize { get; set; }
    public DateTime? LastBackupDate { get; set; }
}
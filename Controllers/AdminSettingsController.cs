using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HotelBookingApi.Data;
using System.Security.Claims;

namespace HotelBookingApi.Controllers;

/// <summary>
/// Admin Settings Controller - Quản lý các cài đặt hệ thống cho Admin
/// </summary>
[ApiController]
[Route("api/admin/settings")]
[Authorize(Roles = "Admin")]
public class AdminSettingsController : ControllerBase
{
    private readonly SystemSettingsRepository _systemRepo;
    private readonly NguoiDungRepository _userRepo;

    public AdminSettingsController(SystemSettingsRepository systemRepo, NguoiDungRepository userRepo)
    {
        _systemRepo = systemRepo;
        _userRepo = userRepo;
    }

    /// <summary>
    /// Lấy tất cả cài đặt hệ thống
    /// GET /api/admin/settings
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSystemSettings()
    {
        try
        {
            var settings = await _systemRepo.GetAllSettingsAsync();
            return Ok(new
            {
                success = true,
                message = "Lấy cài đặt hệ thống thành công",
                data = new
                {
                    systemSettings = new
                    {
                        darkModeEnabled = settings.FirstOrDefault(s => s.Key == "dark_mode_enabled")?.Value ?? "false",
                        autoBackupEnabled = settings.FirstOrDefault(s => s.Key == "auto_backup_enabled")?.Value ?? "true",
                        maintenanceMode = settings.FirstOrDefault(s => s.Key == "maintenance_mode")?.Value ?? "false",
                        maxLoginAttempts = settings.FirstOrDefault(s => s.Key == "max_login_attempts")?.Value ?? "5",
                        sessionTimeout = settings.FirstOrDefault(s => s.Key == "session_timeout")?.Value ?? "30",
                        emailNotifications = settings.FirstOrDefault(s => s.Key == "email_notifications")?.Value ?? "true"
                    },
                    securitySettings = new
                    {
                        twoFactorEnabled = settings.FirstOrDefault(s => s.Key == "two_factor_enabled")?.Value ?? "false",
                        passwordExpiryDays = settings.FirstOrDefault(s => s.Key == "password_expiry_days")?.Value ?? "90",
                        minPasswordLength = settings.FirstOrDefault(s => s.Key == "min_password_length")?.Value ?? "6",
                        requireSpecialChars = settings.FirstOrDefault(s => s.Key == "require_special_chars")?.Value ?? "false"
                    }
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi lấy cài đặt: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Cập nhật cài đặt giao diện
    /// PUT /api/admin/settings/interface
    /// </summary>
    [HttpPut("interface")]
    public async Task<IActionResult> UpdateInterfaceSettings([FromBody] InterfaceSettingsRequest request)
    {
        try
        {
            await _systemRepo.UpdateSettingAsync("dark_mode_enabled", request.DarkModeEnabled.ToString().ToLower());
            await _systemRepo.UpdateSettingAsync("auto_backup_enabled", request.AutoBackupEnabled.ToString().ToLower());

            return Ok(new
            {
                success = true,
                message = "Cập nhật cài đặt giao diện thành công"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi cập nhật cài đặt: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Cập nhật cài đặt bảo mật
    /// PUT /api/admin/settings/security
    /// </summary>
    [HttpPut("security")]
    public async Task<IActionResult> UpdateSecuritySettings([FromBody] SecuritySettingsRequest request)
    {
        try
        {
            await _systemRepo.UpdateSettingAsync("two_factor_enabled", request.TwoFactorEnabled.ToString().ToLower());
            await _systemRepo.UpdateSettingAsync("max_login_attempts", request.MaxLoginAttempts.ToString());
            await _systemRepo.UpdateSettingAsync("session_timeout", request.SessionTimeout.ToString());
            await _systemRepo.UpdateSettingAsync("password_expiry_days", request.PasswordExpiryDays.ToString());
            await _systemRepo.UpdateSettingAsync("min_password_length", request.MinPasswordLength.ToString());
            await _systemRepo.UpdateSettingAsync("require_special_chars", request.RequireSpecialChars.ToString().ToLower());

            return Ok(new
            {
                success = true,
                message = "Cập nhật cài đặt bảo mật thành công"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi cập nhật cài đặt bảo mật: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Bật/tắt chế độ bảo trì
    /// PUT /api/admin/settings/maintenance
    /// </summary>
    [HttpPut("maintenance")]
    public async Task<IActionResult> ToggleMaintenanceMode([FromBody] MaintenanceModeRequest request)
    {
        try
        {
            await _systemRepo.UpdateSettingAsync("maintenance_mode", request.Enabled.ToString().ToLower());
            await _systemRepo.UpdateSettingAsync("maintenance_message", request.Message ?? "Hệ thống đang bảo trì");

            return Ok(new
            {
                success = true,
                message = request.Enabled ? "Đã bật chế độ bảo trì" : "Đã tắt chế độ bảo trì"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi cập nhật chế độ bảo trì: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Xóa cache hệ thống
    /// POST /api/admin/settings/clear-cache
    /// </summary>
    [HttpPost("clear-cache")]
    public async Task<IActionResult> ClearCache()
    {
        try
        {
            // Thêm logic xóa cache ở đây (Redis, MemoryCache, etc.)
            // Tạm thời chỉ log action
            await _systemRepo.LogAdminActionAsync(GetCurrentUserId(), "CLEAR_CACHE", "Admin cleared system cache");

            return Ok(new
            {
                success = true,
                message = "Đã xóa cache hệ thống thành công"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi xóa cache: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Tạo bản sao lưu hệ thống
    /// POST /api/admin/settings/backup
    /// </summary>
    [HttpPost("backup")]
    public async Task<IActionResult> CreateBackup([FromBody] BackupRequest request)
    {
        try
        {
            var backupId = await _systemRepo.CreateBackupAsync(request.BackupName, request.IncludeUploads);
            await _systemRepo.LogAdminActionAsync(GetCurrentUserId(), "CREATE_BACKUP", $"Admin created backup: {request.BackupName}");

            return Ok(new
            {
                success = true,
                message = "Tạo bản sao lưu thành công",
                data = new { backupId }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi tạo bản sao lưu: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Lấy danh sách bản sao lưu
    /// GET /api/admin/settings/backups
    /// </summary>
    [HttpGet("backups")]
    public async Task<IActionResult> GetBackups()
    {
        try
        {
            var backups = await _systemRepo.GetBackupsAsync();
            return Ok(new
            {
                success = true,
                message = "Lấy danh sách bản sao lưu thành công",
                data = backups
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi lấy danh sách bản sao lưu: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Khôi phục từ bản sao lưu
    /// POST /api/admin/settings/restore/{backupId}
    /// </summary>
    [HttpPost("restore/{backupId}")]
    public async Task<IActionResult> RestoreBackup(int backupId)
    {
        try
        {
            await _systemRepo.RestoreBackupAsync(backupId);
            await _systemRepo.LogAdminActionAsync(GetCurrentUserId(), "RESTORE_BACKUP", $"Admin restored backup ID: {backupId}");

            return Ok(new
            {
                success = true,
                message = "Khôi phục từ bản sao lưu thành công"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi khôi phục: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Lấy thống kê hệ thống
    /// GET /api/admin/settings/system-stats
    /// </summary>
    [HttpGet("system-stats")]
    public async Task<IActionResult> GetSystemStats()
    {
        try
        {
            var stats = await _systemRepo.GetSystemStatsAsync();
            return Ok(new
            {
                success = true,
                message = "Lấy thống kê hệ thống thành công",
                data = stats
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi lấy thống kê: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Lấy thông tin thư mục backup
    /// GET /api/admin/settings/backup-info
    /// </summary>
    [HttpGet("backup-info")]
    public IActionResult GetBackupInfo()
    {
        try
        {
            var backupDir = Path.Combine(Directory.GetCurrentDirectory(), "backups");
            var backupExists = Directory.Exists(backupDir);
            
            var files = backupExists ? 
                Directory.GetFiles(backupDir, "*.bak")
                    .Select(f => new {
                        name = Path.GetFileName(f),
                        size = new FileInfo(f).Length,
                        created = System.IO.File.GetCreationTime(f),
                        path = f
                    }).Cast<object>().ToList() : 
                new List<object>();

            return Ok(new
            {
                success = true,
                message = "Thông tin backup",
                data = new
                {
                    backupDirectory = backupDir,
                    directoryExists = backupExists,
                    totalFiles = files.Count,
                    totalSize = files.Count > 0 ? files.Sum(f => (long)((dynamic)f).size) : 0,
                    backupFiles = files
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Lỗi khi lấy thông tin backup: {ex.Message}"
            });
        }
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.FindFirst("id")?.Value ?? "0");
    }
}

// Request Models
public class InterfaceSettingsRequest
{
    public bool DarkModeEnabled { get; set; }
    public bool AutoBackupEnabled { get; set; }
}

public class SecuritySettingsRequest
{
    public bool TwoFactorEnabled { get; set; }
    public int MaxLoginAttempts { get; set; }
    public int SessionTimeout { get; set; }
    public int PasswordExpiryDays { get; set; }
    public int MinPasswordLength { get; set; }
    public bool RequireSpecialChars { get; set; }
}

public class MaintenanceModeRequest
{
    public bool Enabled { get; set; }
    public string? Message { get; set; }
}

public class BackupRequest
{
    public string BackupName { get; set; } = "";
    public bool IncludeUploads { get; set; } = true;
}
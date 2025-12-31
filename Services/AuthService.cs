using BCrypt.Net;
using HotelBookingApi.Data;

namespace HotelBookingApi.Services;

public class AuthService
{
    private readonly NguoiDungRepository _nguoiDungRepo;
    private readonly JwtService _jwtService;

    public AuthService(NguoiDungRepository nguoiDungRepo, JwtService jwtService)
    {
        _nguoiDungRepo = nguoiDungRepo;
        _jwtService = jwtService;
    }

    // Đăng ký tài khoản mới
    public async Task<(bool success, string message, object? data)> RegisterAsync(
        string email, string password, string hoTen, string? soDienThoai, string vaiTro = "User")
    {
        try
        {
            // Kiểm tra email đã tồn tại
            var existingUser = await _nguoiDungRepo.GetByEmailAsync(email);
            if (existingUser != null)
            {
                return (false, "Email đã được sử dụng", null);
            }

            // Validate password
            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                return (false, "Mật khẩu phải có ít nhất 6 ký tự", null);
            }

            // Hash password
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

            // Tạo user mới
            int userId = await _nguoiDungRepo.CreateAsync(
                email, hashedPassword, hoTen, soDienThoai, vaiTro, "Active");

            // Generate JWT token
            var token = _jwtService.GenerateToken(userId.ToString(), email, vaiTro);

            return (true, "Đăng ký thành công", new { 
                userId, 
                email, 
                hoTen, 
                vaiTro,
                token 
            });
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi đăng ký: {ex.Message}", null);
        }
    }

    // Đăng nhập
    public async Task<(bool success, string message, object? data)> LoginAsync(string email, string password)
    {
        try
        {
            // Tìm user theo email
            var user = await _nguoiDungRepo.GetByEmailAsync(email);
            if (user == null)
            {
                return (false, "Email hoặc mật khẩu không đúng", null);
            }

            // Kiểm tra trạng thái tài khoản
            if (user.TrangThai != "Active")
            {
                return (false, $"Tài khoản đang ở trạng thái: {user.TrangThai}", null);
            }

            // Verify password
            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(password, user.MatKhau);
            if (!isPasswordValid)
            {
                return (false, "Email hoặc mật khẩu không đúng", null);
            }

            // Generate JWT token
            var token = _jwtService.GenerateToken(
                user.Id.ToString(), 
                user.Email, 
                user.VaiTro ?? "User");

            return (true, "Đăng nhập thành công", new {
                userId = user.Id,
                email = user.Email,
                hoTen = user.HoTen,
                vaiTro = user.VaiTro,
                token
            });
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi đăng nhập: {ex.Message}", null);
        }
    }

    // Đổi mật khẩu
    public async Task<(bool success, string message)> ChangePasswordAsync(
        int userId, string oldPassword, string newPassword)
    {
        try
        {
            var user = await _nguoiDungRepo.GetByIdAsync(userId);
            if (user == null)
            {
                return (false, "Không tìm thấy tài khoản");
            }

            // Verify old password
            bool isOldPasswordValid = BCrypt.Net.BCrypt.Verify(oldPassword, user.MatKhau);
            if (!isOldPasswordValid)
            {
                return (false, "Mật khẩu cũ không đúng");
            }

            // Validate new password
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            {
                return (false, "Mật khẩu mới phải có ít nhất 6 ký tự");
            }

            // Hash new password
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);

            // Update password
            await _nguoiDungRepo.UpdatePasswordAsync(userId, hashedPassword);

            return (true, "Đổi mật khẩu thành công");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi đổi mật khẩu: {ex.Message}");
        }
    }

    // Reset mật khẩu (dành cho admin)
    public async Task<(bool success, string message, string? newPassword)> ResetPasswordAsync(int userId)
    {
        try
        {
            var user = await _nguoiDungRepo.GetByIdAsync(userId);
            if (user == null)
            {
                return (false, "Không tìm thấy tài khoản", null);
            }

            // Generate random password
            string newPassword = GenerateRandomPassword();
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);

            // Update password
            await _nguoiDungRepo.UpdatePasswordAsync(userId, hashedPassword);

            return (true, "Reset mật khẩu thành công", newPassword);
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi reset mật khẩu: {ex.Message}", null);
        }
    }

    // Set mật khẩu cụ thể cho user (dành cho admin)
    public async Task<(bool success, string message)> SetPasswordAsync(int userId, string newPassword)
    {
        try
        {
            var user = await _nguoiDungRepo.GetByIdAsync(userId);
            if (user == null)
            {
                return (false, "Không tìm thấy tài khoản");
            }

            // Validate new password
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            {
                return (false, "Mật khẩu mới phải có ít nhất 6 ký tự");
            }

            // Hash new password
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);

            // Update password
            await _nguoiDungRepo.UpdatePasswordAsync(userId, hashedPassword);

            return (true, "Set mật khẩu thành công");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi set mật khẩu: {ex.Message}");
        }
    }

    // Set mật khẩu cho tất cả user (dành cho admin)
    public async Task<(bool success, string message, int count)> SetPasswordForAllAsync(string newPassword)
    {
        try
        {
            // Validate password
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            {
                return (false, "Mật khẩu mới phải có ít nhất 6 ký tự", 0);
            }

            // Hash password một lần
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);

            // Lấy tất cả user
            var (users, total) = await _nguoiDungRepo.ListAsync(1, 10000, null);

            int count = 0;
            foreach (var user in users)
            {
                await _nguoiDungRepo.UpdatePasswordAsync(user.Id, hashedPassword);
                count++;
            }

            return (true, $"Đã set mật khẩu cho {count} user", count);
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi set mật khẩu: {ex.Message}", 0);
        }
    }

    // Helper: Generate random password
    private string GenerateRandomPassword(int length = 8)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}

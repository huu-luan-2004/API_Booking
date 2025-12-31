using Google.Apis.Auth;
using HotelBookingApi.Data;

namespace HotelBookingApi.Services;

public class GoogleAuthService
{
    private readonly IConfiguration _config;
    private readonly NguoiDungRepository _users;
    private readonly JwtService _jwt;

    public GoogleAuthService(IConfiguration config, NguoiDungRepository users, JwtService jwt)
    {
        _config = config;
        _users = users;
        _jwt = jwt;
    }

    public class GoogleLoginResult
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public object? data { get; set; }
    }

    public async Task<GoogleLoginResult> LoginWithIdTokenAsync(string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return new GoogleLoginResult { success = false, message = "Thiếu idToken" };
        }

        // Hỗ trợ nhiều Client ID (Web/Android/iOS). Nếu không khai báo danh sách, dùng ClientId đơn.
        var allowedClientIds = _config.GetSection("GoogleAuth:AllowedClientIds").Get<string[]>() ?? Array.Empty<string>();
        if (allowedClientIds.Length == 0)
        {
            var clientId = _config["GoogleAuth:ClientId"] ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                allowedClientIds = new[] { clientId };
            }
        }
        if (allowedClientIds == null || allowedClientIds.Length == 0)
        {
            return new GoogleLoginResult { success = false, message = "Chưa cấu hình GoogleAuth:ClientId hoặc AllowedClientIds" };
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = allowedClientIds
            });
        }
        catch (Exception ex)
        {
            return new GoogleLoginResult { success = false, message = $"ID token không hợp lệ: {ex.Message}" };
        }

        var email = payload.Email;
        var name = payload.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return new GoogleLoginResult { success = false, message = "Không lấy được email từ Google" };
        }

        var user = await _users.GetByEmailAsync(email);
        int userId;
        string role;
        if (user == null)
        {
            // Auto-create user as KhachHang
            var hashed = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N").Substring(0, 12));
            userId = await _users.CreateAsync(email, hashed, name, null, "KhachHang", "Active");
            role = "KhachHang";
        }
        else
        {
            userId = (int)user.Id;
            role = (user.VaiTro?.ToString()) ?? "KhachHang";
        }

        var token = _jwt.GenerateToken(userId.ToString(), email, role);
        return new GoogleLoginResult
        {
            success = true,
            message = "Đăng nhập Google thành công",
            data = new
            {
                userId,
                email,
                hoTen = name,
                vaiTro = role,
                token,
                googleSub = payload.Subject,
                picture = payload.Picture
            }
        };
    }
}

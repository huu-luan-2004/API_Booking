using HotelBookingApi.Data;
using HotelBookingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly FirebaseService _firebase;
    private readonly NguoiDungRepository _repo;
    private readonly JwtService _jwt;
    public AuthController(FirebaseService firebase, NguoiDungRepository repo, JwtService jwt)
    {
        _firebase = firebase; _repo = repo; _jwt = jwt;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] System.Text.Json.JsonElement body)
    {
        // Extract idToken from multiple possible keys for compatibility
        string idToken = string.Empty;
        foreach (var key in new[] { "idToken", "firebaseIdToken", "firebaseToken", "token" })
        {
            if (body.TryGetProperty(key, out var prop))
            {
                idToken = prop.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(idToken)) break;
            }
        }

        string? hoTen = body.TryGetProperty("hoTen", out var ht) ? ht.GetString() : null;
        string? soDienThoai = body.TryGetProperty("soDienThoai", out var sdt) ? sdt.GetString() : null;

        if (string.IsNullOrWhiteSpace(idToken))
            return BadRequest(new { success=false, message="Thiếu idToken" });

        var decoded = await _firebase.VerifyIdTokenAsync(idToken);
        if (decoded is null) return BadRequest(new { success=false, message="ID token không hợp lệ" });

        var fuid = decoded.Value.Uid; var email = decoded.Value.Email; var name = hoTen ?? decoded.Value.Name;
        var user = await _repo.FindByFirebaseUidAsync(fuid) ?? (email != null ? await _repo.FindByEmailAsync(email) : null);
        if (user is null)
        {
            await _repo.CreateUserAsync(email, name, soDienThoai, fuid, "KhachHang");
            user = await _repo.FindByFirebaseUidAsync(fuid) ?? (email != null ? await _repo.FindByEmailAsync(email) : null);
        }
        return Ok(new { success=true, message="Đăng ký thành công với Firebase", data=new { user } });
    }

    public class LoginRequest
    {
        public string idToken { get; set; } = string.Empty;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? body)
    {
        // Accept Firebase ID token from either body.idToken or Authorization: Bearer <idToken>
        string idToken = body?.idToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(idToken))
        {
            if (Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var auth = authHeader.ToString();
                const string prefix = "Bearer ";
                if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var possible = auth.Substring(prefix.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(possible)) idToken = possible;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(idToken))
            return BadRequest(new { success=false, message="Thiếu idToken (gửi body.idToken hoặc Authorization: Bearer <Firebase ID token>)" });

        var decoded = await _firebase.VerifyIdTokenAsync(idToken);
        if (decoded is null) return BadRequest(new { success=false, message="ID token không hợp lệ" });

        var fuid = decoded.Value.Uid; var email = decoded.Value.Email; var name = decoded.Value.Name; var phone = decoded.Value.Phone;
        var user = await _repo.FindByFirebaseUidAsync(fuid) ?? (email != null ? await _repo.FindByEmailAsync(email) : null);
        if (user is null)
        {
            var id = await _repo.CreateUserAsync(email, name, phone, fuid, "KhachHang");
            user = await _repo.FindByFirebaseUidAsync(fuid) ?? (email != null ? await _repo.FindByEmailAsync(email) : null);
        }

        var roles = await _repo.GetRolesAsync(user!);
        var perms = await _repo.GetPermissionsAsync(roles);
        int idUser = (int)user!.Id;
        var token = _jwt.CreateToken(idUser, (string?)user.Email, fuid, roles, perms);
        return Ok(new { success=true, message="Đăng nhập thành công", data = new { user, roles, permissions=perms, token, accessToken=token } });
    }

    [HttpGet("login")]
    public IActionResult LoginInfo()
    {
        return Ok(new { 
            success = true, 
            message = "Login endpoint chỉ hỗ trợ POST method",
            usage = "POST /api/auth/login với body: { \"idToken\": \"firebase_token\" }"
        });
    }

    [Authorize]
    [HttpGet("profile")]
    public IActionResult Profile()
    {
        return Ok(new { user = new { id = User.FindFirst("id")?.Value, email = User.FindFirst("email")?.Value } });
    }
}

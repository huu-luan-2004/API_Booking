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
    private readonly ILogger<AuthController> _logger;
    public AuthController(FirebaseService firebase, NguoiDungRepository repo, JwtService jwt, ILogger<AuthController> logger)
    {
        _firebase = firebase; _repo = repo; _jwt = jwt; _logger = logger;
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

        try
        {
            var fuid = decoded.Value.Uid; var email = decoded.Value.Email; var name = decoded.Value.Name; var phone = decoded.Value.Phone;
            var user = await _repo.FindByFirebaseUidAsync(fuid) ?? (email != null ? await _repo.FindByEmailAsync(email) : null);
            if (user is null)
            {
                var id = await _repo.CreateUserAsync(email, name, phone, fuid, "KhachHang");
                user = await _repo.FindByFirebaseUidAsync(fuid) ?? (email != null ? await _repo.FindByEmailAsync(email) : null);
            }

            var roles = await _repo.GetRolesAsync(user!);
            var perms = await _repo.GetPermissionsAsync(roles);
            int idUser = Convert.ToInt32(user!.Id);
            var token = _jwt.CreateToken(idUser, (string?)user.Email, fuid, roles, perms);
            return Ok(new { success=true, message="Đăng nhập thành công", data = new { user, roles, permissions=perms, token, accessToken=token } });
        }
        catch (Exception ex)
        {
            // Trả về thông tin lỗi gợi ý trong môi trường Development để dễ debug
            _logger.LogError(ex, "[Auth/Login] Unexpected error. fuid={Fuid}, email={Email}", decoded.Value.Uid, decoded.Value.Email);
            return StatusCode(500, new { success=false, message="Lỗi máy chủ khi xử lý đăng nhập", error = ex.Message });
        }
    }

    // Login bằng Google qua Firebase trên FE
    // FE dùng signInWithPopup(new GoogleAuthProvider()) -> lấy Firebase ID Token -> gửi vào đây
    [HttpPost("google")]
    public Task<IActionResult> GoogleLogin([FromBody] LoginRequest? body)
        => Login(body);

    // Hỗ trợ GET cho trường hợp test nhanh trên trình duyệt:
    // - Nếu có header Authorization: Bearer <Firebase ID token> thì xử lý đăng nhập giống POST
    // - Nếu không có, trả về hướng dẫn sử dụng
    [HttpGet("google")]
    public async Task<IActionResult> GoogleLoginGet()
    {
        string idToken = string.Empty;
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var auth = authHeader.ToString();
            const string prefix = "Bearer ";
            if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                idToken = auth.Substring(prefix.Length).Trim();
            }
        }
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return Ok(new {
                success = false,
                message = "Vui lòng gọi POST /api/auth/google với body { idToken } hoặc gửi GET với header Authorization: Bearer <Firebase ID token>",
                usage = new {
                    post = new { path = "/api/auth/google", body = new { idToken = "<FIREBASE_ID_TOKEN>" } },
                    get = new { path = "/api/auth/google", header = "Authorization: Bearer <FIREBASE_ID_TOKEN>" }
                }
            });
        }
        return await Login(new LoginRequest { idToken = idToken });
    }

    // Làm mới JWT của backend bằng Firebase ID token mới
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] LoginRequest? body)
    {
        var idToken = body?.idToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(idToken))
            return BadRequest(new { success=false, message="Thiếu idToken" });

        var decoded = await _firebase.VerifyIdTokenAsync(idToken);
        if (decoded is null) return BadRequest(new { success=false, message="ID token không hợp lệ" });

        try
        {
            var fuid = decoded.Value.Uid; var email = decoded.Value.Email; var name = decoded.Value.Name; var phone = decoded.Value.Phone;
            var user = await _repo.FindByFirebaseUidAsync(fuid) ?? (email != null ? await _repo.FindByEmailAsync(email) : null);
            if (user is null)
            {
                var id = await _repo.CreateUserAsync(email, name, phone, fuid, "KhachHang");
                user = await _repo.GetByIdAsync(id);
            }
            var roles = await _repo.GetRolesAsync(user!);
            var perms = await _repo.GetPermissionsAsync(roles);
            int idUser = Convert.ToInt32(user!.Id);
            var token = _jwt.CreateToken(idUser, (string?)user.Email, fuid, roles, perms);
            return Ok(new { success=true, message="Làm mới token thành công", data = new { user, roles, permissions=perms, token, accessToken=token } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Auth/RefreshToken] Unexpected error. fuid={Fuid}, email={Email}", decoded.Value.Uid, decoded.Value.Email);
            return StatusCode(500, new { success=false, message="Lỗi máy chủ khi làm mới token", error = ex.Message });
        }
    }

    // Hỗ trợ GET tương tự /google để test nhanh:
    // - Nếu có Authorization: Bearer <Firebase ID token> thì xử lý làm mới token
    // - Nếu không có sẽ trả usage hướng dẫn
    [HttpGet("refresh-token")]
    public async Task<IActionResult> RefreshTokenGet()
    {
        string idToken = string.Empty;
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var auth = authHeader.ToString();
            const string prefix = "Bearer ";
            if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                idToken = auth.Substring(prefix.Length).Trim();
            }
        }
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return Ok(new {
                success = false,
                message = "Vui lòng gọi POST /api/auth/refresh-token với body { idToken } hoặc gửi GET với header Authorization: Bearer <Firebase ID token>",
                usage = new {
                    post = new { path = "/api/auth/refresh-token", body = new { idToken = "<FIREBASE_ID_TOKEN>" } },
                    get = new { path = "/api/auth/refresh-token", header = "Authorization: Bearer <FIREBASE_ID_TOKEN>" }
                }
            });
        }
        return await RefreshToken(new LoginRequest { idToken = idToken });
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

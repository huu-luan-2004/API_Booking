using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FirebaseAdmin.Auth;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private static string NormalizeRole(string? input)
    {
        if (input is null) return "KhachHang";
        var key = new string(input.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).Where(c => char.IsLetter(c)).ToArray());
        if (key.Contains("admin")) return "Admin";
        if (key.Contains("chucoso") || key.Contains("chucso") || key.Contains("owner")) return "ChuCoSo";
        if (key.Contains("khachhang") || key.Contains("khach") || key.Contains("customer")) return "KhachHang";
        return "KhachHang";
    }

    private readonly NguoiDungRepository _repo;
    public AdminController(NguoiDungRepository repo) => _repo = repo;

    [Authorize(Roles="Admin")]
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] System.Text.Json.JsonElement body)
    {
        static string? ReadString(System.Text.Json.JsonElement je, string prop)
        {
            return je.TryGetProperty(prop, out var v) ? v.GetString() : null;
        }
        string? email = ReadString(body, "email");
        string? password = ReadString(body, "password");
        string? roleRaw = ReadString(body, "role") ?? ReadString(body, "vaiTro");
        string name = ReadString(body, "name") ?? ReadString(body, "hoTen") ?? string.Empty;
        string? phone = ReadString(body, "phone") ?? ReadString(body, "soDienThoai");

        // Chuẩn hóa số điện thoại về định dạng E.164 (+84...) nếu có cung cấp
        static string? NormalizePhoneE164(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            s = s.Replace(" ", string.Empty).Replace("-", string.Empty).Replace(".", string.Empty)
                 .Replace("(", string.Empty).Replace(")", string.Empty);
            if (s.StartsWith('+')) return s;              // đã ở dạng E.164
            if (s.StartsWith("84")) return "+" + s;     // thiếu dấu +
            if (s.StartsWith("0")) return "+84" + s[1..]; // VN nội địa -> +84
            return null; // không rõ quốc gia/định dạng
        }
        var normalizedPhone = NormalizePhoneE164(phone);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return BadRequest(new { success=false, message="Thiếu email hoặc password" });

    var vaiTro = NormalizeRole(roleRaw);
        if (vaiTro != "ChuCoSo")
            return BadRequest(new { success=false, message="Endpoint này chỉ dùng để tạo tài khoản Chủ cơ sở. Khách hàng vui lòng tự đăng ký qua /api/auth/register." });

        if (!string.IsNullOrWhiteSpace(phone) && normalizedPhone is null)
            return BadRequest(new { success=false, message="Số điện thoại không hợp lệ. Vui lòng nhập theo định dạng E.164 bắt đầu bằng dấu '+' (ví dụ: +84901234567). Bạn có thể để trống nếu không muốn lưu số điện thoại." });

        UserRecord userRecord;
        try
        {
            var args = new UserRecordArgs { Email = email!, Password = password!, DisplayName = name, EmailVerified = false, Disabled = false };
            if (!string.IsNullOrWhiteSpace(normalizedPhone)) args.PhoneNumber = normalizedPhone;
            userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(args);
        }
        catch (ArgumentException)
        {
            return BadRequest(new { success=false, message="Số điện thoại không hợp lệ. Vui lòng nhập theo định dạng E.164 bắt đầu bằng dấu '+' (ví dụ: +84901234567)." });
        }
        catch (FirebaseAuthException e) when (e.AuthErrorCode == AuthErrorCode.EmailAlreadyExists)
        {
            return Conflict(new { success=false, message="Email đã tồn tại trên hệ thống Firebase" });
        }

        try
        {
            await _repo.CreateUserAsync(email, name, phone, userRecord.Uid, vaiTro);
        }
        catch (Exception ex)
        {
            try { await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs { Uid = userRecord.Uid, Disabled = true }); } catch {}
            var msg = ex.Message.ToLowerInvariant();
            if (msg.Contains("duplicate") || msg.Contains("unique") || msg.Contains("violation"))
                return Conflict(new { success=false, message="Email đã tồn tại trong cơ sở dữ liệu" });
            return StatusCode(500, new { success=false, message="Lỗi khi tạo tài khoản trong DB" });
        }

        var local = await _repo.FindByFirebaseUidAsync(userRecord.Uid) ?? await _repo.FindByEmailAsync(email);
        return StatusCode(201, new { success=true, message="Tạo tài khoản thành công", data = new { user = local } });
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HotelBookingApi.Services;

public class JwtService
{
    private readonly IConfiguration _config;
    public JwtService(IConfiguration config) => _config = config;

    public string CreateToken(int id, string? email, string firebaseUid, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new Claim("id", id.ToString()),
            new Claim("firebaseUid", firebaseUid)
        };
        if (!string.IsNullOrWhiteSpace(email)) claims.Add(new Claim("email", email!));
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        foreach (var p in permissions) claims.Add(new Claim("perm", p));

        var secret = _config["JWT_SECRET"] ?? "your_jwt_secret_key";
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);
        // Token sống 7 ngày để app có thể lưu và tự đăng nhập lại
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddDays(7), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

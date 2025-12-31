using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HotelBookingApi.Services;

public class JwtService
{
    private readonly IConfiguration _config;
    public JwtService(IConfiguration config) => _config = config;

    // Generate token cho Database Authentication
    public string GenerateToken(string userId, string email, string role)
    {
        var claims = new List<Claim>
        {
            new Claim("id", userId),
            new Claim("email", email),
            new Claim(ClaimTypes.Role, role)
        };

        var secret = _config["JWT_SECRET"] ?? "your_jwt_secret_key";
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), 
            SecurityAlgorithms.HmacSha256);
        
        var token = new JwtSecurityToken(
            claims: claims, 
            expires: DateTime.UtcNow.AddDays(7), 
            signingCredentials: creds);
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

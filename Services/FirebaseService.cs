using FirebaseAdmin.Auth;

namespace HotelBookingApi.Services;

public class FirebaseService
{
    public async Task<(string Uid, string? Email, string? Name, string? Phone)?> VerifyIdTokenAsync(string idToken)
    {
        idToken = (idToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Split('.').Length != 3)
            return null;
        try
        {
            var decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
            var uid = decoded.Uid;
            var email = decoded.Claims.TryGetValue("email", out var e) ? e?.ToString() : null;
            var name = decoded.Claims.TryGetValue("name", out var n) ? n?.ToString() : null;
            var phone = decoded.Claims.TryGetValue("phone_number", out var p) ? p?.ToString() : null;
            return (uid, email, name, phone);
        }
        catch
        {
            return null;
        }
    }
}

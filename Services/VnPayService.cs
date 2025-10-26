using System.Security.Cryptography;
using System.Text;
using System.Net;

namespace HotelBookingApi.Services;

public class VnPayService
{
    private readonly IConfiguration _config;
    public VnPayService(IConfiguration config) => _config = config;

    public string BuildPaymentUrl(IDictionary<string, string> parameters)
    {
        var paymentUrl = _config["VNPAY_URL"] ?? string.Empty;
        var hashSecret = _config["VNPAY_HASH_SECRET"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(paymentUrl) || string.IsNullOrWhiteSpace(hashSecret))
            throw new InvalidOperationException("VNPAY config is missing");

        // VNPAY yêu cầu sắp xếp a-z theo tên tham số, chỉ ký dữ liệu không rỗng, chỉ mã hoá URL giá trị (không mã hoá key)
        var rawData = BuildDataToSign(parameters);
        var secureHash = HmacSHA512(hashSecret, rawData);
        var query = BuildQuery(parameters) + "&vnp_SecureHash=" + secureHash + "&vnp_SecureHashType=HmacSHA512";
        return paymentUrl + "?" + query;
    }

    public bool ValidateReturn(IDictionary<string, string> queryParams)
    {
        if (!queryParams.TryGetValue("vnp_SecureHash", out var secureHash)) return false;
        var hashSecret = _config["VNPAY_HASH_SECRET"] ?? string.Empty;

        // Tạo bản sao trừ 2 khoá hash
        var data = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in queryParams)
        {
            if (string.Equals(kv.Key, "vnp_SecureHash", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(kv.Key, "vnp_SecureHashType", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(kv.Value)) data[kv.Key] = kv.Value;
        }
        var rawData = BuildDataToSign(data);
        var myHash = HmacSHA512(hashSecret, rawData);
        return string.Equals(myHash, secureHash, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetClientIp(HttpContext httpContext)
    {
        var ip = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(ip)) ip = httpContext.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip;
    }

    private static string BuildDataToSign(IDictionary<string, string> parameters)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in parameters)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            {
                sorted[kv.Key] = kv.Value;
            }
        }
        var sb = new StringBuilder();
        foreach (var kv in sorted)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(kv.Key);
            sb.Append('=');
            // Chỉ mã hoá URL giá trị theo UTF-8; chuyển khoảng trắng thành '+' để gần với HttpUtility
            sb.Append(UrlEncodeValue(kv.Value));
        }
        return sb.ToString();
    }

    private static string BuildQuery(IDictionary<string, string> parameters)
    {
        var sb = new StringBuilder();
        foreach (var kv in new SortedDictionary<string, string>(parameters, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            if (sb.Length > 0) sb.Append('&');
            // Theo sample, chỉ cần encode value; encode key cũng không sao nhưng giữ đồng nhất
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(UrlEncodeValue(kv.Value));
        }
        return sb.ToString();
    }

    private static string UrlEncodeValue(string value)
    {
        // VNPAY sample thường dùng HttpUtility.UrlEncode (space -> '+').
        // WebUtility.UrlEncode dùng "%20" cho space. Để tương thích hơn, chuyển %20 -> +
        var encoded = WebUtility.UrlEncode(value) ?? string.Empty;
        return encoded.Replace("%20", "+");
    }

    private static string HmacSHA512(string key, string data)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA512(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        // VNPAY thường dùng hex IN HOA
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("X2"));
        return sb.ToString();
    }
}

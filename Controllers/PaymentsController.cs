using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HotelBookingApi.Services;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly ThanhToanRepository _payRepo;
    private readonly DatPhongRepository _bookingRepo;
    private readonly IConfiguration _config;
    private readonly VnPayService _vnPay;

    public PaymentsController(ThanhToanRepository payRepo, DatPhongRepository bookingRepo, IConfiguration config, VnPayService vnPay)
    {
        _payRepo = payRepo; _bookingRepo = bookingRepo; _config = config; _vnPay = vnPay;
    }

    public class InitPaymentRequest
    {
        public int idDatPhong { get; set; }
        public decimal soTien { get; set; }
        public string phuongThuc { get; set; } = "VNPAY"; // VNPAY/MOMO/COD...
        public string loaiGiaoDich { get; set; } = "Thanh toán"; // "Thanh toán cọc" | "Thanh toán"
        public bool? isCoc { get; set; } // Tùy chọn: frontend báo rõ là thanh toán cọc
        public string? noiDung { get; set; }
    }

    // DTO đơn giản chỉ cần idDatPhong
    public class InitByBookingRequest { public int idDatPhong { get; set; } }

    // Helper: tạo giao dịch và build URL VNPay (tái sử dụng logic từ Init)
    private async Task<object> CreatePaymentAndUrlAsync(int idDatPhong, decimal amount, string loaiGiaoDich, bool isDeposit)
    {
        if (amount <= 0) throw new ArgumentException("Số tiền phải > 0", nameof(amount));

        // Làm tròn về đơn vị đồng cho nhất quán với VNPAY (nhân 100 phía sau)
        amount = Math.Round(amount, 0, MidpointRounding.AwayFromZero);

        // Sinh mã
        var maGiaoDich = $"PAY_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        var maDonHang = $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{idDatPhong}";

        // Hủy giao dịch chờ cũ và tạo giao dịch mới ở trạng thái chờ thanh toán
        await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
        var noiDung = loaiGiaoDich;
        await _payRepo.CreateAsync(idDatPhong, maGiaoDich, amount, "VNPAY", "Chờ thanh toán", noiDung, maDonHang, loaiGiaoDich);

        // Build ReturnUrl theo host hiện tại hoặc config
        var requestHost = Request.Host.ToString();
        var scheme = Request.Scheme;
        var baseUrl = $"{scheme}://{requestHost}";
        var configuredReturn = _config["VNPAY_RETURN_URL"];
        string returnUrl;
        if (!string.IsNullOrWhiteSpace(configuredReturn))
        {
            returnUrl = configuredReturn.Contains("{host}", StringComparison.OrdinalIgnoreCase)
                ? configuredReturn.Replace("{host}", requestHost)
                : (Uri.IsWellFormedUriString(configuredReturn, UriKind.Absolute) ? configuredReturn : $"{baseUrl}/api/payments/vnpay-return");
        }
        else
        {
            returnUrl = $"{baseUrl}/api/payments/vnpay-return";
        }

        var tmnCode = _config["VNPAY_TMN_CODE"] ?? string.Empty;
        var ipAddr = VnPayService.GetClientIp(HttpContext);
        var amount100 = (long)(amount * 100);
        var createDate = DateTime.Now.ToString("yyyyMMddHHmmss");
        var expireDate = DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss");

        var dict = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = tmnCode,
            ["vnp_Amount"] = amount100.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["vnp_CreateDate"] = createDate,
            ["vnp_ExpireDate"] = expireDate,
            ["vnp_CurrCode"] = "VND",
            ["vnp_IpAddr"] = ipAddr,
            ["vnp_Locale"] = "vn",
            ["vnp_OrderInfo"] = isDeposit ? $"Thanh toan coc don {maDonHang}" : $"Thanh toan don {maDonHang}",
            ["vnp_OrderType"] = "other",
            ["vnp_ReturnUrl"] = returnUrl!,
            ["vnp_TxnRef"] = maGiaoDich
        };

        var redirectUrl = _vnPay.BuildPaymentUrl(dict);
        return new { maGiaoDich, maDonHang, redirectUrl, amount, loaiGiaoDich };
    }

    // 1) Thanh toán FULL 100%: tính số tiền còn lại để đủ 100%
    [Authorize]
    [HttpPost("init-full")]
    public async Task<IActionResult> InitFull([FromBody] InitByBookingRequest body)
    {
        if (body == null || body.idDatPhong <= 0) return BadRequest(new { success=false, message="Thiếu idDatPhong" });
        var booking = await _bookingRepo.GetByIdAsync(body.idDatPhong);
        if (booking == null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });
        decimal tong = 0m; try { tong = (decimal)(booking?.TongTienTamTinh ?? 0m); } catch { }
        var paid = await _payRepo.GetTongDaThanhToanAsync(body.idDatPhong);
        var amount = Math.Max(0, tong - paid);
        if (amount <= 0) return BadRequest(new { success=false, message="Không còn số tiền cần thanh toán (đã đủ 100%)" });
        var result = await CreatePaymentAndUrlAsync(body.idDatPhong, amount, "Thanh toán", isDeposit:false);
        return Ok(new { success=true, data = result });
    }

    // 2) Thanh toán CỌC 30%: tính số tiền để đạt mức 30% tổng (mặc định cấu hình 0.3 nếu khác vẫn lấy 0.3)
    [Authorize]
    [HttpPost("init-deposit-30")]
    public async Task<IActionResult> InitDeposit30([FromBody] InitByBookingRequest body)
    {
        if (body == null || body.idDatPhong <= 0) return BadRequest(new { success=false, message="Thiếu idDatPhong" });
        var booking = await _bookingRepo.GetByIdAsync(body.idDatPhong);
        if (booking == null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });
        decimal tong = 0m; try { tong = (decimal)(booking?.TongTienTamTinh ?? 0m); } catch { }
        var paid = await _payRepo.GetTongDaThanhToanAsync(body.idDatPhong);
        var depositRate = _config.GetValue<decimal>("PAYMENT:DepositRate", 0.3m);
        var desired = Math.Round(tong * depositRate, 0, MidpointRounding.AwayFromZero);
        var amount = Math.Max(0, desired - paid);
        if (amount <= 0) return BadRequest(new { success=false, message="Đã đạt/vượt mức cọc 30%, không cần thanh toán thêm" });
        var result = await CreatePaymentAndUrlAsync(body.idDatPhong, amount, "Thanh toán cọc", isDeposit:true);
        return Ok(new { success=true, data = result });
    }

    // 3) Thanh toán BỔ SUNG 70%: tính số tiền để đạt mức 70% tổng
    [Authorize]
    [HttpPost("init-supplement-70")]
    public async Task<IActionResult> InitSupplement70([FromBody] InitByBookingRequest body)
    {
        if (body == null || body.idDatPhong <= 0) return BadRequest(new { success=false, message="Thiếu idDatPhong" });
        var booking = await _bookingRepo.GetByIdAsync(body.idDatPhong);
        if (booking == null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });
        decimal tong = 0m; try { tong = (decimal)(booking?.TongTienTamTinh ?? 0m); } catch { }
        var paid = await _payRepo.GetTongDaThanhToanAsync(body.idDatPhong);
        // Supplement 70% = thanh toán phần còn lại để đạt 100%, nhưng yêu cầu đã cọc tối thiểu 30% trước
        var depositRate = _config.GetValue<decimal>("PAYMENT:DepositRate", 0.3m);
        var requiredDeposit = Math.Round(tong * depositRate, 0, MidpointRounding.AwayFromZero);
        if (paid < requiredDeposit)
            return BadRequest(new { success=false, message=$"Vui lòng thanh toán cọc {depositRate:P0} trước khi thanh toán phần còn lại" });

        var amount = Math.Max(0, tong - paid);
        if (amount <= 0) return BadRequest(new { success=false, message="Đơn đã được thanh toán đủ 100%" });
        var result = await CreatePaymentAndUrlAsync(body.idDatPhong, amount, "Thanh toán bổ sung", isDeposit:false);
        return Ok(new { success=true, data = result });
    }

    // Khởi tạo giao dịch thanh toán cho 1 đơn đặt phòng
    [Authorize]
    [HttpPost("init")]
    public async Task<IActionResult> Init([FromBody] InitPaymentRequest body)
    {
        if (body.idDatPhong <= 0)
            return BadRequest(new { success=false, message="Thiếu idDatPhong" });

        // Xác định có phải thanh toán cọc hay không (chịu lỗi chính tả, không dấu, hoặc từ khóa 'deposit')
        bool yeuCauCoc = body.isCoc == true;
        if (!yeuCauCoc && !string.IsNullOrWhiteSpace(body.loaiGiaoDich))
        {
            var s = body.loaiGiaoDich.Trim().ToLowerInvariant();
            // bỏ dấu đơn giản
            s = s
                .Replace("cọc", "coc")
                .Replace("thanh toán", "thanhtoan")
                .Replace("thanh toan", "thanhtoan");
            if (s.Contains("coc") || s.Contains("deposit")) yeuCauCoc = true;
        }

        // Luôn clamp số tiền theo loại giao dịch:
        // - Nếu là cọc -> bắt buộc dùng tỷ lệ cọc (bỏ qua soTien client gửi)
        // - Nếu không phải cọc -> nếu soTien<=0 thì lấy toàn phần
        var booking = await _bookingRepo.GetByIdAsync(body.idDatPhong);
        if (booking == null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });
        decimal tong = (decimal)(booking?.TongTienTamTinh ?? 0m);
        var depositRate = _config.GetValue<decimal>("PAYMENT:DepositRate", 0.3m);
        if (yeuCauCoc)
        {
            body.soTien = Math.Round(tong * depositRate, 0, MidpointRounding.AwayFromZero);
            if (string.IsNullOrWhiteSpace(body.noiDung)) body.noiDung = "Thanh toán cọc";
            body.loaiGiaoDich = "Thanh toán cọc";
        }
        else if (body.soTien <= 0)
        {
            body.soTien = tong;
            if (string.IsNullOrWhiteSpace(body.noiDung)) body.noiDung = "Thanh toán";
            body.loaiGiaoDich = "Thanh toán";
        }

        if (body.soTien <= 0)
            return BadRequest(new { success=false, message="soTien không hợp lệ" });

        // Sinh mã giao dịch/đơn hàng sử dụng làm vnp_TxnRef
        var maGiaoDich = $"PAY_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        var maDonHang = $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{body.idDatPhong}";

    // Hủy các giao dịch chờ cũ để tránh trùng
    await _payRepo.CancelAllPendingForBookingAsync(body.idDatPhong);
    // Tạo record ThanhToan ở trạng thái "Chờ thanh toán"
    await _payRepo.CreateAsync(body.idDatPhong, maGiaoDich, body.soTien, "VNPAY", "Chờ thanh toán", body.noiDung ?? body.loaiGiaoDich, maDonHang, body.loaiGiaoDich);

        // Tạo URL thanh toán VNPAY
        var tmnCode = _config["VNPAY_TMN_CODE"] ?? string.Empty;
        // Xây dựng ReturnUrl linh hoạt để chạy cả web (localhost) và emulator (10.0.2.2)
        var requestHost = Request.Host.ToString();
        var scheme = Request.Scheme;
        var baseUrl = $"{scheme}://{requestHost}";
        var configuredReturn = _config["VNPAY_RETURN_URL"];
        string returnUrl;
        if (!string.IsNullOrWhiteSpace(configuredReturn))
        {
            // Hỗ trợ mẫu {host} để tự thay bằng host hiện tại
            if (configuredReturn.Contains("{host}", StringComparison.OrdinalIgnoreCase))
            {
                returnUrl = configuredReturn.Replace("{host}", requestHost);
            }
            else
            {
                // Nếu config đã là URL tuyệt đối thì dùng luôn, ngược lại fallback về baseUrl
                returnUrl = Uri.IsWellFormedUriString(configuredReturn, UriKind.Absolute)
                    ? configuredReturn
                    : $"{baseUrl}/api/payments/vnpay-return";
            }
        }
        else
        {
            returnUrl = $"{baseUrl}/api/payments/vnpay-return";
        }
    var ipAddr = VnPayService.GetClientIp(HttpContext);
    var amount100 = (long)(body.soTien * 100); // VNPAY yêu cầu nhân 100
    var createDate = DateTime.Now.ToString("yyyyMMddHHmmss");
    var expireDate = DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss");

        var dict = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = tmnCode,
            ["vnp_Amount"] = amount100.ToString(CultureInfo.InvariantCulture),
            ["vnp_CreateDate"] = createDate,
            ["vnp_ExpireDate"] = expireDate,
            ["vnp_CurrCode"] = "VND",
            ["vnp_IpAddr"] = ipAddr,
            ["vnp_Locale"] = "vn",
            ["vnp_OrderInfo"] = yeuCauCoc ? $"Thanh toan coc don {maDonHang}" : $"Thanh toan don {maDonHang}",
            ["vnp_OrderType"] = "other",
            ["vnp_ReturnUrl"] = returnUrl!,
            ["vnp_TxnRef"] = maGiaoDich
        };

        var redirectUrl = _vnPay.BuildPaymentUrl(dict);
        return Ok(new { success=true, data = new { maGiaoDich, maDonHang, redirectUrl } });
    }

    // Callback mô phỏng (chỉ dùng nội bộ DEV). Chặn bằng quyền Admin để tránh lẫn với luồng thật.
    [Authorize(Roles="Admin")]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string maGiaoDich, [FromQuery] string status = "success", [FromQuery] decimal amount = 0, [FromQuery] string? signature = null)
    {
        if (string.IsNullOrWhiteSpace(maGiaoDich))
            return BadRequest(new { success=false, message="Thiếu maGiaoDich" });

        // TODO: xác thực chữ ký khi kết nối cổng thực (signature)

        var payment = await _payRepo.GetByMaGiaoDichAsync(maGiaoDich);
        if (payment == null)
            return NotFound(new { success=false, message="Không tìm thấy giao dịch" });

        var idDatPhong = (int)payment.IdDatPhong;
        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thành công", null);

            // Tính tổng đã thanh toán để cập nhật trạng thái đơn
            var tongDaTra = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
            var booking = await _bookingRepo.GetByIdAsync(idDatPhong);
            decimal tongTien = (decimal)(booking?.TongTienTamTinh ?? 0m);

            // Nếu đã trả đủ, đánh dấu "Đã thanh toán đầy đủ"; nếu chưa, đánh dấu "Đã cọc"
            if (tongDaTra >= tongTien && tongTien > 0)
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaThanhToanDayDu");
            else
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaCoc");

            return Ok(new { success=true, message="Thanh toán thành công", data = new { idDatPhong, tongDaTra } });
        }
        else
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thất bại", null);
            return Ok(new { success=false, message="Thanh toán thất bại", data = new { idDatPhong } });
        }
    }

    // VNPAY return (thật)
    [HttpGet("vnpay-return")]
    public async Task<IActionResult> VnPayReturn()
    {
        // Lấy toàn bộ query
        var q = HttpContext.Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        Console.WriteLine($"[VNPAY-RETURN] Host={Request.Host}, QueryKeys={string.Join(',', q.Keys)}");
        if (!q.TryGetValue("vnp_TxnRef", out var maGiaoDich))
            return BadRequest(new { success=false, message="Thiếu vnp_TxnRef" });

        var isValid = _vnPay.ValidateReturn(q);
        var responseCode = q.TryGetValue("vnp_ResponseCode", out var rc) ? rc : "";
        var amountStr = q.TryGetValue("vnp_Amount", out var a) ? a : "0";
        var amount100 = long.TryParse(amountStr, out var l) ? l : 0;
        var amount = (decimal)amount100 / 100m;

        var payment = await _payRepo.GetByMaGiaoDichAsync(maGiaoDich);
        if (payment == null)
            return NotFound(new { success=false, message="Không tìm thấy giao dịch" });

        var idDatPhong = (int)payment.IdDatPhong;
        if (isValid && string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase))
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thành công", System.Text.Json.JsonSerializer.Serialize(q));
            Console.WriteLine($"[VNPAY-RETURN] SUCCESS TxnRef={maGiaoDich}");
            var tongDaTra = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
            var booking = await _bookingRepo.GetByIdAsync(idDatPhong);
            decimal tongTien = (decimal)(booking?.TongTienTamTinh ?? 0m);
            if (tongDaTra >= tongTien && tongTien > 0)
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaThanhToanDayDu");
            else
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaCoc");
            await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
            return Ok(new { success=true, message="Thanh toán thành công", data = new { idDatPhong, tongDaTra } });
        }
        else
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thất bại", System.Text.Json.JsonSerializer.Serialize(q));
            Console.WriteLine($"[VNPAY-RETURN] FAIL TxnRef={maGiaoDich}, Code={responseCode}, Valid={isValid}");
            return Ok(new { success=false, message=$"Thanh toán thất bại (responseCode={responseCode}, valid={isValid})", data = new { idDatPhong } });
        }
    }

    // VNPAY confirm (app forward JSON) — fallback khi ReturnUrl không vào được API
    // Cho phép anonymous vì payload đã được xác thực bằng chữ ký VNPAY (vnp_SecureHash)
    [AllowAnonymous]
    [HttpPost("vnpay-confirm")]
    public async Task<IActionResult> VnPayConfirm([FromBody] Dictionary<string, string> payload)
    {
        if (payload == null || payload.Count == 0)
            return BadRequest(new { success=false, message="Thiếu payload" });

        if (!payload.TryGetValue("vnp_TxnRef", out var maGiaoDich))
            return BadRequest(new { success=false, message="Thiếu vnp_TxnRef" });

        var isValid = _vnPay.ValidateReturn(payload);
        var responseCode = payload.TryGetValue("vnp_ResponseCode", out var rc) ? rc : "";
        Console.WriteLine($"[VNPAY-CONFIRM] Host={Request.Host}, TxnRef={maGiaoDich}, Code={responseCode}, Valid={isValid}");

        var payment = await _payRepo.GetByMaGiaoDichAsync(maGiaoDich);
        if (payment == null)
            return NotFound(new { success=false, message="Không tìm thấy giao dịch" });

        int idDatPhong = (int)payment.IdDatPhong;
        if (isValid && string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase))
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thành công", System.Text.Json.JsonSerializer.Serialize(payload));
            var tongDaTra = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
            var booking = await _bookingRepo.GetByIdAsync(idDatPhong);
            decimal tongTien = (decimal)(booking?.TongTienTamTinh ?? 0m);
            if (tongDaTra >= tongTien && tongTien > 0)
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaThanhToanDayDu");
            else
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaCoc");
            await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
            return Ok(new { success=true, message="Xác nhận thanh toán thành công", data = new { idDatPhong, tongDaTra } });
        }
        else
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thất bại", System.Text.Json.JsonSerializer.Serialize(payload));
            return Ok(new { success=false, message=$"Xác nhận thất bại (responseCode={responseCode}, valid={isValid})", data = new { idDatPhong } });
        }
    }

    // VNPAY confirm (GET) — biến thể nhận trực tiếp query string để tiện cho WebView/ứng dụng di động
    // Ví dụ: app bắt được URL cuối cùng của VNPAY, lấy toàn bộ query và gọi GET đến endpoint này
    [AllowAnonymous]
    [HttpGet("vnpay-confirm")]
    public async Task<IActionResult> VnPayConfirmGet()
    {
        var payload = HttpContext.Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        if (!payload.Any())
            return BadRequest(new { success=false, message="Thiếu query" });

        if (!payload.TryGetValue("vnp_TxnRef", out var maGiaoDich))
            return BadRequest(new { success=false, message="Thiếu vnp_TxnRef" });

        var isValid = _vnPay.ValidateReturn(payload);
        var responseCode = payload.TryGetValue("vnp_ResponseCode", out var rc) ? rc : "";
        Console.WriteLine($"[VNPAY-CONFIRM-GET] Host={Request.Host}, TxnRef={maGiaoDich}, Code={responseCode}, Valid={isValid}");

        var payment = await _payRepo.GetByMaGiaoDichAsync(maGiaoDich);
        if (payment == null)
            return NotFound(new { success=false, message="Không tìm thấy giao dịch" });

        int idDatPhong = (int)payment.IdDatPhong;
        if (isValid && string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase))
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thành công", System.Text.Json.JsonSerializer.Serialize(payload));
            var tongDaTra = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
            var booking = await _bookingRepo.GetByIdAsync(idDatPhong);
            decimal tongTien = (decimal)(booking?.TongTienTamTinh ?? 0m);
            if (tongDaTra >= tongTien && tongTien > 0)
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaThanhToanDayDu");
            else
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaCoc");
            await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
            return Ok(new { success=true, message="Xác nhận thanh toán thành công (GET)", data = new { idDatPhong, tongDaTra } });
        }
        else
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thất bại", System.Text.Json.JsonSerializer.Serialize(payload));
            return Ok(new { success=false, message=$"Xác nhận thất bại (responseCode={responseCode}, valid={isValid})", data = new { idDatPhong } });
        }
    }

    public class ConfirmFromUrlRequest { public string? url { get; set; } }

    // VNPAY confirm (POST) — nhận 1 chuỗi URL hoàn chỉnh, server tự parse query vnp_*
    [AllowAnonymous]
    [HttpPost("vnpay-confirm-from-url")]
    public async Task<IActionResult> VnPayConfirmFromUrl([FromBody] ConfirmFromUrlRequest body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.url))
            return BadRequest(new { success=false, message="Thiếu url" });

        // Hỗ trợ cả trường hợp chỉ gửi phần query (bắt đầu bằng "vnp_")
        string fullUrl = body.url!.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || body.url!.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? body.url!
            : (body.url!.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase) || body.url!.StartsWith("?vnp_", StringComparison.OrdinalIgnoreCase)
                ? ($"http://dummy.local/{(body.url!.StartsWith("?", StringComparison.Ordinal) ? body.url!.Substring(1) : body.url!)}")
                : body.url!);

        Uri? uri;
        if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out uri) || uri == null)
            return BadRequest(new { success=false, message="url không hợp lệ" });

        var parsed = QueryHelpers.ParseQuery(uri.Query);
        var payload = parsed.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        if (!payload.TryGetValue("vnp_TxnRef", out var maGiaoDich))
            return BadRequest(new { success=false, message="Thiếu vnp_TxnRef" });

        var isValid = _vnPay.ValidateReturn(payload);
        var responseCode = payload.TryGetValue("vnp_ResponseCode", out var rc) ? rc : "";
        Console.WriteLine($"[VNPAY-CONFIRM-URL] Host={Request.Host}, TxnRef={maGiaoDich}, Code={responseCode}, Valid={isValid}");

        var payment = await _payRepo.GetByMaGiaoDichAsync(maGiaoDich);
        if (payment == null)
            return NotFound(new { success=false, message="Không tìm thấy giao dịch" });

        int idDatPhong = (int)payment.IdDatPhong;
        if (isValid && string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase))
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thành công", System.Text.Json.JsonSerializer.Serialize(payload));
            var tongDaTra = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
            var booking = await _bookingRepo.GetByIdAsync(idDatPhong);
            decimal tongTien = (decimal)(booking?.TongTienTamTinh ?? 0m);
            if (tongDaTra >= tongTien && tongTien > 0)
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaThanhToanDayDu");
            else
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaCoc");
            await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
            return Ok(new { success=true, message="Xác nhận thanh toán thành công (URL)", data = new { idDatPhong, tongDaTra } });
        }
        else
        {
            await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thất bại", System.Text.Json.JsonSerializer.Serialize(payload));
            return Ok(new { success=false, message=$"Xác nhận thất bại (responseCode={responseCode}, valid={isValid})", data = new { idDatPhong } });
        }
    }

    // VNPAY IPN (server-to-server) — dùng khi cấu hình VNPay gọi về máy chủ công khai (yêu cầu URL public)
    // Lưu ý: VNPay không thể gọi vào 10.0.2.2 hoặc localhost. Dùng khi bạn expose API qua domain công khai/ngrok.
    [AllowAnonymous]
    [HttpGet("vnpay-ipn")]
    public async Task<IActionResult> VnPayIpn()
    {
        var q = HttpContext.Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        if (!q.TryGetValue("vnp_SecureHash", out var secure)) return Ok(new { RspCode = "97", Message = "Thiếu chữ ký" });
        // Xác thực chữ ký
        var isValid = _vnPay.ValidateReturn(q);
        if (!isValid) return Ok(new { RspCode = "97", Message = "Invalid checksum" });

        if (!q.TryGetValue("vnp_TxnRef", out var maGiaoDich)) return Ok(new { RspCode = "01", Message = "Thiếu mã đơn hàng" });
        var payment = await _payRepo.GetByMaGiaoDichAsync(maGiaoDich);
        if (payment == null) return Ok(new { RspCode = "01", Message = "Không tìm thấy đơn hàng" });

        // Kiểm tra số tiền (VNPay gửi nhân 100)
        var amountStr = q.TryGetValue("vnp_Amount", out var a) ? a : "0";
        var amount100 = long.TryParse(amountStr, out var l) ? l : 0;
        var amount = (decimal)amount100 / 100m;
        if (amount != (decimal)payment.SoTien) return Ok(new { RspCode = "04", Message = "Số tiền không hợp lệ" });

        var responseCode = q.TryGetValue("vnp_ResponseCode", out var rc) ? rc : "";
        var status = q.TryGetValue("vnp_TransactionStatus", out var st) ? st : "";
        var success = string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "00", StringComparison.OrdinalIgnoreCase);

        await _payRepo.UpdateTrangThaiAsync(maGiaoDich, success ? "Thành công" : "Thất bại", System.Text.Json.JsonSerializer.Serialize(q));
        if (success)
        {
            int idDatPhong = (int)payment.IdDatPhong;
            var tongDaTra = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
            var booking = await _bookingRepo.GetByIdAsync(idDatPhong);
            decimal tongTien = (decimal)(booking?.TongTienTamTinh ?? 0m);
            if (tongDaTra >= tongTien && tongTien > 0)
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaThanhToanDayDu");
            else
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaCoc");
            await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
        }

        return Ok(new { RspCode = "00", Message = "Confirm Success" });
    }

    // Endpoint chẩn đoán: cho biết returnUrl sẽ là gì đối với request hiện tại
    [HttpGet("return-url-preview")]
    public IActionResult ReturnUrlPreview()
    {
        var requestHost = Request.Host.ToString();
        var scheme = Request.Scheme;
        var baseUrl = $"{scheme}://{requestHost}";
        var configuredReturn = _config["VNPAY_RETURN_URL"];
        string resolved;
        if (!string.IsNullOrWhiteSpace(configuredReturn))
        {
            resolved = configuredReturn.Contains("{host}", StringComparison.OrdinalIgnoreCase)
                ? configuredReturn.Replace("{host}", requestHost)
                : (Uri.IsWellFormedUriString(configuredReturn, UriKind.Absolute) ? configuredReturn : $"{baseUrl}/api/payments/vnpay-return");
        }
        else
        {
            resolved = $"{baseUrl}/api/payments/vnpay-return";
        }
        return Ok(new { success=true, data = new { host=requestHost, baseUrl, resolvedReturnUrl = resolved } });
    }

    // Danh sách giao dịch của 1 đơn
    [Authorize]
    [HttpGet("booking/{idDatPhong:int}")]
    public async Task<IActionResult> ListByBooking([FromRoute] int idDatPhong)
    {
        var items = await _payRepo.ListByBookingAsync(idDatPhong);
        return Ok(new { success=true, data = items });
    }

    // Trả config thanh toán cho frontend (tỷ lệ cọc)
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var depositRate = _config.GetValue<decimal>("PAYMENT:DepositRate", 0.3m);
        return Ok(new { success=true, data = new { depositRate } });
    }

    public class RefundRequest
    {
        public decimal soTien { get; set; }
        public string noiDung { get; set; } = "Hoàn tiền";
        public string maDonHang { get; set; } = string.Empty;
    }

    // Tạo yêu cầu hoàn tiền cho 1 giao dịch (Admin)
    [Authorize(Roles="Admin")]
    [HttpPost("{maGiaoDich}/refund")]
    public async Task<IActionResult> Refund([FromRoute] string maGiaoDich, [FromBody] RefundRequest body)
    {
        var payment = await _payRepo.GetByMaGiaoDichAsync(maGiaoDich);
        if (payment == null) return NotFound(new { success=false, message="Không tìm thấy giao dịch" });
        int idDatPhong = (int)payment.IdDatPhong;

        var newRefundId = $"REF_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        var created = await _payRepo.CreateRefundAsync(idDatPhong, newRefundId, body.soTien, body.noiDung, body.maDonHang);
        return Ok(new { success=true, message="Đã tạo yêu cầu hoàn tiền", data = created });
    }
}

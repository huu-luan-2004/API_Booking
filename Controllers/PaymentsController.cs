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
    
    // DTO cho payment data từ dynamic object
    private class PaymentData
    {
        public string LoaiGiaoDich { get; set; } = "";
        public decimal SoTien { get; set; }
        public DateTime? NgayThanhToan { get; set; }
        public int? IdCoSoLuuTru { get; set; }
        public string TenCoSo { get; set; } = "";
    }

    // DTO đơn giản chỉ cần idDatPhong
    public class InitByBookingRequest { public int idDatPhong { get; set; } }
    // DTO tạo thanh toán trực tiếp từ thông tin phòng (không tạo DatPhong trước)
    public class InitDirectRequest 
    { 
        public int idPhong { get; set; }
        public DateTime ngayNhanPhong { get; set; }
        public DateTime ngayTraPhong { get; set; }
        public decimal? tongTien { get; set; }
        public string? ghiChu { get; set; }
        public string? holdToken { get; set; }
        public bool isDeposit { get; set; } = false; // (bị vô hiệu) không hỗ trợ cọc trong luồng direct
    }

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

    // Khởi tạo thanh toán trực tiếp từ thông tin phòng/ngày, chỉ tạo đơn sau khi thanh toán thành công
    [Authorize]
    [HttpPost("init-direct")]
    public async Task<IActionResult> InitDirect([FromBody] InitDirectRequest body)
    {
        if (!int.TryParse(User?.FindFirst("id")?.Value, out var idNguoiDung))
            return Unauthorized(new { success=false, message="Vui lòng đăng nhập" });
        if (body == null || body.idPhong <= 0 || body.ngayNhanPhong == default || body.ngayTraPhong == default || body.ngayNhanPhong >= body.ngayTraPhong)
            return BadRequest(new { success=false, message="Thiếu hoặc sai thông tin (idPhong, ngayNhanPhong, ngayTraPhong)" });
        if (body.isDeposit)
            return BadRequest(new { success=false, message="API cọc đã bị vô hiệu. Vui lòng thanh toán toàn phần." });
        bool useHold = !string.IsNullOrWhiteSpace(body.holdToken);
        string? holdToken = body.holdToken;
        if (useHold)
        {
            var holdRepo = HttpContext.RequestServices.GetService(typeof(HotelBookingApi.Data.PreBookingHoldRepository)) as HotelBookingApi.Data.PreBookingHoldRepository;
            var hold = await holdRepo!.GetByTokenAsync(holdToken!);
            if (hold == null) return BadRequest(new { success=false, message="HoldToken không hợp lệ" });
            DateTime exp = DateTime.MinValue; try { exp = Convert.ToDateTime(((IDictionary<string, object>)hold)["ExpiresAt"]); } catch { }
            if (exp <= DateTime.UtcNow) return BadRequest(new { success=false, message="Hold đã hết hạn" });
            // Xác thực hold khớp phòng và khoảng thời gian
            int holdPhong = 0; DateTime holdNhan = body.ngayNhanPhong, holdTra = body.ngayTraPhong;
            try
            {
                var d = (IDictionary<string, object>)hold;
                holdPhong = Convert.ToInt32(d["IdPhong"]);
                holdNhan = Convert.ToDateTime(d["NgayNhanPhong"]);
                holdTra = Convert.ToDateTime(d["NgayTraPhong"]);
            }
            catch { }
            if (holdPhong != body.idPhong || holdNhan != body.ngayNhanPhong || holdTra != body.ngayTraPhong)
                return BadRequest(new { success=false, message="Hold không khớp thông tin đặt phòng" });
        }
        else
        {
            // Fallback: cho phép luồng DIRECT (không cần hold), vẫn kiểm tra khả dụng
            var available = await _bookingRepo.CheckAvailabilityAsync(body.idPhong, body.ngayNhanPhong, body.ngayTraPhong);
            if (!available) return BadRequest(new { success=false, message="Phòng không khả dụng trong khoảng thời gian này" });
        }

        // Tính tổng tiền (giá trị toàn phần của booking) nếu FE không gửi
        decimal tongTien = body.tongTien ?? 0m;
        if (tongTien <= 0m)
        {
            try
            {
                var roomRepo = HttpContext.RequestServices.GetService(typeof(HotelBookingApi.Data.PhongRepository)) as HotelBookingApi.Data.PhongRepository;
                var room = await roomRepo!.GetByIdAsync(body.idPhong);
                decimal gia = 0m; try { gia = (decimal)(room?.Gia ?? 0m); } catch { }
                var nights = (body.ngayTraPhong.Date - body.ngayNhanPhong.Date).Days; if (nights <= 0) nights = 1;
                var calc = gia * nights; if (calc > 0) tongTien = calc;
            }
            catch { }
        }
        if (tongTien <= 0m) return BadRequest(new { success=false, message="Không xác định được tổng tiền" });

        // Luồng direct chỉ hỗ trợ thanh toán toàn phần
        var soTienThanhToan = Math.Round(tongTien, 0, MidpointRounding.AwayFromZero);

        // Sinh mã giao dịch nhưng KHÔNG lưu vào DB (chỉ lưu khi thành công)
        var maGiaoDich = $"PAY_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        var maDonHang = $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_R{body.idPhong}";
        var noiDung = string.IsNullOrWhiteSpace(body.ghiChu) ? "Thanh toán" : body.ghiChu!;

        // Build return URL như logic hiện có
        var requestHost = Request.Host.ToString();
        var scheme = Request.Scheme;
        var baseUrl = $"{scheme}://{requestHost}";
        var configuredReturn = _config["VNPAY_RETURN_URL"];
        string returnUrl = !string.IsNullOrWhiteSpace(configuredReturn)
            ? (configuredReturn.Contains("{host}", StringComparison.OrdinalIgnoreCase) ? configuredReturn.Replace("{host}", requestHost) : (Uri.IsWellFormedUriString(configuredReturn, UriKind.Absolute) ? configuredReturn : $"{baseUrl}/api/payments/vnpay-return"))
            : $"{baseUrl}/api/payments/vnpay-return";

        var tmnCode = _config["VNPAY_TMN_CODE"] ?? string.Empty;
        var ipAddr = VnPayService.GetClientIp(HttpContext);
        var amount100 = (long)(soTienThanhToan * 100);
        var createDate = DateTime.Now.ToString("yyyyMMddHHmmss");
        var expireDate = DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss");
        // Gói thông tin cần thiết vào vnp_OrderInfo để tạo đơn sau khi thành công (chỉ DIRECT/HOLD)
        // Kèm tổng tiền booking để server set TongTienTamTinh chính xác
        var orderInfo = useHold
            ? $"HOLD|{holdToken}|{body.idPhong}|{body.ngayNhanPhong:yyyyMMdd}|{body.ngayTraPhong:yyyyMMdd}|{idNguoiDung}|{tongTien}"
            : $"DIRECT|{body.idPhong}|{body.ngayNhanPhong:yyyyMMdd}|{body.ngayTraPhong:yyyyMMdd}|{idNguoiDung}|{tongTien}";
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
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_OrderType"] = "other",
            ["vnp_ReturnUrl"] = returnUrl!,
            ["vnp_TxnRef"] = maGiaoDich
        };

        var redirectUrl = _vnPay.BuildPaymentUrl(dict);
        return Ok(new { success=true, data = new { maGiaoDich, maDonHang, redirectUrl, total = tongTien, amount = soTienThanhToan } });
    }

    // 1) Thanh toán FULL 100%: tính số tiền còn lại để đủ 100%
    [Authorize]
    [HttpPost("init-full")]
    public async Task<IActionResult> InitFull([FromBody] InitByBookingRequest body)
    {
        // Trước khi khởi tạo thanh toán, dọn rác các đơn giữ chỗ đã quá hạn
        try { await _bookingRepo.PurgeExpiredUnpaidAsync(15); } catch { }
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

    // (Đã xoá) Các API cọc/bổ sung đã bị loại bỏ — chỉ hỗ trợ thanh toán toàn phần

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
            s = s.Replace("cọc", "coc").Replace("thanh toán", "thanhtoan").Replace("thanh toan", "thanhtoan");
            if (s.Contains("coc") || s.Contains("deposit")) yeuCauCoc = true;
        }
        if (yeuCauCoc)
            return BadRequest(new { success=false, message="API thanh toán cọc đã bị vô hiệu hoá. Vui lòng thanh toán toàn phần." });

        // Luôn clamp số tiền theo loại giao dịch:
        // - Nếu là cọc -> bắt buộc dùng tỷ lệ cọc (bỏ qua soTien client gửi)
        // - Nếu không phải cọc -> nếu soTien<=0 thì lấy toàn phần
        var booking = await _bookingRepo.GetByIdAsync(body.idDatPhong);
        if (booking == null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });
        decimal tong = (decimal)(booking?.TongTienTamTinh ?? 0m);
        if (body.soTien <= 0)
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
        int idDatPhong = 0; try { idDatPhong = payment != null ? (int)(payment.IdDatPhong ?? 0) : 0; } catch { }
        if (isValid && string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[VNPAY-RETURN] SUCCESS TxnRef={maGiaoDich}");
            // Nếu không có record ThanhToan (luồng init-direct), tạo đơn và chèn thanh toán thành công từ vnp_OrderInfo
            if (payment == null || idDatPhong <= 0)
            {
                var info = q.TryGetValue("vnp_OrderInfo", out var oi) ? oi : string.Empty;
                // HOLD|{token}|{idPhong}|{yyyyMMddNgayNhan}|{yyyyMMddNgayTra}|{idNguoiDung}
                // DIRECT|{idPhong}|{yyyyMMddNgayNhan}|{yyyyMMddNgayTra}|{idNguoiDung}
                var parts = info.Split('|');
                if (parts.Length >= 6 && parts[0] == "DIRECT")
                {
                    int idPhong = int.TryParse(parts[1], out var p) ? p : 0;
                    DateTime ngayNhan = DateTime.ParseExact(parts[2], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    DateTime ngayTra = DateTime.ParseExact(parts[3], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    int idNguoiDung = int.TryParse(parts[4], out var u) ? u : 0;
                    decimal total = 0m; if (parts.Length >= 6) decimal.TryParse(parts[5], out total);
                    idDatPhong = await _bookingRepo.CreateAsync(idNguoiDung, idPhong, ngayNhan, ngayTra, total > 0 ? total : amount);
                    await _payRepo.CreateAsync(idDatPhong, maGiaoDich, amount, "VNPAY", "Thành công", "Thanh toán", $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{idDatPhong}", "Thanh toán");
                }
                else if (parts.Length >= 7 && parts[0] == "HOLD")
                {
                    var holdToken = parts[1];
                    int idPhong = int.TryParse(parts[2], out var p) ? p : 0;
                    DateTime ngayNhan = DateTime.ParseExact(parts[3], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    DateTime ngayTra = DateTime.ParseExact(parts[4], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    int idNguoiDung = int.TryParse(parts[5], out var u) ? u : 0;
                    decimal total = 0m; if (parts.Length >= 7) decimal.TryParse(parts[6], out total);
                    idDatPhong = await _bookingRepo.CreateAsync(idNguoiDung, idPhong, ngayNhan, ngayTra, total > 0 ? total : amount);
                    await _payRepo.CreateAsync(idDatPhong, maGiaoDich, amount, "VNPAY", "Thành công", "Thanh toán", $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{idDatPhong}", "Thanh toán");
                    var holdRepo = HttpContext.RequestServices.GetService(typeof(HotelBookingApi.Data.PreBookingHoldRepository)) as HotelBookingApi.Data.PreBookingHoldRepository;
                    try { await holdRepo!.ReleaseAsync(holdToken); } catch { }
                }
            }

            var tongDaTra = idDatPhong > 0 ? await _payRepo.GetTongDaThanhToanAsync(idDatPhong) : amount;
            var booking = idDatPhong > 0 ? await _bookingRepo.GetByIdAsync(idDatPhong) : null;
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
            // Nếu là luồng init-direct (không có payment record), chỉ mở khóa phòng (nếu HOLD) và KHÔNG lưu vào DB
            var info = q.TryGetValue("vnp_OrderInfo", out var oi) ? oi : string.Empty;
            var parts = info.Split('|');
            if (parts.Length >= 2 && parts[0] == "HOLD")
            {
                var holdToken = parts[1];
                var holdRepo = HttpContext.RequestServices.GetService(typeof(HotelBookingApi.Data.PreBookingHoldRepository)) as HotelBookingApi.Data.PreBookingHoldRepository;
                try { await holdRepo!.ReleaseAsync(holdToken); } catch { }
            }
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
        int idDatPhong = 0; try { idDatPhong = payment != null ? (int)(payment.IdDatPhong ?? 0) : 0; } catch { }
        if (isValid && string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase))
        {
            if (payment == null || idDatPhong <= 0)
            {
                var info = payload.TryGetValue("vnp_OrderInfo", out var oi) ? oi : string.Empty;
                var parts = info.Split('|');
                if (parts.Length >= 6 && parts[0] == "DIRECT")
                {
                    int idPhong = int.TryParse(parts[1], out var p) ? p : 0;
                    DateTime ngayNhan = DateTime.ParseExact(parts[2], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    DateTime ngayTra = DateTime.ParseExact(parts[3], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    int idNguoiDung = int.TryParse(parts[4], out var u) ? u : 0;
                    var amountStr = payload.TryGetValue("vnp_Amount", out var a) ? a : "0";
                    var amount100 = long.TryParse(amountStr, out var l) ? l : 0;
                    var money = (decimal)amount100 / 100m;
                    decimal total = 0m; if (parts.Length >= 6) decimal.TryParse(parts[5], out total);
                    idDatPhong = await _bookingRepo.CreateAsync(idNguoiDung, idPhong, ngayNhan, ngayTra, total > 0 ? total : money);
                    await _payRepo.CreateAsync(idDatPhong, maGiaoDich, money, "VNPAY", "Thành công", "Thanh toán", $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{idDatPhong}", "Thanh toán");
                }
                else if (parts.Length >= 7 && parts[0] == "HOLD")
                {
                    var holdToken = parts[1];
                    int idPhong = int.TryParse(parts[2], out var p) ? p : 0;
                    DateTime ngayNhan = DateTime.ParseExact(parts[3], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    DateTime ngayTra = DateTime.ParseExact(parts[4], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    int idNguoiDung = int.TryParse(parts[5], out var u) ? u : 0;
                    var amountStr = payload.TryGetValue("vnp_Amount", out var a) ? a : "0";
                    var amount100 = long.TryParse(amountStr, out var l) ? l : 0;
                    var money = (decimal)amount100 / 100m;
                    decimal total = 0m; if (parts.Length >= 7) decimal.TryParse(parts[6], out total);
                    idDatPhong = await _bookingRepo.CreateAsync(idNguoiDung, idPhong, ngayNhan, ngayTra, total > 0 ? total : money);
                    await _payRepo.CreateAsync(idDatPhong, maGiaoDich, money, "VNPAY", "Thành công", "Thanh toán", $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{idDatPhong}", "Thanh toán");
                    var holdRepo = HttpContext.RequestServices.GetService(typeof(HotelBookingApi.Data.PreBookingHoldRepository)) as HotelBookingApi.Data.PreBookingHoldRepository;
                    try { await holdRepo!.ReleaseAsync(holdToken); } catch { }
                }
            }
            var tongDaTra = idDatPhong > 0 ? await _payRepo.GetTongDaThanhToanAsync(idDatPhong) : 0m;
            var booking = idDatPhong > 0 ? await _bookingRepo.GetByIdAsync(idDatPhong) : null;
            decimal tongTien = (decimal)(booking?.TongTienTamTinh ?? 0m);
            if (tongDaTra >= tongTien && tongTien > 0)
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaThanhToanDayDu");
            else
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaCoc");
            if (idDatPhong > 0) await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
            return Ok(new { success=true, message="Xác nhận thanh toán thành công", data = new { idDatPhong, tongDaTra } });
        }
        else
        {
            // Luồng init-direct: không lưu thất bại, chỉ mở khóa phòng nếu HOLD
            var info = payload.TryGetValue("vnp_OrderInfo", out var oi) ? oi : string.Empty;
            var parts = info.Split('|');
            if (parts.Length >= 2 && parts[0] == "HOLD")
            {
                var holdToken = parts[1];
                var holdRepo = HttpContext.RequestServices.GetService(typeof(HotelBookingApi.Data.PreBookingHoldRepository)) as HotelBookingApi.Data.PreBookingHoldRepository;
                try { await holdRepo!.ReleaseAsync(holdToken); } catch { }
            }
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
        int idDatPhong = 0; try { idDatPhong = payment != null ? (int)(payment.IdDatPhong ?? 0) : 0; } catch { }

        if (isValid && string.Equals(responseCode, "00", StringComparison.OrdinalIgnoreCase))
        {
            // Hỗ trợ luồng DIRECT/HOLD khi không có payment record (tạo đơn sau khi thanh toán thành công)
            if (payment == null || idDatPhong <= 0)
            {
                var info = payload.TryGetValue("vnp_OrderInfo", out var oi) ? oi : string.Empty;
                var parts = info.Split('|');
                if (parts.Length >= 6 && parts[0] == "DIRECT")
                {
                    int idPhong = int.TryParse(parts[1], out var p) ? p : 0;
                    DateTime ngayNhan = DateTime.ParseExact(parts[2], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    DateTime ngayTra = DateTime.ParseExact(parts[3], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    int idNguoiDung = int.TryParse(parts[4], out var u) ? u : 0;
                    var amountStr2 = payload.TryGetValue("vnp_Amount", out var a2) ? a2 : "0";
                    var amount100_2 = long.TryParse(amountStr2, out var l2) ? l2 : 0;
                    var money2 = (decimal)amount100_2 / 100m;
                    decimal total = 0m; if (parts.Length >= 6) decimal.TryParse(parts[5], out total);
                    idDatPhong = await _bookingRepo.CreateAsync(idNguoiDung, idPhong, ngayNhan, ngayTra, total > 0 ? total : money2);
                    await _payRepo.CreateAsync(idDatPhong, maGiaoDich, money2, "VNPAY", "Thành công", "Thanh toán", $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{idDatPhong}", "Thanh toán");
                }
                else if (parts.Length >= 7 && parts[0] == "HOLD")
                {
                    var holdToken = parts[1];
                    int idPhong = int.TryParse(parts[2], out var p) ? p : 0;
                    DateTime ngayNhan = DateTime.ParseExact(parts[3], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    DateTime ngayTra = DateTime.ParseExact(parts[4], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    int idNguoiDung = int.TryParse(parts[5], out var u) ? u : 0;
                    var amountStr2 = payload.TryGetValue("vnp_Amount", out var a2) ? a2 : "0";
                    var amount100_2 = long.TryParse(amountStr2, out var l2) ? l2 : 0;
                    var money2 = (decimal)amount100_2 / 100m;
                    decimal total = 0m; if (parts.Length >= 7) decimal.TryParse(parts[6], out total);
                    idDatPhong = await _bookingRepo.CreateAsync(idNguoiDung, idPhong, ngayNhan, ngayTra, total > 0 ? total : money2);
                    await _payRepo.CreateAsync(idDatPhong, maGiaoDich, money2, "VNPAY", "Thành công", "Thanh toán", $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{idDatPhong}", "Thanh toán");
                    var holdRepo = HttpContext.RequestServices.GetService(typeof(HotelBookingApi.Data.PreBookingHoldRepository)) as HotelBookingApi.Data.PreBookingHoldRepository;
                    try { await holdRepo!.ReleaseAsync(holdToken); } catch { }
                }
            }

            // Nếu có sẵn payment record (luồng init cho DatPhong), cập nhật trạng thái thành công
            if (idDatPhong <= 0 && payment != null) { try { idDatPhong = (int)(payment.IdDatPhong ?? 0); } catch { } }
            if (payment != null)
            {
                await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thành công", System.Text.Json.JsonSerializer.Serialize(payload));
            }

            var tongDaTra = idDatPhong > 0 ? await _payRepo.GetTongDaThanhToanAsync(idDatPhong) : 0m;
            var booking = idDatPhong > 0 ? await _bookingRepo.GetByIdAsync(idDatPhong) : null;
            decimal tongTien = (decimal)(booking?.TongTienTamTinh ?? 0m);
            if (tongDaTra >= tongTien && tongTien > 0)
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaThanhToanDayDu");
            else
                await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "DaCoc");
            if (idDatPhong > 0) await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
            return Ok(new { success=true, message="Xác nhận thanh toán thành công (URL)", data = new { idDatPhong, tongDaTra } });
        }
        else
        {
            // Luồng init-direct: không lưu thất bại, chỉ mở khóa phòng nếu HOLD
            var info = payload.TryGetValue("vnp_OrderInfo", out var oi) ? oi : string.Empty;
            var parts = info.Split('|');
            if (parts.Length >= 2 && parts[0] == "HOLD")
            {
                var holdToken = parts[1];
                var holdRepo = HttpContext.RequestServices.GetService(typeof(HotelBookingApi.Data.PreBookingHoldRepository)) as HotelBookingApi.Data.PreBookingHoldRepository;
                try { await holdRepo!.ReleaseAsync(holdToken); } catch { }
            }
            if (payment != null)
            {
                int idDatPhongFail = 0; try { idDatPhongFail = (int)(payment.IdDatPhong ?? 0); } catch { }
                await _payRepo.UpdateTrangThaiAsync(maGiaoDich, "Thất bại", System.Text.Json.JsonSerializer.Serialize(payload));
                return Ok(new { success=false, message=$"Xác nhận thất bại (responseCode={responseCode}, valid={isValid})", data = new { idDatPhong = idDatPhongFail } });
            }
            // Không có payment record (luồng DIRECT) => trả về thất bại đơn giản
            return Ok(new { success=false, message=$"Xác nhận thất bại (responseCode={responseCode}, valid={isValid})" });
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

    // (Đã xoá) Endpoint cấu hình tỷ lệ cọc không còn cần thiết

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

    // Báo cáo thanh toán - Admin xem thống kê
    [Authorize(Roles="Admin")]
    [HttpGet("report")]
    public async Task<IActionResult> PaymentReport([FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null, [FromQuery] string? status = null)
    {
        try 
        {
            // Mặc định lấy 30 ngày gần nhất
            var from = fromDate ?? DateTime.UtcNow.AddDays(-30);
            var to = toDate ?? DateTime.UtcNow;

            var payments = await _payRepo.GetReportAsync(from, to, status);
            
            // Tính thống kê
            var totalAmount = payments.Sum(p => Convert.ToDecimal(p.SoTien ?? 0));
            var totalTransactions = payments.Count();
            var successCount = payments.Count(p => p.TrangThai?.ToString() == "Thanh toán thành công");
            var pendingCount = payments.Count(p => p.TrangThai?.ToString() == "Chờ thanh toán");
            var failedCount = payments.Count(p => p.TrangThai?.ToString() == "Thanh toán thất bại");

            return Ok(new { 
                success = true, 
                message = "Báo cáo thanh toán",
                data = new {
                    fromDate = from,
                    toDate = to,
                    summary = new {
                        totalAmount,
                        totalTransactions,
                        successCount,
                        pendingCount,
                        failedCount,
                        successRate = totalTransactions > 0 ? (double)successCount / totalTransactions * 100 : 0
                    },
                    payments = payments.Select(p => new {
                        id = p.Id,
                        maGiaoDich = p.MaGiaoDich?.ToString(),
                        soTien = Convert.ToDecimal(p.SoTien ?? 0),
                        trangThai = p.TrangThai?.ToString(),
                        phuongThuc = p.PhuongThuc?.ToString(),
                        ngayTao = p.NgayTao,
                        idDatPhong = p.IdDatPhong
                    })
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server khi lấy báo cáo", error = ex.Message });
        }
    }

    // Thống kê doanh thu theo tháng - Admin only  
    [Authorize(Roles="Admin")]
    [HttpGet("revenue-stats")]
    public async Task<IActionResult> RevenueStats([FromQuery] int year = 0, [FromQuery] int month = 0)
    {
        try 
        {
            if (year == 0) year = DateTime.UtcNow.Year;
            if (month == 0) month = DateTime.UtcNow.Month;

            var stats = await _payRepo.GetRevenueStatsAsync(year, month);
            
            return Ok(new { 
                success = true, 
                message = $"Thống kê doanh thu tháng {month}/{year}",
                data = stats
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi server khi lấy thống kê", error = ex.Message });
        }
    }

    // 💰 API DOANH THU CỦA APP - 10% Hoa hồng từ chủ cơ sở
    // TODO: Thêm lại [Authorize(Roles="Admin")] sau khi test xong
    // [Authorize(Roles="Admin")]
    [HttpGet("app-revenue")]
    public async Task<IActionResult> GetAppRevenue(
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null,
        [FromQuery] int year = 0,
        [FromQuery] int month = 0)
    {
        try 
        {
            // Xử lý tham số thời gian
            DateTime startDate, endDate;
            
            if (!string.IsNullOrEmpty(fromDate) && !string.IsNullOrEmpty(toDate))
            {
                startDate = DateTime.Parse(fromDate);
                endDate = DateTime.Parse(toDate);
            }
            else if (year > 0 && month > 0)
            {
                startDate = new DateTime(year, month, 1);
                endDate = startDate.AddMonths(1).AddDays(-1);
            }
            else if (year > 0)
            {
                startDate = new DateTime(year, 1, 1);
                endDate = new DateTime(year, 12, 31);
            }
            else
            {
                // Mặc định: tháng hiện tại
                var now = DateTime.UtcNow;
                startDate = new DateTime(now.Year, now.Month, 1);
                endDate = startDate.AddMonths(1).AddDays(-1);
            }

            // Lấy tất cả giao dịch thành công trong khoảng thời gian
            var successfulPayments = await _payRepo.GetAppRevenueAsync(startDate, endDate);
            
            if (successfulPayments == null || !successfulPayments.Any())
            {
                return Ok(new {
                    success = true,
                    message = $"Không có giao dịch nào trong khoảng thời gian từ {startDate:dd/MM/yyyy} đến {endDate:dd/MM/yyyy}",
                    data = new {
                        overview = new {
                            tongSoGiaoDich = 0,
                            tongGiaTriGiaoDich = 0,
                            doanhThuApp = 0
                        }
                    }
                });
            }
            
            // Tính toán doanh thu app (10% của tổng giao dịch)
            decimal totalTransactionValue = 0;
            foreach (var payment in successfulPayments)
            {
                var soTien = GetDynamicValue<decimal?>(payment, "SoTien");
                totalTransactionValue += soTien ?? 0;
            }
            
            decimal appCommissionRate = 0.10m; // 10%
            decimal appRevenue = totalTransactionValue * appCommissionRate;
            
            // Phân tích theo loại giao dịch - Convert to strongly typed list
            var paymentsList = successfulPayments.Select(p => new PaymentData {
                LoaiGiaoDich = GetDynamicValue<string>(p, "LoaiGiaoDich") ?? "Khác",
                SoTien = GetDynamicValue<decimal?>(p, "SoTien") ?? 0m,
                NgayThanhToan = GetDynamicValue<DateTime?>(p, "NgayThanhToan"),
                IdCoSoLuuTru = GetDynamicValue<int?>(p, "IdCoSoLuuTru"),
                TenCoSo = GetDynamicValue<string>(p, "TenCoSo") ?? "Không xác định"
            }).ToList();
            
            var revenueByType = paymentsList
                .GroupBy(p => p.LoaiGiaoDich)
                .Select(g => new {
                    loaiGiaoDich = g.Key,
                    soGiaoDich = g.Count(),
                    tongGiaTriGiaoDich = g.Sum(p => p.SoTien),
                    doanhThuApp = g.Sum(p => p.SoTien) * appCommissionRate
                }).ToList();

            // Phân tích theo tháng (nếu query theo năm)
            var monthlyBreakdown = paymentsList
                .GroupBy(p => new { 
                    Year = p.NgayThanhToan?.Year ?? startDate.Year,
                    Month = p.NgayThanhToan?.Month ?? startDate.Month
                })
                .Select(g => new {
                    year = g.Key.Year,
                    month = g.Key.Month,
                    soGiaoDich = g.Count(),
                    tongGiaTriGiaoDich = g.Sum(p => p.SoTien),
                    doanhThuApp = g.Sum(p => p.SoTien) * appCommissionRate
                })
                .OrderBy(x => x.year).ThenBy(x => x.month)
                .ToList();

            // Top cơ sở lưu trú đóng góp nhiều nhất
            var topAccommodations = paymentsList
                .Where(p => p.IdCoSoLuuTru.HasValue)
                .GroupBy(p => new { 
                    Id = p.IdCoSoLuuTru.Value,
                    TenCoSo = p.TenCoSo
                })
                .Select(g => new {
                    idCoSoLuuTru = g.Key.Id,
                    tenCoSo = g.Key.TenCoSo,
                    soGiaoDich = g.Count(),
                    tongGiaTriGiaoDich = g.Sum(p => p.SoTien),
                    doanhThuAppTuCoSo = g.Sum(p => p.SoTien) * appCommissionRate,
                    phanTramDongGop = totalTransactionValue > 0 
                        ? Math.Round((g.Sum(p => p.SoTien) / totalTransactionValue) * 100, 2)
                        : 0
                })
                .OrderByDescending(x => x.doanhThuAppTuCoSo)
                .Take(10)
                .ToList();

            var result = new {
                success = true,
                message = $"📊 Báo cáo doanh thu app từ {startDate:dd/MM/yyyy} đến {endDate:dd/MM/yyyy}",
                data = new {
                    // Tổng quan
                    overview = new {
                        kiGianBaoCao = new {
                            tuNgay = startDate.ToString("dd/MM/yyyy"),
                            denNgay = endDate.ToString("dd/MM/yyyy")
                        },
                        tongSoGiaoDich = paymentsList.Count,
                        tongGiaTriGiaoDich = totalTransactionValue,
                        tyLeHoaHong = $"{appCommissionRate * 100}%",
                        doanhThuApp = appRevenue,
                        doanhThuAppFormatted = $"{appRevenue:N0} VND"
                    },

                    // Chi tiết theo loại giao dịch
                    theo_LoaiGiaoDich = revenueByType,

                    // Chi tiết theo tháng (nếu có)
                    theo_Thang = monthlyBreakdown,

                    // Top cơ sở đóng góp
                    topCoSoDongGop = topAccommodations,

                    // Thống kê bổ sung
                    thongKeBoSung = new {
                        giaoDichTrungBinh = paymentsList.Count > 0 
                            ? Math.Round(totalTransactionValue / paymentsList.Count, 0) 
                            : 0,
                        doanhThuAppTrungBinhMoiGiaoDich = paymentsList.Count > 0
                            ? Math.Round(appRevenue / paymentsList.Count, 0)
                            : 0,
                        soCoSoCoGiaoDich = paymentsList.Where(p => p.IdCoSoLuuTru.HasValue)
                                                       .Select(p => p.IdCoSoLuuTru.Value)
                                                       .Distinct()
                                                       .Count()
                    }
                }
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetAppRevenue ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi server khi tính doanh thu app", 
                error = ex.Message,
                detail = ex.InnerException?.Message
            });
        }
    }

    // 📈 API Thống kê doanh thu app theo thời gian (Demo không cần auth)
    [HttpGet("app-revenue-demo")]
    public async Task<IActionResult> GetAppRevenueDemo(
        [FromQuery] string period = "month", // "month", "quarter", "year"
        [FromQuery] int year = 0,
        [FromQuery] int month = 0)
    {
        try 
        {
            var now = DateTime.UtcNow;
            DateTime startDate, endDate;
            string periodName;

            switch (period.ToLower())
            {
                case "quarter":
                    var quarter = (now.Month - 1) / 3 + 1;
                    startDate = new DateTime(now.Year, (quarter - 1) * 3 + 1, 1);
                    endDate = startDate.AddMonths(3).AddDays(-1);
                    periodName = $"Quý {quarter}/{now.Year}";
                    break;
                case "year":
                    var targetYear = year > 0 ? year : now.Year;
                    startDate = new DateTime(targetYear, 1, 1);
                    endDate = new DateTime(targetYear, 12, 31);
                    periodName = $"Năm {targetYear}";
                    break;
                default: // month
                    var targetMonth = month > 0 ? month : now.Month;
                    var targetYearForMonth = year > 0 ? year : now.Year;
                    startDate = new DateTime(targetYearForMonth, targetMonth, 1);
                    endDate = startDate.AddMonths(1).AddDays(-1);
                    periodName = $"Tháng {targetMonth}/{targetYearForMonth}";
                    break;
            }

            // Lấy dữ liệu giao dịch
            var payments = await _payRepo.GetAppRevenueAsync(startDate, endDate);
            
            decimal totalValue = 0;
            foreach (var payment in payments)
            {
                var soTien = GetDynamicValue<decimal?>(payment, "SoTien");
                totalValue += soTien ?? 0;
            }
            
            decimal appRevenue = totalValue * 0.10m;

            var quickStats = new {
                success = true,
                message = $"🚀 Demo - Doanh thu app {periodName}",
                data = new {
                    period = periodName,
                    summary = new {
                        totalTransactions = payments.Count,
                        totalValue = totalValue,
                        appRevenue = appRevenue,
                        formatted = new {
                            totalValue = $"{totalValue:N0} VND",
                            appRevenue = $"{appRevenue:N0} VND",
                            commissionRate = "10%"
                        }
                    },
                    trends = new {
                        dailyAverage = payments.Count > 0 
                            ? Math.Round(appRevenue / (endDate - startDate).Days, 0)
                            : 0,
                        transactionAverage = payments.Count > 0
                            ? Math.Round(totalValue / payments.Count, 0)
                            : 0
                    }
                }
            };

            return Ok(quickStats);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi demo doanh thu app", 
                error = ex.Message 
            });
        }
    }

    // Helper method để truy cập dynamic object an toàn từ Dapper
    private T? GetDynamicValue<T>(dynamic obj, string propertyName)
    {
        try
        {
            // Dapper returns ExpandoObject or DapperRow
            if (obj is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue(propertyName, out var value))
                {
                    if (value == null) return default(T);
                    
                    var targetType = typeof(T);
                    var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                    
                    // Xử lý conversion
                    if (underlyingType == typeof(decimal))
                    {
                        return (T)(object)Convert.ToDecimal(value);
                    }
                    else if (underlyingType == typeof(int))
                    {
                        return (T)(object)Convert.ToInt32(value);
                    }
                    else if (underlyingType == typeof(long))
                    {
                        return (T)(object)Convert.ToInt64(value);
                    }
                    else if (underlyingType == typeof(DateTime))
                    {
                        return (T)(object)Convert.ToDateTime(value);
                    }
                    else if (underlyingType == typeof(string))
                    {
                        return (T)(object)Convert.ToString(value);
                    }
                    else if (underlyingType == typeof(bool))
                    {
                        return (T)(object)Convert.ToBoolean(value);
                    }
                    
                    // Try direct cast
                    if (value is T typedValue)
                    {
                        return typedValue;
                    }
                    
                    // Last resort: Convert.ChangeType
                    return (T)Convert.ChangeType(value, underlyingType);
                }
            }
            
            return default(T);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ GetDynamicValue error for property '{propertyName}': {ex.Message}");
            return default(T);
        }
    }
}


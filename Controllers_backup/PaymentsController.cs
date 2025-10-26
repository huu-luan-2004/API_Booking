using System.Security.Cryptography;
using System.Text;
using Dapper;
using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ThanhToanRepository _payRepo;
    private readonly DatPhongRepository _bookingRepo;
    private readonly SqlConnectionFactory _factory;
    private readonly HuyDatPhongRepository _cancelRepo;
    public PaymentsController(IConfiguration config, ThanhToanRepository payRepo, DatPhongRepository bookingRepo, SqlConnectionFactory factory, HuyDatPhongRepository cancelRepo)
    { _config = config; _payRepo = payRepo; _bookingRepo = bookingRepo; _factory = factory; _cancelRepo = cancelRepo; }

    [HttpGet]
    public IActionResult Health() => Ok(new { success=true, message="API thanh toán hoạt động" });

    [Authorize]
    [HttpPost("create-vnpay-payment")]
    public async Task<IActionResult> CreateVnpayPayment([FromBody] dynamic body)
    {
        int idDatPhong = (int)(body?.idDatPhong ?? 0);
        string? paymentType = body?.paymentType;
        decimal? amount = (decimal?)body?.amount;
        string? orderDescription = body?.orderDescription;
        string? bankCode = body?.bankCode;
        if (idDatPhong<=0) return BadRequest(new { success=false, message="Thiếu thông tin thanh toán (idDatPhong)" });

        // Fetch booking total
        var dp = await _bookingRepo.GetByIdAsync(idDatPhong);
        if (dp is null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });
        decimal ReadTotal(dynamic row)
        {
            if (row is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue("TongTienTamTinh", out var v1) && v1 != null && decimal.TryParse(v1.ToString(), out var d1)) return d1;
                if (dict.TryGetValue("TongTien", out var v2) && v2 != null && decimal.TryParse(v2.ToString(), out var d2)) return d2;
            }
            try { return (decimal)(row?.TongTienTamTinh ?? 0m); } catch {}
            try { return (decimal)(row?.TongTien ?? 0m); } catch {}
            return 0m;
        }
        var tong = ReadTotal(dp);

        var type = (paymentType ?? "full").ToString().ToLowerInvariant();
        decimal amountToPay;
        string loaiGiaoDich;
        string defaultInfo;
        if (type == "deposit") { amountToPay = Math.Round(tong * 0.3m, 0); loaiGiaoDich = "Thanh toán cọc"; defaultInfo=$"Thanh toan coc dat phong #{idDatPhong}"; }
        else if (type == "topup")
        {
            var paid = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
            var remaining = Math.Max(0, Math.Round(tong - paid, 0));
            if (remaining <= 0) return BadRequest(new { success=false, message="Đơn hàng đã thanh toán đủ, không còn số tiền cần bổ sung" });
            amountToPay = amount.HasValue && amount.Value>0 ? Math.Min(remaining, Math.Round(amount.Value)) : remaining;
            loaiGiaoDich = "Thanh toán bổ sung"; defaultInfo=$"Thanh toan bo sung dat phong #{idDatPhong}";
        }
        else { amountToPay = Math.Round(tong, 0); loaiGiaoDich = "Thanh toán"; defaultInfo=$"Thanh toan dat phong #{idDatPhong}"; }
        if (amountToPay<=0) return BadRequest(new { success=false, message="Không xác định được số tiền cần thanh toán từ tổng tiền đơn đặt phòng" });

        // VNPay config
        var tmnCode = _config["VNPAY_TMN_CODE"] ?? "WSAUQG18";
        var hashSecret = _config["VNPAY_HASH_SECRET"] ?? "DGF1EWCJ0F6RRW7XUWVO5G3FG0GYSV2E";
        var vnpUrl = _config["VNPAY_URL"] ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    var returnUrl = _config["VNPAY_RETURN_URL"] ?? "http://localhost:5099/api/payments/vnpay-return";

        var createDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var expireDate = DateTime.UtcNow.AddMinutes(15).ToString("yyyyMMddHHmmss");
        var vnpTxnRef = $"{DateTime.UtcNow:HHmmssfff}{idDatPhong}{Random.Shared.Next(10,99)}";

        string Normalize(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var noDiacritics = s.Replace("đ","d").Replace("Đ","D").Normalize(System.Text.NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in noDiacritics)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return new string(sb.ToString().Where(c => char.IsLetterOrDigit(c) || " #:/.,-".Contains(c)).ToArray());
        }

        var orderInfo = Normalize(orderDescription ?? defaultInfo);
        var dict = new SortedDictionary<string,string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = tmnCode,
            ["vnp_Amount"] = ((long)(amountToPay*100)).ToString(),
            ["vnp_CreateDate"] = createDate,
            ["vnp_ExpireDate"] = expireDate,
            ["vnp_CurrCode"] = "VND",
            ["vnp_IpAddr"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
            ["vnp_Locale"] = "vn",
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_OrderType"] = "billpayment",
            ["vnp_ReturnUrl"] = returnUrl,
            ["vnp_TxnRef"] = vnpTxnRef
        };
        if (!string.IsNullOrWhiteSpace(bankCode)) dict["vnp_BankCode"] = bankCode;

        // Sign
        string Encode(string s) => Uri.EscapeDataString(s).Replace("%20","+");
        var query = string.Join("&", dict.Select(kv => $"{Encode(kv.Key)}={Encode(kv.Value)}"));
        var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hashSecret));
        var signed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        var paymentUrl = $"{vnpUrl}?{query}&vnp_SecureHash={signed}";

        // persist payment record
        try
        {
            await _payRepo.CancelAllPendingForBookingAsync(idDatPhong);
            await _payRepo.CreateAsync(idDatPhong, vnpTxnRef, amountToPay, "VNPAY", "Chờ thanh toán", orderInfo, vnpTxnRef, loaiGiaoDich);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success=false, message="Lỗi cơ sở dữ liệu khi lưu giao dịch", detail = ex.Message });
        }

        return Ok(new { success=true, data = new { paymentUrl, orderId = vnpTxnRef, amount = amountToPay, paymentType = type } });
    }

    [HttpGet("vnpay-return")]
    public async Task<IActionResult> VnpayReturn([FromQuery] Dictionary<string,string> all)
    {
        if (!all.TryGetValue("vnp_SecureHash", out var secure)) return BadRequest(new { success=false, message="Thiếu chữ ký" });
        var dict = new SortedDictionary<string,string>(all.Where(kv => kv.Key!="vnp_SecureHash" && kv.Key!="vnp_SecureHashType").ToDictionary(k=>k.Key,v=>v.Value), StringComparer.Ordinal);
        string Encode(string s) => Uri.EscapeDataString(s).Replace("%20","+");
        var query = string.Join("&", dict.Select(kv => $"{Encode(kv.Key)}={Encode(kv.Value)}"));
        var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_config["VNPAY_HASH_SECRET"] ?? "DGF1EWCJ0F6RRW7XUWVO5G3FG0GYSV2E"));
        var signed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        if (!string.Equals(secure, signed, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success=false, message="Chữ ký không hợp lệ" });

        var orderId = dict["vnp_TxnRef"]; var responseCode = dict.GetValueOrDefault("vnp_ResponseCode"); var status = dict.GetValueOrDefault("vnp_TransactionStatus");
        var amount = (decimal)(long.Parse(dict["vnp_Amount"]) / 100);
        var message = (responseCode == "00" || status == "00") ? "Thanh toán thành công" : "Thanh toán thất bại";
        var success = responseCode == "00" || status == "00";

        await _payRepo.UpdateTrangThaiAsync(orderId, success ? "Thành công" : "Thất bại", System.Text.Json.JsonSerializer.Serialize(dict));
        if (success)
        {
            var thanhToan2 = await _payRepo.GetByMaGiaoDichAsync(orderId);
            if (thanhToan2 != null)
            {
                int idDatPhong = (int)thanhToan2.IdDatPhong;
                var dp = await _bookingRepo.GetByIdAsync(idDatPhong);
                decimal total = 0m;
                if (dp is IDictionary<string, object> d)
                {
                    if (d.TryGetValue("TongTienTamTinh", out var v1) && v1 != null && decimal.TryParse(v1.ToString(), out var d1)) total = d1;
                    else if (d.TryGetValue("TongTien", out var v2) && v2 != null && decimal.TryParse(v2.ToString(), out var d2)) total = d2;
                }
                if (total == 0m) { try { total = (decimal)(dp?.TongTienTamTinh ?? 0m); } catch {} try { total = total==0? (decimal)(dp?.TongTien ?? 0m): total; } catch {} }
                var paid = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
                var loai = (string?)(thanhToan2.LoaiGiaoDich?.ToString());
                if (!string.IsNullOrWhiteSpace(loai) && loai.Contains("cọc", StringComparison.OrdinalIgnoreCase))
                {
                    await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "Đã cọc");
                }
                else if (paid >= total && total > 0)
                {
                    await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "Đã thanh toán");
                }
            }
        }

        return Ok(new { success, message, data = new { orderId, amount, responseCode } });
    }

    [HttpGet("vnpay-ipn")]
    public async Task<IActionResult> VnpayIpn([FromQuery] Dictionary<string,string> all)
    {
        if (!all.TryGetValue("vnp_SecureHash", out var secure)) return Ok(new { RspCode="97", Message="Thiếu chữ ký" });
        var dict = new SortedDictionary<string,string>(all.Where(kv => kv.Key!="vnp_SecureHash" && kv.Key!="vnp_SecureHashType").ToDictionary(k=>k.Key,v=>v.Value), StringComparer.Ordinal);
        string Encode(string s) => Uri.EscapeDataString(s).Replace("%20","+");
        var query = string.Join("&", dict.Select(kv => $"{Encode(kv.Key)}={Encode(kv.Value)}"));
        var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_config["VNPAY_HASH_SECRET"] ?? "DGF1EWCJ0F6RRW7XUWVO5G3FG0GYSV2E"));
        var signed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        if (!string.Equals(secure, signed, StringComparison.OrdinalIgnoreCase)) return Ok(new { RspCode="97", Message="Invalid checksum" });

        var orderId = dict["vnp_TxnRef"]; var responseCode = dict.GetValueOrDefault("vnp_ResponseCode"); var status = dict.GetValueOrDefault("vnp_TransactionStatus");
        var vnpAmount = (decimal)(long.Parse(dict["vnp_Amount"]) / 100);
        var thanhToan = await _payRepo.GetByMaGiaoDichAsync(orderId);
        if (thanhToan is null) return Ok(new { RspCode="01", Message="Không tìm thấy đơn hàng" });
        if (vnpAmount != (decimal)thanhToan.SoTien) return Ok(new { RspCode="04", Message="Số tiền không hợp lệ" });

        if (responseCode == "00" || status == "00")
        {
            await _payRepo.UpdateTrangThaiAsync(orderId, "Thành công", System.Text.Json.JsonSerializer.Serialize(dict));
            var thanhToan2 = await _payRepo.GetByMaGiaoDichAsync(orderId);
            if (thanhToan2 != null)
            {
                int idDatPhong = (int)thanhToan2.IdDatPhong;
                var dp = await _bookingRepo.GetByIdAsync(idDatPhong);
                decimal total = 0m;
                if (dp is IDictionary<string, object> d)
                {
                    if (d.TryGetValue("TongTienTamTinh", out var v1) && v1 != null && decimal.TryParse(v1.ToString(), out var d1)) total = d1;
                    else if (d.TryGetValue("TongTien", out var v2) && v2 != null && decimal.TryParse(v2.ToString(), out var d2)) total = d2;
                }
                if (total == 0m) { try { total = (decimal)(dp?.TongTienTamTinh ?? 0m); } catch {} try { total = total==0? (decimal)(dp?.TongTien ?? 0m): total; } catch {} }
                var paid = await _payRepo.GetTongDaThanhToanAsync(idDatPhong);
                var loai = (string?)(thanhToan2.LoaiGiaoDich?.ToString());
                if (!string.IsNullOrWhiteSpace(loai) && loai.Contains("cọc", StringComparison.OrdinalIgnoreCase))
                {
                    await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "Đã cọc");
                }
                else if (paid >= total && total > 0)
                {
                    await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "Đã thanh toán");
                }
            }
        }
        return Ok(new { RspCode="00", Message="Confirm Success" });
    }

    [Authorize]
    [HttpPost("create-refund")]
    public async Task<IActionResult> CreateRefund([FromBody] dynamic body)
    {
        int idDatPhong = (int)(body?.idDatPhong ?? 0);
        string? lyDo = body?.lyDo;
        if (idDatPhong <= 0) return BadRequest(new { success=false, message="Thiếu thông tin đơn đặt phòng" });

        var dp = await _bookingRepo.GetByIdAsync(idDatPhong);
        if (dp is null) return NotFound(new { success=false, message="Không tìm thấy đơn đặt phòng" });

        var existed = await _cancelRepo.GetByDatPhongIdAsync(idDatPhong);
        if (existed != null) return BadRequest(new { success=false, message="Đơn đặt phòng này đã có yêu cầu hủy" });

        var (tong, daTT, tienHoan, tienPhat, tyLePhat, soNgayConLai, soGioConLai) = await _cancelRepo.TinhTienHoanTraVaTienPhatAsync(idDatPhong);
        int idNguoiDung = 0; int.TryParse(User.FindFirst("id")?.Value, out idNguoiDung);
        var idHuy = await _cancelRepo.CreateAsync(idDatPhong, lyDo ?? "Khách hàng yêu cầu hủy", tienHoan, tienPhat, "Chờ xử lý", idNguoiDung);

        await _bookingRepo.UpdateTrangThaiAsync(idDatPhong, "Yêu cầu hủy");
        var huy = await _cancelRepo.GetByIdAsync(idHuy);
        return Ok(new { success=true, message="Yêu cầu hủy đặt phòng thành công", data = new {
            huyDatPhong = huy,
            thongTinHoanTien = new { tongTien = tong, daThanhToan = daTT, tienHoanLai = tienHoan, tienPhat, tyLePhat, soNgayConLai, soGioConLai,
                chinhSach = soGioConLai >= 24 ? "Hủy trước >=24h: hoàn toàn bộ số đã thanh toán" : "Hủy <24h: phạt 30% tổng tiền; nếu chỉ cọc ~30% thì mất cọc" }
        }});
    }

    private async Task<(bool ok, object? data, string? raw)> CallVnpRefundAsync(Dictionary<string,string> payload)
    {
        var url = _config["VNPAY_API_URL"] ?? "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction";
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var json = JsonSerializer.Serialize(payload);
        var res = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await res.Content.ReadAsStringAsync();
        try { var data = JsonSerializer.Deserialize<object>(body); return (true, data, body); } catch { return (false, null, body); }
    }

    private string ComputeVnpHash(string data)
    {
        var secret = _config["VNPAY_HASH_SECRET"] ?? "DGF1EWCJ0F6RRW7XUWVO5G3FG0GYSV2E";
        var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("confirm-refund")]
    public async Task<IActionResult> ConfirmRefund([FromBody] dynamic body)
    {
        int idHuyDatPhong = (int)(body?.idHuyDatPhong ?? 0);
        string? ghiChu = body?.ghiChu;
        if (idHuyDatPhong <= 0) return BadRequest(new { success=false, message="Thiếu thông tin yêu cầu hủy" });

        var huy = await _cancelRepo.GetByIdAsync(idHuyDatPhong);
        if (huy is null) return NotFound(new { success=false, message="Không tìm thấy yêu cầu hủy" });
        // Chỉ kiểm tra nếu có cột TrangThai
        try { if (huy.TrangThai != null && (string)huy.TrangThai != "Chờ xử lý") return BadRequest(new { success=false, message="Yêu cầu hủy này đã được xử lý" }); } catch {}

        var originalPayment = await _payRepo.GetLatestSuccessPaymentAsync((int)huy.IdDatPhong);
        if (originalPayment is null) return NotFound(new { success=false, message="Không tìm thấy thông tin thanh toán gốc" });

        decimal soTienHoan = 0m; try { soTienHoan = (decimal)(huy.TienHoanLai ?? 0m); } catch { }
        var refundTxId = $"REFUND-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var refund = await _payRepo.CreateRefundAsync((int)huy.IdDatPhong, refundTxId, soTienHoan, $"Hoàn tiền cho đơn hàng #{huy.IdDatPhong}", (string)(originalPayment.MaDonHang ?? originalPayment.MaGiaoDich));

        // VNPay refund call
        string requestId = $"{DateTime.UtcNow:HHmmssfff}{Random.Shared.Next(100,999)}";
        string createDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        string createBy = User?.FindFirst("email")?.Value ?? User?.FindFirst("id")?.Value ?? "system";
        string tmnCode = _config["VNPAY_TMN_CODE"] ?? "WSAUQG18";
        string txnRef = (string)(originalPayment.MaDonHang ?? originalPayment.MaGiaoDich);
        string amount100 = ((long)(soTienHoan * 100)).ToString();
    string? transactionNo = null;
    string? transactionDate = null;
        try
        {
            var meta = (string?)(originalPayment.Meta?.ToString());
            if (!string.IsNullOrWhiteSpace(meta))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string,object>>(meta!);
                if (dict != null)
                {
                    if (dict.TryGetValue("vnp_TransactionNo", out var tno) && tno != null) transactionNo = tno.ToString();
                    if (dict.TryGetValue("vnp_PayDate", out var pdate) && pdate != null) transactionDate = pdate.ToString();
                }
            }
        }
        catch {}

        var dataToSign = string.Join('|', new [] {
            requestId,
            "2.1.0",
            "refund",
            tmnCode,
            "02",
            txnRef,
            amount100,
            transactionNo ?? string.Empty,
            transactionDate ?? string.Empty,
            createBy,
            createDate
        });
        var secureHash = ComputeVnpHash(dataToSign);

        var payload = new Dictionary<string,string>
        {
            ["vnp_RequestId"] = requestId,
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "refund",
            ["vnp_TmnCode"] = tmnCode,
            ["vnp_TransactionType"] = "02",
            ["vnp_TxnRef"] = txnRef,
            ["vnp_Amount"] = amount100,
            ["vnp_TransactionNo"] = transactionNo ?? string.Empty,
            ["vnp_TransactionDate"] = transactionDate ?? string.Empty,
            ["vnp_CreateBy"] = createBy,
            ["vnp_CreateDate"] = createDate,
            ["vnp_IpAddr"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
            ["vnp_OrderInfo"] = $"Hoan tien don #{huy.IdDatPhong}",
            ["vnp_SecureHash"] = secureHash
        };

        var (ok, data, raw) = await CallVnpRefundAsync(payload);
        bool vnpOk = false; string? rspCode = null;
        if (data is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("RspCode", out var c)) rspCode = c.GetString();
            vnpOk = rspCode == "00";
        }

        await _payRepo.UpdateTrangThaiAsync((string)refund.MaGiaoDich, vnpOk ? "Thành công" : "Thất bại", data != null ? JsonSerializer.Serialize(data) : raw);
        await _cancelRepo.UpdateTrangThaiAsync(idHuyDatPhong, "Đã xử lý", vnpOk ? (ghiChu ?? "Đã hoàn tiền thành công") : (ghiChu ?? "Hoàn tiền thất bại"));
        await _bookingRepo.UpdateTrangThaiAsync((int)huy.IdDatPhong, "Đã hủy");

        return Ok(new { success=true, message = vnpOk ? "Xác nhận hoàn tiền thành công" : "Đã xử lý yêu cầu hủy, hoàn tiền thất bại từ VNPay", data = new {
            thanhToanHoanTien = refund,
            tienHoanLai = soTienHoan,
            tienPhat = (decimal?)huy.TienPhat,
            vnpResult = data ?? raw
        }});
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("reject-refund")]
    public async Task<IActionResult> RejectRefund([FromBody] dynamic body)
    {
        int idHuyDatPhong = (int)(body?.idHuyDatPhong ?? 0);
        string? lyDo = body?.lyDo;
        if (idHuyDatPhong <= 0) return BadRequest(new { success=false, message="Thiếu thông tin yêu cầu hủy" });
        if (string.IsNullOrWhiteSpace(lyDo)) return BadRequest(new { success=false, message="Vui lòng cung cấp lý do từ chối" });

        var huy = await _cancelRepo.GetByIdAsync(idHuyDatPhong);
        if (huy is null) return NotFound(new { success=false, message="Không tìm thấy yêu cầu hủy" });
        try { if (huy.TrangThai != null && (string)huy.TrangThai != "Chờ xử lý") return BadRequest(new { success=false, message="Yêu cầu hủy này đã được xử lý" }); } catch {}

        await _cancelRepo.UpdateTrangThaiAsync(idHuyDatPhong, "Từ chối", lyDo);
        await _bookingRepo.UpdateTrangThaiAsync((int)huy.IdDatPhong, "Đã thanh toán");
        return Ok(new { success=true, message="Đã từ chối yêu cầu hoàn tiền", data = new { idHuyDatPhong, lyDo } });
    }

    [Authorize]
    [HttpGet("by-booking/{idDatPhong:int}")]
    [HttpGet("booking/{idDatPhong:int}")]
    public async Task<IActionResult> GetPaymentsByBooking([FromRoute] int idDatPhong)
    {
        if (idDatPhong <= 0) return BadRequest(new { success=false, message="Thiếu ID đặt phòng" });
        var list = await _payRepo.ListByBookingAsync(idDatPhong);
        return Ok(new { success=true, data = list });
    }
}

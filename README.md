# Hotel Booking API (.NET 8)

Port .NET Web API tương thích với hành vi của Node.js API hiện tại:
- Đăng ký/đăng nhập bằng Firebase idToken, tạo JWT backend.
- Đăng ký mặc định tạo vai trò KhachHang.
- Admin chỉ được tạo tài khoản Chủ cơ sở.
- SQL Server truy cập qua Dapper, tự phát hiện Id không-IDENTITY để gán MAX(Id)+1.

## Cấu hình
- Sửa `appsettings.json` hoặc biến môi trường:
  - ConnectionStrings:SqlServer
  - JWT_SECRET
  - FIREBASE_STORAGE_BUCKET (bắt buộc nếu muốn upload ảnh)
  - FIREBASE_SERVICE_ACCOUNT_PATH (hoặc 3 biến projectId/clientEmail/privateKey)

## Chạy dự án
- Yêu cầu .NET 8 SDK
- Tại thư mục `hotel-booking-api-dotnet`:
  - dotnet restore
  - dotnet run (mặc định chạy http://localhost:5000)

Chạy trên cổng tùy chọn (ví dụ 5099):
- dotnet run --urls "http://localhost:5099"

## Endpoints
- POST /api/auth/register { idToken, hoTen?, soDienThoai? }
- POST /api/auth/login { idToken }
- GET /api/auth/profile (Bearer)
- POST /api/admin/users (Bearer Admin) { email, password, role:"chucoso", hoTen?, soDienThoai? }
- GET /api/payments  (health)
- POST /api/payments/create-vnpay-payment (Bearer) { idDatPhong, paymentType: "deposit"|"topup"|"full", amount?, bankCode?, orderDescription? }
  - Returns: { paymentUrl, orderId, amount, paymentType }
- GET /api/payments/vnpay-return (VNPay returnUrl) -> verifies checksum and updates transaction
- GET /api/payments/vnpay-ipn (VNPay server-to-server) -> idempotent confirmation

## API routes and purposes (for Postman)

Authentication
- POST /api/auth/register
  - Body: { idToken, hoTen?, soDienThoai? }
  - Giải thích (TV): Đăng ký bằng Firebase ID token. Nếu chưa có trong DB, tạo user mới với vai trò mặc định KhachHang.
- POST /api/auth/login
  - Body: { idToken }
  - Giải thích (TV): Xác thực Firebase, đọc vai trò từ DB (NguoiDung.VaiTro) và trả về JWT backend: { token, accessToken, roles, user }.
- GET /api/auth/profile
  - Header: Authorization: Bearer <backend JWT>
  - Giải thích (TV): Trả về thông tin cơ bản lấy từ JWT (id, email...).

Users (admin)
- POST /api/admin/users
  - Header: Bearer (Admin)
  - Body: { email, password, role:"chucoso", hoTen?, soDienThoai? }
  - Giải thích (TV): Admin tạo tài khoản Chủ cơ sở: tạo user trên Firebase và bản ghi người dùng trong DB với vai trò ChuCoSo.

Accommodations (Cơ Sở Lưu Trú)
- GET /api/cosoluutru
  - Query: page?, pageSize?, q?, ownerId?
  - Behavior:
    - Giải thích (TV):
      - Admin xem tất cả (bao gồm Chờ duyệt + Đã duyệt).
      - Người dùng thường: nếu xem ownerId của chính mình (hoặc không truyền ownerId) thì thấy cả bản ghi Chờ duyệt của mình.
      - Không cho phép người dùng thường xem dữ liệu của người khác; hệ thống sẽ tự ép về id của chính họ.
- GET /api/cosoluutru/{id}
  - Giải thích (TV): Trả về chi tiết. Nếu chưa được duyệt thì chỉ Admin hoặc Chủ sở hữu mới xem được.
- POST /api/cosoluutru
  - Header: Bearer (role ChuCoSo)
  - Body: { tenCoSo, moTa?, soTaiKhoan?, tenTaiKhoan?, tenNganHang?, anh?, idDiaChi? }
  - Giải thích (TV): Chủ cơ sở tạo cơ sở lưu trú mới. Trạng thái mặc định Chờ duyệt (ChoDuyet), tự gán chủ sở hữu là user đang đăng nhập.
- PATCH /api/cosoluutru/{id}/approve
  - Header: Bearer (Admin)
  - Giải thích (TV): Duyệt cơ sở (TrangThaiDuyet=DaDuyet).
- PATCH /api/cosoluutru/{id}/reject
  - Header: Bearer (Admin)
  - Body: { lyDo }
  - Giải thích (TV): Từ chối cơ sở (TrangThaiDuyet=TuChoi) kèm lý do.

Rooms
- GET /api/rooms
- GET /api/rooms/{id}
- POST /api/rooms (Bearer ChuCoSo)
  - JSON body: { tenPhong, moTa?, soNguoiToiDa?, idCoSoLuuTru, idLoaiPhong?, gia? }
  - Hoặc multipart/form-data (để upload ảnh):
    - fields: tenPhong, moTa?, soNguoiToiDa?, idCoSoLuuTru, idLoaiPhong?, gia?
    - file: dùng key file hoặc image (chấp nhận file đầu tiên nếu không đặt tên)
  - Lưu ý:
    - Chỉ vai trò ChuCoSo được tạo phòng và chỉ được tạo cho cơ sở do chính mình sở hữu (idCoSoLuuTru phải thuộc user hiện tại). Admin có thể bỏ qua kiểm tra sở hữu.
    - Khi upload ảnh, API sẽ đẩy file lên Firebase Storage và lưu cột AnhUrl, AnhPath trong bảng Phong.
    - Cấu hình bắt buộc để upload: đặt biến môi trường FIREBASE_STORAGE_BUCKET; khuyến nghị đặt thêm FIREBASE_SERVICE_ACCOUNT_PATH.
  - Trả về: { success, message, data } với data chứa AnhUrl, AnhPath nếu có.

- DELETE /api/rooms/{id}/image (Bearer ChuCoSo|Admin)
  - Xóa ảnh của phòng trên Firebase Storage (nếu có) và clear cột AnhUrl/AnhPath trong DB.
  - Kiểm tra quyền sở hữu: ChuCoSo chỉ xóa ảnh của phòng thuộc cơ sở của mình; Admin có thể xóa bất kỳ.
  - Trả về: { success, message }

Images (quản lý ảnh dùng Firebase Storage)
- POST /api/images/upload (Bearer ChuCoSo|Admin)
  - multipart/form-data: key file (hoặc image), query folder? (mặc định uploads)
  - Trả về: { path, url }. Nếu bucket private, url có thể không truy cập công khai → dùng signed-url.
- GET /api/images (Bearer ChuCoSo|Admin)
  - Query: prefix? (ví dụ rooms), pageSize?, pageToken?
- DELETE /api/images?path=<objectName> (Bearer ChuCoSo|Admin)
- GET /api/images/signed-url?path=<objectName>&minutes=10 (Bearer ChuCoSo|Admin)
  - Yêu cầu FIREBASE_SERVICE_ACCOUNT_PATH để ký URL tạm thời.
  - Khuyến nghị dùng khi bucket để private.

Promotions
- GET /api/promotions/rooms/{id}/preview
  - Giải thích (TV): Xem thử khuyến mãi áp dụng cho phòng cụ thể.

Bookings
- GET /api/bookings
- POST /api/bookings (Bearer)
- GET /api/bookings/user (Bearer)
- GET /api/bookings/check-availability
- GET /api/bookings/{id}
  - Giải thích (TV): API đặt phòng (tạo, xem danh sách, kiểm tra phòng trống, xem đặt phòng của chính mình...).

Payments
- GET /api/payments
- POST /api/payments/create-vnpay-payment (Bearer)
- GET /api/payments/vnpay-return
- GET /api/payments/vnpay-ipn
- POST /api/payments/create-refund (if enabled)
  - Giải thích (TV): Tích hợp VNPay (tạo thanh toán, return URL, IPN, hoàn tiền nếu bật).

Development & Testing
- GET /api/dev/token
  - Query: userId: number (bắt buộc)
  - Returns: { token, accessToken, roles, userId, email }
  - Giải thích (TV): Lấy JWT phục vụ test. Vai trò luôn được đọc từ cột VaiTro của bảng NguoiDung theo userId, không nhận role từ client.
- GET /api/dev/db-check
  - Giải thích (TV): Chẩn đoán trong môi trường phát triển: kiểm tra cấu trúc bảng và dữ liệu mẫu.

Gỡ lỗi IDE (IntelliSense) vs. build
- Nếu build chạy thành công nhưng IDE báo lỗi namespace (ví dụ Google.Cloud.* hoặc FirebaseStorageService không tìm thấy), hãy thử:
  - Reload Window (VS Code), hoặc Restart C# / .NET language server
  - Xóa thư mục bin/ và obj/ rồi build lại
  - Đảm bảo tiến trình API đang chạy không khoá file (dừng server trước khi rebuild)

### Suggested Postman flow
1) Get admin token từ DB role
  - GET /api/dev/token?userId=3
   - Save data.accessToken as accessToken_admin.
2) Get owner token từ DB role
  - GET /api/dev/token?userId={id}
   - Save as accessToken_owner.
3) List accommodations (owner)
  - GET /api/cosoluutru?ownerId={id}&pageSize=100
   - Header: Authorization: Bearer {{accessToken_owner}}
  - Giải thích (TV): Chủ cơ sở xem danh sách của chính mình (bao gồm Chờ duyệt + Đã duyệt).
4) Approve one (admin)
   - PATCH /api/cosoluutru/{id}/approve
   - Header: Authorization: Bearer {{accessToken_admin}}
  - Giải thích (TV): Admin duyệt một cơ sở.
5) Create new (owner)
   - POST /api/cosoluutru
   - Header: Authorization: Bearer {{accessToken_owner}}
   - Body: { "tenCoSo":"CSLT Demo", "moTa":"..." }
  - Giải thích (TV): Chủ cơ sở tạo cơ sở mới, hệ thống tự gán trạng thái Chờ duyệt.

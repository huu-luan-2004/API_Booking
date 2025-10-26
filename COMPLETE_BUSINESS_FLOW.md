# 🏨 HOTEL BOOKING API - FULL BUSINESS FLOW

## 📋 **Tổng quan**

API quản lý đặt phòng khách sạn hoàn chỉnh với **5 bước chính**:
1. **🔐 Đăng nhập** - Firebase Authentication + JWT
2. **🏢 Tạo cơ sở lưu trú** - Tự động tạo địa chỉ + liên kết
3. **🛏️ Tạo phòng** - Upload ảnh + quản lý inventory
4. **📅 Đặt phòng** - Booking với validation
5. **💳 Thanh toán** - VNPay integration

---

## 🔍 **Chi tiết từng bước**

### 1️⃣ **AUTHENTICATION FLOW**

#### **Đăng nhập Production (Firebase)**
```http
POST /api/auth/login
Content-Type: application/json

{
  "idToken": "Firebase_ID_Token"
}
```

#### **Đăng nhập Development (Test)**
```http
GET /api/dev/token?userId=1
```

**Vai trò:**
- `Admin` - Toàn quyền hệ thống
- `ChuCoSo` - Quản lý cơ sở và phòng của mình
- `KhachHang` - Đặt phòng và thanh toán

**Response:**
```json
{
  "success": true,
  "data": {
    "user": { "id": 1, "email": "user@example.com", "vaiTro": "ChuCoSo" },
    "token": "JWT_TOKEN",
    "roles": ["ChuCoSo"]
  }
}
```

---

### 2️⃣ **TẠO CƠ SỞ LƯU TRÚ + ĐỊA CHỈ**

```http
POST /api/cosoluutru
Content-Type: multipart/form-data
Authorization: Bearer <token>

Form Data:
✅ tenCoSo: "Hotel ABC"
✅ moTa: "Khách sạn 3 sao"
✅ soTaiKhoan: "1234567890"
✅ tenTaiKhoan: "Nguyen Van A"
✅ tenNganHang: "Vietcombank"
✅ soNha: "123"
✅ phuong: "Phường ABC"
✅ quan: "Quận XYZ" 
✅ thanhPho: "Hà Nội" (bắt buộc)
✅ kinhDo: 105.804817
✅ viDo: 21.028511
✅ file: <image_file>
```

**Quy trình backend:**
1. **Tạo địa chỉ** trong `DiaChiChiTiet`
2. **Kiểm tra trùng lặp** địa chỉ
3. **Lưu ảnh** vào `/wwwroot/uploads/accommodations/`
4. **Tạo cơ sở** với `IdDiaChi` tự động
5. **Set trạng thái** `ChoDuyet`

**Response:**
```json
{
  "success": true,
  "data": {
    "Id": 1,
    "TenCoSo": "Hotel ABC",
    "IdDiaChi": 5,
    "TrangThaiDuyet": "ChoDuyet",
    "imageUrl": "http://localhost:5000/uploads/accommodations/image.jpg"
  }
}
```

---

### 3️⃣ **TẠO PHÒNG**

```http
POST /api/rooms
Content-Type: multipart/form-data
Authorization: Bearer <token>

Form Data:
✅ idCoSoLuuTru: 1
✅ tenPhong: "Phòng Deluxe 101"
✅ giaPhong: 1500000
✅ moTa: "Phòng deluxe với view đẹp"
✅ soGiuong: 2
✅ dienTich: 35
✅ sucChua: 4
✅ file: <room_image>
```

**Quy trình backend:**
1. **Validate ownership** - Chỉ chủ cơ sở mới tạo được phòng
2. **Upload ảnh** vào `/wwwroot/uploads/rooms/`
3. **Tạo phòng** với status mặc định
4. **Liên kết** với `LoaiPhong` (nếu có)

**Response:**
```json
{
  "success": true,
  "data": {
    "Id": 1,
    "TenPhong": "Phòng Deluxe 101",
    "GiaPhong": 1500000,
    "IdCoSoLuuTru": 1,
    "imageUrl": "http://localhost:5000/uploads/rooms/room.jpg"
  }
}
```

---

### 4️⃣ **ĐẶT PHÒNG**

```http
POST /api/bookings
Content-Type: application/json
Authorization: Bearer <token>

{
  "idPhong": 1,
  "ngayNhanPhong": "2024-01-01",
  "ngayTraPhong": "2024-01-02",
  "tongTien": 3000000
}
```

**Quy trình backend:**
1. **Validate dates** - Ngày hợp lệ, không quá khứ
2. **Check availability** - Phòng có sẵn không
3. **Calculate total** - Tính tổng tiền theo ngày
4. **Create booking** với trạng thái `Chờ thanh toán`
5. **Send confirmation**

**Response:**
```json
{
  "success": true,
  "data": {
    "Id": 1,
    "IdPhong": 1,
    "IdNguoiDung": 2,
    "NgayNhanPhong": "2024-01-01",
    "NgayTraPhong": "2024-01-02",
    "TongTienTamTinh": 3000000,
    "TrangThai": "Chờ thanh toán"
  }
}
```

---

### 5️⃣ **THANH TOÁN VNPAY**

```http
POST /api/payments/create-vnpay-payment
Content-Type: application/json
Authorization: Bearer <token>

{
  "idDatPhong": 1,
  "paymentType": "deposit", // "deposit" | "full" | "topup"
  "bankCode": "VCB",
  "orderDescription": "Thanh toan dat phong"
}
```

**Loại thanh toán:**
- **`deposit`** - Cọc 30% 
- **`full`** - Thanh toán đầy đủ
- **`topup`** - Thanh toán bổ sung (phần còn lại)

**Quy trình backend:**
1. **Validate booking** - Kiểm tra đặt phòng tồn tại
2. **Calculate amount** - Tính số tiền cần thanh toán
3. **Create VNPay URL** - Generate secure payment URL
4. **Save pending payment** - Lưu giao dịch chờ xử lý
5. **Return payment URL**

**Response:**
```json
{
  "success": true,
  "data": {
    "paymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?...",
    "orderId": "20241020151234567",
    "amount": 900000,
    "paymentType": "Thanh toán cọc"
  }
}
```

**VNPay Callback:**
```http
GET /api/payments/vnpay-return?vnp_ResponseCode=00&...
GET /api/payments/vnpay-ipn?vnp_ResponseCode=00&... (Server-to-server)
```

**Kết quả thanh toán:**
- **`vnp_ResponseCode=00`** → Thành công → Update trạng thái booking
- **Khác 00** → Thất bại → Giữ nguyên trạng thái

---

## 🔄 **BUSINESS WORKFLOW**

```mermaid
graph TD
    A[👤 Đăng nhập] --> B{Vai trò?}
    B -->|ChuCoSo| C[🏢 Tạo cơ sở lưu trú]
    B -->|KhachHang| G[📱 Xem danh sách cơ sở]
    
    C --> D[🛏️ Tạo phòng]
    D --> E[✅ Chờ admin duyệt]
    E --> F[📢 Phòng có sẵn]
    
    G --> H[📅 Chọn phòng + ngày]
    H --> I[💰 Tạo đặt phòng]
    I --> J[💳 Thanh toán VNPay]
    
    J --> K{Kết quả?}
    K -->|Thành công| L[✅ Booking confirmed]
    K -->|Thất bại| M[❌ Booking pending]
    
    F --> G
```

---

## 📊 **DATABASE FLOW**

### **Tables được tạo/cập nhật:**

1. **`NguoiDung`** - User authentication & roles
2. **`DiaChiChiTiet`** - Địa chỉ chi tiết với GPS coordinates  
3. **`CoSoLuuTru`** - Cơ sở lưu trú + link to địa chỉ
4. **`Phong`** - Phòng + link to cơ sở
5. **`DatPhong`** - Booking + link to phòng & user
6. **`ThanhToan`** - Payment transactions + VNPay data

### **Relationships:**
```
NguoiDung (1) → (n) CoSoLuuTru
DiaChiChiTiet (1) → (n) CoSoLuuTru  
CoSoLuuTru (1) → (n) Phong
Phong (1) → (n) DatPhong
NguoiDung (1) → (n) DatPhong
DatPhong (1) → (n) ThanhToan
```

---

## 🛠️ **TESTING**

### **1. Automated Test:**
Mở file `FULL_FLOW_TEST.html` trong trình duyệt để test toàn bộ flow

### **2. Manual API Test:**
```bash
# 1. Login
curl -X POST http://localhost:5000/api/dev/token?userId=1

# 2. Create accommodation (with form-data)
curl -X POST http://localhost:5000/api/cosoluutru \
  -H "Authorization: Bearer <token>" \
  -F "tenCoSo=Hotel Test" \
  -F "thanhPho=Hà Nội"

# 3. Create room
curl -X POST http://localhost:5000/api/rooms \
  -H "Authorization: Bearer <token>" \
  -F "idCoSoLuuTru=1" \
  -F "tenPhong=Phòng Test"

# 4. Create booking (switch to customer)
curl -X POST http://localhost:5000/api/bookings \
  -H "Authorization: Bearer <customer_token>" \
  -H "Content-Type: application/json" \
  -d '{"idPhong":1,"ngayNhanPhong":"2024-01-01","ngayTraPhong":"2024-01-02","tongTien":3000000}'

# 5. Create payment
curl -X POST http://localhost:5000/api/payments/create-vnpay-payment \
  -H "Authorization: Bearer <customer_token>" \
  -H "Content-Type: application/json" \
  -d '{"idDatPhong":1,"paymentType":"deposit"}'
```

---

## 🔧 **CONFIGURATION**

### **Required Environment Variables:**
```bash
# Database
ConnectionStrings__DefaultConnection="Server=...;Database=QuanLyDatPhong;..."

# JWT
JWT_SECRET="your_super_secret_key_here"

# VNPay
VNPAY_TMN_CODE="WSAUQG18"
VNPAY_HASH_SECRET="DGF1EWCJ0F6RRW7XUWVO5G3FG0GYSV2E"
VNPAY_URL="https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"
VNPAY_RETURN_URL="http://localhost:5000/api/payments/vnpay-return"

# Firebase (for production)
FIREBASE_PROJECT_ID="your-project-id"
FIREBASE_SERVICE_ACCOUNT_PATH="config/serviceAccount.json"
```

---

## ✅ **FEATURES IMPLEMENTED**

### **✅ Authentication:**
- [x] Firebase Authentication integration
- [x] JWT token management
- [x] Role-based authorization (Admin/ChuCoSo/KhachHang)
- [x] Development login for testing

### **✅ Accommodation Management:**
- [x] Create accommodation with address auto-creation
- [x] Address deduplication
- [x] Image upload & management
- [x] Approval workflow (ChoDuyet → DaDuyet)
- [x] Owner validation

### **✅ Room Management:**
- [x] Room creation with images
- [x] Owner validation
- [x] Price and inventory management
- [x] Room type integration

### **✅ Booking System:**
- [x] Date validation
- [x] Availability checking
- [x] Price calculation
- [x] Status management
- [x] User-specific bookings

### **✅ Payment Integration:**
- [x] VNPay sandbox integration
- [x] Multiple payment types (cọc/đầy đủ/bổ sung)
- [x] Secure signature generation
- [x] Callback handling (return + IPN)
- [x] Transaction status tracking
- [x] Refund support

### **✅ Additional Features:**
- [x] File upload with validation
- [x] Error handling & logging
- [x] CORS configuration
- [x] API documentation
- [x] Reference data APIs
- [x] Bank integration support

---

## 🎯 **READY FOR PRODUCTION**

Hệ thống đã sẵn sàng cho production với:
- ✅ **Complete Business Flow** từ đăng nhập đến thanh toán
- ✅ **Security** với JWT + role-based access
- ✅ **Payment Gateway** VNPay production-ready  
- ✅ **File Management** với validation đầy đủ
- ✅ **Database Design** tối ưu và scalable
- ✅ **Error Handling** comprehensive
- ✅ **API Documentation** chi tiết
- ✅ **Testing Tools** automated + manual

**🚀 Khởi chạy server và bắt đầu test ngay!**

```bash
dotnet run --project HotelBooking.Api.csproj
# Server running at http://localhost:5000
# Open FULL_FLOW_TEST.html to test complete flow
```
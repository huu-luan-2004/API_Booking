# API QUẢN LÝ KHÁCH SạN CHO CHỦ CƠ SỞ

## 🔗 **BASE URL**
```
http://localhost:5000
```

## 🛡️ **AUTHENTICATION**
Tất cả API có `Authorization: Bearer <token>` yêu cầu JWT token trong header.

## 🛡️ **PHÂN QUYỀN**
- **Admin:** Toàn quyền
- **ChuCoSo:** Quản lý cơ sở và phòng của mình
- **KhachHang:** Đặt phòng, xem thông tin cá nhân
- **Public:** Xem cơ sở và phòng đã được duyệt

---

## 🏢 **1. QUẢN LÝ CƠ SỞ LƯU TRÚ** `/api/cosoluutru`

### **Xem danh sách cơ sở**
```http
GET /api/cosoluutru?page=1&pageSize=20&q=search&ownerId=123
```
- **Quyền:** Public (đã duyệt), ChuCoSo (của mình), Admin (tất cả)
- **Chức năng:** Lấy danh sách cơ sở lưu trú với phân trang và tìm kiếm

### **Xem chi tiết cơ sở**
```http
GET /api/cosoluutru/{id}
```

### **Tạo cơ sở mới**
```http
POST /api/cosoluutru
Content-Type: multipart/form-data
Authorization: Bearer <token>

Form data:
- tenCoSo: string (bắt buộc)
- moTa: string
- soTaiKhoan: string
- tenTaiKhoan: string
- tenNganHang: string
- soNha: string
- phuong: string
- quan: string  
- thanhPho: string (bắt buộc)
- kinhDo: double (tọa độ kinh độ)
- viDo: double (tọa độ vĩ độ)
- file: image (optional)
```

**Lưu ý:** API sẽ tự động tạo địa chỉ mới trong bảng `DiaChiChiTiet` và liên kết với cơ sở lưu trú.

### **Cập nhật cơ sở**
```http
PUT /api/cosoluutru/{id}
Content-Type: multipart/form-data
Authorization: Bearer <token>

Form data:
- tenCoSo: string
- moTa: string
- soTaiKhoan: string
- tenTaiKhoan: string
- tenNganHang: string
- idDiaChi: int
- file: image (optional)
```

### **Xóa cơ sở**
```http
DELETE /api/cosoluutru/{id}
Authorization: Bearer <token>
```

### **Duyệt cơ sở (Admin only)**
```http
PATCH /api/cosoluutru/{id}/approve
Authorization: Bearer <admin_token>
```

### **Từ chối cơ sở (Admin only)**
```http
PATCH /api/cosoluutru/{id}/reject
Content-Type: application/json
Authorization: Bearer <admin_token>

{
  "lyDo": "Lý do từ chối"
}
```

### Reverse Geocoding (OpenStreetMap) cho form địa chỉ
```http
GET /api/cosoluutru/reverse-geocode?lat=21.0278&lng=105.8342
# hoặc
GET /api/cosoluutru/reverse-geocode?viDo=21.0278&kinhDo=105.8342
```

• Input: chấp nhận cặp tham số `lat/lng` hoặc `viDo/kinhDo` (dấu phẩy thập phân cũng được).  
• Output: địa chỉ thân thiện để auto-fill form. Trả về 200 luôn, với `success=false` khi không tìm thấy chi tiết.

Response (ví dụ):
```json
{
  "success": true,
  "data": {
    "chiTiet": null,
    "pho": "Phố Tràng Tiền",
    "thon": "Thôn Trung",
    "phuong": "Phường Tràng Tiền", 
    "thanhPho": "Hà Nội",
    "tinhThanh": "Hà Nội",
    "nuoc": "Việt Nam",
    "displayName": "Phố Tràng Tiền, Hoàn Kiếm, Hà Nội, Việt Nam",
    "viDo": 21.0278,
    "kinhDo": 105.8342
  }
}
```

Ghi chú ánh xạ trường:
- `phuong` (xã/phường/thị trấn) ưu tiên: `suburb → town → village → hamlet → neighbourhood → quarter → municipality → city_district`.
- `thon` (thôn/xóm/ấp/tổ): ưu tiên `hamlet`, nếu không có thì `neighbourhood → quarter`.
- `thanhPho` và alias `tinhThanh` lấy từ `city` hoặc `province/state` (chỉ để hiển thị, chưa lưu DB).
- `chiTiet` luôn để trống để người dùng nhập số nhà/tòa nhà thủ công.

Snippet frontend (JS thuần):
```javascript
async function autofillAddressFromMap(lat, lng) {
  const url = `http://localhost:5000/api/cosoluutru/reverse-geocode?lat=${lat}&lng=${lng}`;
  const res = await fetch(url);
  const json = await res.json();
  if (!json.success) return;
  const d = json.data;
  document.querySelector('#chiTiet').value = d.chiTiet ?? '';
  document.querySelector('#pho').value = d.pho ?? '';
  // Nếu là vùng nông thôn, có thể dùng d.thon để hiển thị hoặc gán vào pho khi không có tên đường
  if (!d.pho && d.thon) document.querySelector('#pho').value = d.thon;
  document.querySelector('#phuong').value = d.phuong ?? '';
  document.querySelector('#thanhPho').value = d.thanhPho ?? d.tinhThanh ?? '';
  document.querySelector('#nuoc').value = d.nuoc ?? 'Việt Nam';
  document.querySelector('#viDo').value = d.viDo ?? '';
  document.querySelector('#kinhDo').value = d.kinhDo ?? '';
}
```

Form create/update: có thể gửi thêm trường `thon`; backend sẽ dùng `thon` làm alias cho `pho` nếu `pho` trống.

---

## 🏠 **2. QUẢN LÝ PHÒNG** `/api/rooms`

### **Danh sách phòng**
```http
GET /api/rooms?page=1&pageSize=20&coSoId=123&available=true&checkin=2024-01-01&checkout=2024-01-02
```
- **Filter:** Theo cơ sở, tình trạng, ngày trống

### **Chi tiết phòng**
```http
GET /api/rooms/{id}
```

### **Tạo phòng mới**
```http
POST /api/rooms
Content-Type: multipart/form-data
Authorization: Bearer <token>

Form data:
- tenPhong: string
- idCoSoLuuTru: int
- loaiPhong: string
- giaPhong: decimal
- moTa: string
- file: image (optional)
```

Ghi chú:
- Trường giá có thể gửi bằng khóa giaPhong hoặc gia (cả hai đều được chấp nhận).
- Nếu gửi giá khi tạo, hệ thống sẽ tạo một bản ghi trong bảng GiaPhong với NgayApDung = thời điểm hiện tại.

### **Cập nhật thông tin phòng**
```http
PUT /api/rooms/{id}
Content-Type: application/json
Authorization: Bearer <token>

{
  "tenPhong": "string",
  "loaiPhong": "string", 
  "giaPhong": decimal,
  "moTa": "string"
}
```

Ghi chú:
- Trường giá có thể gửi bằng khóa giaPhong hoặc gia (cả hai đều được chấp nhận).
- Khi cung cấp giá, API sẽ KHÔNG cập nhật giá tại chỗ mà sẽ chèn THÊM một dòng lịch sử vào bảng GiaPhong (giữ lịch sử giá). Các API GET sẽ luôn trả về giá mới nhất cùng NgayApDungGia.

### **Upload/Cập nhật ảnh phòng**
```http
PUT /api/rooms/{id}/image
Content-Type: multipart/form-data
Authorization: Bearer <token>

Form data:
- file: image
```

### **Xóa phòng**
```http
DELETE /api/rooms/{id}
Authorization: Bearer <token>
```

### **Xóa ảnh phòng**
```http
DELETE /api/rooms/{id}/image
Authorization: Bearer <token>
```

---

## 📅 **3. QUẢN LÝ ĐẶT PHÒNG** `/api/bookings`

### **Tạo đặt phòng**
```http
POST /api/bookings
Content-Type: application/json
Authorization: Bearer <token>

{
  "idPhong": int,
  "ngayNhanPhong": "2024-01-01",
  "ngayTraPhong": "2024-01-02", 
  "tongTien": decimal
}
```

### **Danh sách đặt phòng của user**
```http
GET /api/bookings/user
Authorization: Bearer <token>
```

### **Chi tiết đặt phòng**
```http
GET /api/bookings/{id}
Authorization: Bearer <token>
```

### **Kiểm tra tình trạng phòng**
```http
GET /api/bookings/check-availability?idPhong=123&ngayNhanPhong=2024-01-01&ngayTraPhong=2024-01-02
```

---

## 💳 **4. QUẢN LÝ THANH TOÁN** `/api/payments`

### **Danh sách thanh toán**
```http
GET /api/payments
Authorization: Bearer <token>
```

### **Tạo thanh toán VNPay**
```http
POST /api/payments/create-vnpay-payment
Content-Type: application/json
Authorization: Bearer <token>

{
  "idDatPhong": int,
  "amount": decimal,
  "orderInfo": "string"
}
```

### **VNPay callback**
```http
GET /api/payments/vnpay-return?[vnpay_params]
GET /api/payments/vnpay-ipn?[vnpay_params]
```

### **Tạo hoàn tiền**
```http
POST /api/payments/create-refund
Content-Type: application/json
Authorization: Bearer <token>

{
  "idDatPhong": int,
  "lyDoHuy": "string"
}
```

### **Xác nhận hoàn tiền**
```http
POST /api/payments/confirm-refund
Authorization: Bearer <token>

{
  "idHuyDatPhong": int
}
```

---

## 🎯 **5. QUẢN LÝ KHUYẾN MÃI** `/api/promotions`

### **Xem khuyến mãi phòng**
```http
GET /api/promotions/rooms/{id}/preview
```

---

## 👤 **6. QUẢN LÝ NGƯỜI DÙNG** `/api/user`

### **Thông tin cá nhân**
```http
GET /api/user/me
Authorization: Bearer <token>
```

### **Danh sách người dùng**
```http
GET /api/user?page=1&pageSize=20&q=search
```

### **Cập nhật thông tin**
```http
PUT /api/user/profile
Content-Type: application/json
Authorization: Bearer <token>

{
  "hoTen": "string",
  "soDienThoai": "string"
}
```

### **Upload avatar**
```http
PUT /api/user/avatar
Content-Type: multipart/form-data
Authorization: Bearer <token>

Form data:
- file: image
```

### **Xóa avatar**
```http
DELETE /api/user/avatar
Authorization: Bearer <token>
```

### **Cập nhật vai trò (Admin only)**
```http
PATCH /api/user/{id}/role
Content-Type: application/json
Authorization: Bearer <admin_token>

{
  "vaiTro": "Admin|ChuCoSo|KhachHang"
}
```

---

## 🔐 **10. XÁC THỰC** `/api/auth`

### **Đăng nhập**
```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "string",
  "password": "string"
}
```

### **Đăng ký**
```http
POST /api/auth/register
Content-Type: application/json

{
  "email": "string", 
  "password": "string",
  "hoTen": "string",
  "soDienThoai": "string"
}
```

---

## �️ **8. OPENSTREETMAP INTEGRATION** `/api/maps`

### **Geocoding - Chuyển địa chỉ thành tọa độ**
```http
GET /api/maps/geocode?address=123%20Phường%20Trần%20Hưng%20Đạo,%20Quận%20Hoàn%20Kiếm,%20Hà%20Nội
```

**Response:**
```json
{
  "success": true,
  "data": {
    "latitude": 21.028511,
    "longitude": 105.804817,
    "displayName": "123, Phường Trần Hưng Đạo, Quận Hoàn Kiếm, Hà Nội, Việt Nam",
    "boundingBox": ["21.0285", "21.0286", "105.8048", "105.8049"]
  },
  "message": "Geocoding thành công"
}
```

### **Reverse Geocoding - Chuyển tọa độ thành địa chỉ**
```http
GET /api/maps/reverse-geocode?lat=21.028511&lon=105.804817
```

**Response:**
```json
{
  "success": true,
  "data": {
    "latitude": 21.028511,
    "longitude": 105.804817,
    "displayName": "123, Phường Trần Hưng Đạo, Quận Hoàn Kiếm, Hà Nội, Việt Nam",
    "address": {
      "houseNumber": "123",
      "road": "Phố Trần Hưng Đạo",
      "quarter": "Phường Trần Hưng Đạo",
      "cityDistrict": "Quận Hoàn Kiếm",
      "city": "Hà Nội",
      "province": "Hà Nội",
      "country": "Việt Nam"
    }
  },
  "message": "Reverse geocoding thành công"
}
```

### **Tìm địa điểm gần nhất**
```http
GET /api/maps/nearby?lat=21.028511&lon=105.804817&category=restaurant&radius=1000
```

**Parameters:**
- `lat`, `lon`: Tọa độ trung tâm
- `category`: Loại địa điểm (restaurant, bank, hospital, school, shop, etc.)
- `radius`: Bán kính tìm kiếm (mét, max 50000)

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "name": "Nhà hàng ABC",
      "latitude": 21.029,
      "longitude": 105.805,
      "distance": 150.5,
      "type": "restaurant",
      "class": "amenity"
    }
  ],
  "total": 1,
  "message": "Tìm thấy 1 địa điểm trong bán kính 1000m"
}
```

### **Lấy danh sách cơ sở lưu trú cho map**
```http
GET /api/maps/accommodations?status=DaDuyet
```

**Parameters:**
- `status`: Trạng thái (DaDuyet, ChoDuyet, hoặc bỏ trống để lấy tất cả)

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "tenCoSo": "Hotel ABC",
      "latitude": 21.028511,
      "longitude": 105.804817,
      "diaChi": "123, Phường Trần Hưng Đạo, Quận Hoàn Kiếm, Hà Nội",
      "trangThaiDuyet": "DaDuyet",
      "anhUrl": "/api/static/uploads/accommodations/hotel1.jpg",
      "moTa": "Khách sạn 3 sao với đầy đủ tiện nghi"
    }
  ],
  "total": 1,
  "message": "Lấy danh sách cơ sở lưu trú thành công"
}
```

### **Tính khoảng cách giữa 2 điểm**
```http
GET /api/maps/distance?lat1=21.028511&lon1=105.804817&lat2=21.0245&lon2=105.8412
```

**Response:**
```json
{
  "success": true,
  "data": {
    "distance": 4236.84,
    "unit": "meters",
    "distanceKm": 4.24,
    "from": { "latitude": 21.028511, "longitude": 105.804817 },
    "to": { "latitude": 21.0245, "longitude": 105.8412 }
  },
  "message": "Tính khoảng cách thành công"
}
```

### **Gợi ý địa chỉ (Autocomplete)**
```http
GET /api/maps/suggest?query=Trần%20Hưng&limit=5
```

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "displayName": "Phố Trần Hưng Đạo, Quận Hoàn Kiếm, Hà Nội",
      "latitude": 21.028511,
      "longitude": 105.804817,
      "type": "highway",
      "class": "primary"
    }
  ],
  "total": 1,
  "message": "Tìm thấy 1 gợi ý"
}
```

---

## �🖼️ **9. QUẢN LÝ FILE TĨNH** `/api/static`

### **Xem ảnh avatar**
```http
GET /api/static/avatars/{fileName}
```

### **Xem file upload**
```http
GET /api/static/uploads/{category}/{fileName}
```
- **category:** avatars, rooms, accommodations

---

## 📋 **EXAMPLES**

### **JavaScript Frontend Usage**

#### **1. Đăng nhập và lấy token**
```javascript
// Đăng nhập
const loginResponse = await fetch('http://localhost:5000/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
        email: 'user@example.com',
        password: 'password123'
    })
});
const { data } = await loginResponse.json();
const token = data.accessToken;

// Lưu token
localStorage.setItem('token', token);
```

#### **2. Lấy thông tin người dùng**
```javascript
const userResponse = await fetch('http://localhost:5000/api/user/me', {
    headers: {
        'Authorization': `Bearer ${localStorage.getItem('token')}`
    }
});
const userData = await userResponse.json();
```

#### **3. Upload avatar**
```javascript
const uploadAvatar = async (file) => {
    const formData = new FormData();
    formData.append('file', file);
    
    const response = await fetch('http://localhost:5000/api/user/avatar', {
        method: 'PUT',
        headers: {
            'Authorization': `Bearer ${localStorage.getItem('token')}`
        },
        body: formData
    });
    
    return await response.json();
};
```

#### **4. Tạo cơ sở lưu trú mới**
```javascript
const createAccommodation = async (accommodationData, imageFile) => {
    const formData = new FormData();
    formData.append('tenCoSo', accommodationData.tenCoSo);
    formData.append('moTa', accommodationData.moTa);
    formData.append('soTaiKhoan', accommodationData.soTaiKhoan);
    formData.append('tenTaiKhoan', accommodationData.tenTaiKhoan);
    formData.append('tenNganHang', accommodationData.tenNganHang);
    formData.append('idDiaChi', accommodationData.idDiaChi);
    if (imageFile) formData.append('file', imageFile);
    
    const response = await fetch('http://localhost:5000/api/cosoluutru', {
        method: 'POST',
        headers: {
            'Authorization': `Bearer ${localStorage.getItem('token')}`
        },
        body: formData
    });
    
    return await response.json();
};

// Cập nhật cơ sở lưu trú
const updateAccommodation = async (id, accommodationData, imageFile) => {
    const formData = new FormData();
    formData.append('tenCoSo', accommodationData.tenCoSo);
    formData.append('moTa', accommodationData.moTa);
    formData.append('soTaiKhoan', accommodationData.soTaiKhoan);
    formData.append('tenTaiKhoan', accommodationData.tenTaiKhoan);
    formData.append('tenNganHang', accommodationData.tenNganHang);
    formData.append('idDiaChi', accommodationData.idDiaChi);
    if (imageFile) formData.append('file', imageFile);
    
    const response = await fetch(`http://localhost:5000/api/cosoluutru/${id}`, {
        method: 'PUT',
        headers: {
            'Authorization': `Bearer ${localStorage.getItem('token')}`
        },
        body: formData
    });
    
    return await response.json();
};

// Xóa cơ sở lưu trú
const deleteAccommodation = async (id) => {
    const response = await fetch(`http://localhost:5000/api/cosoluutru/${id}`, {
        method: 'DELETE',
        headers: {
            'Authorization': `Bearer ${localStorage.getItem('token')}`
        }
    });
    
    return await response.json();
};
```

#### **5. Lấy danh sách phòng**
```javascript
const getRooms = async (coSoId, page = 1, pageSize = 20) => {
    const response = await fetch(
        `http://localhost:5000/api/rooms?coSoId=${coSoId}&page=${page}&pageSize=${pageSize}`
    );
    return await response.json();
};
```

#### **6. Tạo phòng mới**
```javascript
const createRoom = async (roomData, imageFile) => {
    const formData = new FormData();
    formData.append('tenPhong', roomData.tenPhong);
    formData.append('idCoSoLuuTru', roomData.idCoSoLuuTru);
    formData.append('loaiPhong', roomData.loaiPhong);
    formData.append('giaPhong', roomData.giaPhong);
    formData.append('moTa', roomData.moTa);
    if (imageFile) formData.append('file', imageFile);
    
    const response = await fetch('http://localhost:5000/api/rooms', {
        method: 'POST',
        headers: {
            'Authorization': `Bearer ${localStorage.getItem('token')}`
        },
        body: formData
    });
    
    return await response.json();
};
```

#### **7. Đặt phòng**
```javascript
const bookRoom = async (idPhong, ngayNhanPhong, ngayTraPhong, tongTien) => {
    const response = await fetch('http://localhost:5000/api/bookings', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('token')}`
        },
        body: JSON.stringify({
            idPhong,
            ngayNhanPhong,
            ngayTraPhong,
            tongTien
        })
    });
    
    return await response.json();
};
```

---

## 🚀 **Quick Start cho Frontend**

### **1. Setup API Service**
```javascript
class HotelAPI {
    constructor() {
        this.baseURL = 'http://localhost:5000';
        this.token = localStorage.getItem('token');
    }
    
    setToken(token) {
        this.token = token;
        localStorage.setItem('token', token);
    }
    
    async request(endpoint, options = {}) {
        const url = `${this.baseURL}${endpoint}`;
        const config = {
            headers: {
                ...options.headers,
                ...(this.token && { 'Authorization': `Bearer ${this.token}` })
            },
            ...options
        };
        
        const response = await fetch(url, config);
        return await response.json();
    }
    
    // Auth
    async login(email, password) {
        return this.request('/api/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email, password })
        });
    }
    
    // User
    async getMe() {
        return this.request('/api/user/me');
    }
    
    // Rooms
    async getRooms(params = {}) {
        const query = new URLSearchParams(params).toString();
        return this.request(`/api/rooms${query ? '?' + query : ''}`);
    }
    
    // Accommodations với địa chỉ
    async createAccommodationWithAddress(data, imageFile = null) {
        const formData = new FormData();
        
        // Thông tin cơ sở
        formData.append('tenCoSo', data.tenCoSo);
        formData.append('moTa', data.moTa || '');
        formData.append('soTaiKhoan', data.soTaiKhoan || '');
        formData.append('tenTaiKhoan', data.tenTaiKhoan || '');
        formData.append('tenNganHang', data.tenNganHang || '');
        
        // Thông tin địa chỉ
        formData.append('soNha', data.soNha || '');
        formData.append('phuong', data.phuong || '');
        formData.append('quan', data.quan || '');
        formData.append('thanhPho', data.thanhPho);
        
        if (data.kinhDo) formData.append('kinhDo', data.kinhDo.toString());
        if (data.viDo) formData.append('viDo', data.viDo.toString());
        
        // Ảnh (nếu có)
        if (imageFile) {
            formData.append('file', imageFile);
        }
        
        return this.request('/api/cosoluutru', {
            method: 'POST',
            body: formData
        });
    }

    // Bookings
    async createBooking(bookingData) {
        return this.request('/api/bookings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(bookingData)
        });
    }
}

// Sử dụng
const api = new HotelAPI();
```

### **2. Usage Example**
```javascript
// Đăng nhập
const loginResult = await api.login('user@example.com', 'password');
if (loginResult.success) {
    api.setToken(loginResult.data.accessToken);
}

// Lấy thông tin user
const user = await api.getMe();

// Lấy danh sách phòng
const rooms = await api.getRooms({ coSoId: 1, page: 1 });

// Tạo cơ sở lưu trú với địa chỉ mới
const accommodationData = {
    tenCoSo: 'Hotel ABC',
    moTa: 'Khách sạn 3 sao tại trung tâm thành phố',
    soTaiKhoan: '1234567890',
    tenTaiKhoan: 'Nguyen Van A',  
    tenNganHang: 'Vietcombank',
    soNha: '123',
    phuong: 'Phường Trần Hưng Đạo',
    quan: 'Quận Hoàn Kiếm',
    thanhPho: 'Hà Nội',
    kinhDo: 21.028511,
    viDo: 105.804817
};

const imageFile = document.getElementById('imageInput').files[0];
const accommodation = await api.createAccommodationWithAddress(accommodationData, imageFile);

// Đặt phòng
const booking = await api.createBooking({
    idPhong: 1,
    ngayNhanPhong: '2024-01-01',
    ngayTraPhong: '2024-01-02',
    tongTien: 1000000
});
```

## 📋 Reference Data APIs

### **Lấy danh sách loại phòng**
```http
GET /api/reference/room-types
```

### **Lấy danh sách tỉnh/thành phố**
```http
GET /api/reference/provinces
```

### **Lấy trạng thái cơ sở lưu trú**
```http
GET /api/reference/accommodation-statuses
```

### **Quản lý địa chỉ chi tiết**

#### Lấy danh sách địa chỉ
```http
GET /api/reference/addresses
```

#### Tạo địa chỉ mới
```http
POST /api/reference/addresses
Content-Type: application/json

{
  "soNha": "123",
  "phuong": "Phường Trần Hưng Đạo", 
  "quan": "Quận Hoàn Kiếm",
  "thanhPho": "Hà Nội",
  "kenhDo": 21.028511,
  "viDo": 105.804817
}
```

#### Cập nhật địa chỉ
```http
PUT /api/reference/addresses/{id}
Content-Type: application/json

{
  "soNha": "456",
  "phuong": "Phường Tràng Tiền",
  "quan": "Quận Hoàn Kiếm", 
  "thanhPho": "Hà Nội",
  "kenhDo": 21.028511,
  "viDo": 105.804817
}
```

#### Xóa địa chỉ
```http
DELETE /api/reference/addresses/{id}
```

### **Lấy dữ liệu địa chỉ theo cấp**

#### Lấy danh sách tỉnh/thành phố
```http
GET /api/reference/cities
```

#### Lấy danh sách quận/huyện
```http
GET /api/reference/districts
GET /api/reference/districts?thanhPho=Hà Nội
```

#### Lấy danh sách phường/xã
```http
GET /api/reference/wards
GET /api/reference/wards?thanhPho=Hà Nội
GET /api/reference/wards?thanhPho=Hà Nội&quan=Quận Hoàn Kiếm
```

### **Lấy danh sách ngân hàng**
```http
GET /api/reference/banks
```

## 🔧 JavaScript Helper cho Dropdown Địa chỉ

```javascript
class AddressSelector {
  constructor(baseUrl = 'https://your-api.com/api/reference') {
    this.baseUrl = baseUrl;
  }
  
  async loadCities(selectElement) {
    try {
      const response = await fetch(`${this.baseUrl}/cities`);
      const result = await response.json();
      
      selectElement.innerHTML = '<option value="">Chọn tỉnh/thành phố</option>';
      result.data.forEach(item => {
        selectElement.innerHTML += `<option value="${item.ThanhPho}">${item.ThanhPho}</option>`;
      });
    } catch (error) {
      console.error('Lỗi tải danh sách thành phố:', error);
    }
  }
  
  async loadDistricts(selectElement, thanhPho) {
    const url = thanhPho ? 
      `${this.baseUrl}/districts?thanhPho=${encodeURIComponent(thanhPho)}` : 
      `${this.baseUrl}/districts`;
    
    const response = await fetch(url);
    const result = await response.json();
    
    selectElement.innerHTML = '<option value="">Chọn quận/huyện</option>';
    result.data.forEach(item => {
      selectElement.innerHTML += `<option value="${item.Quan}">${item.Quan}</option>`;
    });
  }
  
  async loadWards(selectElement, thanhPho, quan) {
    const params = new URLSearchParams();
    if (thanhPho) params.append('thanhPho', thanhPho);
    if (quan) params.append('quan', quan);
    
    const url = `${this.baseUrl}/wards${params.toString() ? '?' + params.toString() : ''}`;
    const response = await fetch(url);
    const result = await response.json();
    
    selectElement.innerHTML = '<option value="">Chọn phường/xã</option>';
    result.data.forEach(item => {
      selectElement.innerHTML += `<option value="${item.Phuong}">${item.Phuong}</option>`;
    });
  }
  
  async createAddress(addressData) {
    const response = await fetch(`${this.baseUrl}/addresses`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(addressData)
    });
    return await response.json();
  }
}

// Sử dụng Address Selector
const addressSelector = new AddressSelector();
const citySelect = document.getElementById('citySelect');
const districtSelect = document.getElementById('districtSelect');
const wardSelect = document.getElementById('wardSelect');

// Load cities khi trang load
addressSelector.loadCities(citySelect);

// Cascade dropdown
citySelect.addEventListener('change', (e) => {
  const selectedCity = e.target.value;
  if (selectedCity) {
    addressSelector.loadDistricts(districtSelect, selectedCity);
    wardSelect.innerHTML = '<option value="">Chọn phường/xã</option>';
  }
});

districtSelect.addEventListener('change', (e) => {
  const selectedDistrict = e.target.value;
  const selectedCity = citySelect.value;
  if (selectedDistrict && selectedCity) {
    addressSelector.loadWards(wardSelect, selectedCity, selectedDistrict);
  }
});
```

Đây là bộ API documentation hoàn chỉnh để bạn phát triển frontend! 🎉
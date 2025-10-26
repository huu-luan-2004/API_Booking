# 🏢 CHỦ CƠ SỞ FRONTEND SPECIFICATION

## 🎯 **Mục tiêu**
Giao diện quản lý cho chủ cơ sở lưu trú, tập trung vào quản lý cơ sở và phòng của riêng mình

---

## 🚀 **Các chức năng chính**

### **1. 📊 DASHBOARD CỦA TÔI**
```
📈 Thống kê cá nhân:
- Số cơ sở của tôi (đã duyệt/chờ duyệt/từ chối)
- Tổng số phòng đã tạo
- Số đặt phòng tháng này
- Doanh thu tháng này từ phòng của tôi
- Biểu đồ đặt phòng 30 ngày qua
- Top phòng được đặt nhiều nhất

🔔 Thông báo quan trọng:
- Cơ sở mới được duyệt/từ chối
- Đặt phòng mới
- Thanh toán hoàn tất
- Yêu cầu hủy phòng
```

**API cần dùng:**
```http
GET /api/cosoluutru?ownerId={userId}  # Cơ sở của tôi
GET /api/rooms?coSoId={myAccommodationId}  # Phòng của tôi
GET /api/bookings/accommodation/{id}  # Đặt phòng cơ sở tôi
GET /api/payments?accommodationOwner={userId}  # Doanh thu
```

### **2. 🏢 QUẢN LÝ CƠ SỞ CỦA TÔI**
```
📋 Danh sách cơ sở:
- Chỉ hiển thị cơ sở của chủ sở hữu hiện tại
- Trạng thái: ChoDuyet (vàng), DaDuyet (xanh), TuChoi (đỏ)
- Xem chi tiết + địa chỉ + tọa độ map
- Số phòng trong mỗi cơ sở

⚡ Thao tác:
- Tạo cơ sở mới (với auto-geocoding)
- Chỉnh sửa thông tin cơ sở
- Upload/thay đổi ảnh cơ sở  
- Xem lý do từ chối (nếu có)
- Gửi lại để duyệt sau khi sửa
```

**API cần dùng:**
```http
GET /api/cosoluutru?ownerId={userId}  # Cơ sở của tôi
POST /api/cosoluutru  # Tạo mới
PUT /api/cosoluutru/{id}  # Cập nhật
GET /api/cosoluutru/{id}  # Chi tiết
GET /api/maps/geocode  # Auto-geocoding địa chỉ
```

### **3. 🏠 QUẢN LÝ PHÒNG**
```
📋 Danh sách phòng:
- Hiển thị phòng của tất cả cơ sở thuộc sở hữu
- Filter theo cơ sở
- Filter theo trạng thái available/booked
- Xem lịch đặt phòng
- Giá phòng hiện tại

⚡ Thao tác:
- Thêm phòng mới vào cơ sở đã được duyệt
- Chỉnh sửa thông tin phòng
- Upload/thay đổi ảnh phòng
- Cập nhật giá phòng
- Tạm ngưng/kích hoạt phòng
- Xem calendar đặt phòng
```

**API cần dùng:**
```http
GET /api/rooms?coSoId={id}  # Phòng theo cơ sở
POST /api/rooms  # Tạo phòng mới
PUT /api/rooms/{id}  # Cập nhật phòng
PUT /api/rooms/{id}/image  # Upload ảnh
DELETE /api/rooms/{id}/image  # Xóa ảnh
GET /api/bookings/room/{id}  # Lịch đặt phòng
```

### **4. 📅 QUẢN LÝ ĐẶT PHÒNG**
```
📋 Đặt phòng của khách:
- Tất cả booking cho phòng của tôi
- Filter theo trạng thái: DangXuLy/DaXacNhan/DaHuy/HoanTat
- Filter theo cơ sở/phòng
- Thông tin khách hàng

⚡ Thao tác:
- Xác nhận đặt phòng
- Từ chối đặt phòng (với lý do)
- Xem chi tiết khách hàng
- In/export danh sách
- Gửi thông báo cho khách
```

**API cần dùng:**
```http
GET /api/bookings/accommodation/{accommodationId}  # Booking cơ sở tôi
GET /api/bookings/{id}  # Chi tiết booking
PUT /api/bookings/{id}/confirm  # Xác nhận
PUT /api/bookings/{id}/reject   # Từ chối
```

### **5. 💰 QUẢN LÝ DOANH THU**
```
📊 Báo cáo tài chính:
- Doanh thu theo tháng/quý/năm
- Phân tích theo từng cơ sở/phòng
- So sánh với kỳ trước
- Biểu đồ xu hướng

💳 Giao dịch:
- Lịch sử thanh toán của khách
- Trạng thái giao dịch VNPay
- Yêu cầu hoàn tiền
- Export báo cáo Excel
```

**API cần dùng:**
```http
GET /api/payments?accommodationOwner={userId}  # Thanh toán của tôi
GET /api/payments/revenue-report?ownerId={userId}  # Báo cáo doanh thu
```

### **6. 🗺️ BẢN ĐỒ CƠ SỞ**
```
🗺️ Map cá nhân:
- Hiển thị chỉ cơ sở của tôi trên map
- Click xem chi tiết + chỉnh sửa nhanh
- Xem địa điểm xung quanh (nhà hàng, ngân hàng...)
- Kiểm tra tọa độ GPS
```

**API cần dùng:**
```http
GET /api/maps/accommodations?ownerId={userId}  # Map cơ sở của tôi
GET /api/maps/nearby  # Tìm địa điểm gần
```

---

## 🎨 **UI/UX Design**

### **Layout Structure:**
```
┌─────────────────────────────────────────┐
│ 🏢 Tên chủ cơ sở      🔔 Thông báo      │
├─────────────────────────────────────────┤
│ 📊 Stats Cards (3 columns)             │
├─────────────────────────────────────────┤
│ 📈 Revenue Chart  │ 📅 Recent Bookings │
├─────────────────────────────────────────┤
│ 🏠 My Accommodations Quick View         │
└─────────────────────────────────────────┘

Sidebar Menu:
📊 Dashboard
🏢 Cơ sở của tôi
🏠 Phòng
📅 Đặt phòng
💰 Doanh thu  
🗺️ Bản đồ
👤 Hồ sơ
🚪 Đăng xuất
```

### **Color Scheme:**
```css
Primary: #059669 (Emerald)
Success: #16a34a (Green)
Warning: #f59e0b (Amber) 
Danger: #dc2626 (Red)
Info: #0284c7 (Sky)

Status Colors:
ChoDuyet: #f59e0b (Amber)
DaDuyet: #10b981 (Emerald)
TuChoi: #ef4444 (Red)
Available: #22c55e (Green)
Booked: #f97316 (Orange)
```

### **Components cần thiết:**
```jsx
// Shared Components
<OwnerLayout />
<StatsCard />
<AccommodationCard />
<RoomCard />
<BookingCard />
<StatusBadge />
<ImageUpload />
<MapView />
<Calendar />

// Page Components
<OwnerDashboard />
<MyAccommodations />
<AccommodationForm />
<MyRooms />
<RoomForm />  
<BookingManagement />
<RevenueReports />
<ProfileSettings />
```

---

## 📱 **Responsive Design**

### **Desktop (1200px+):**
- Full sidebar navigation
- 3 columns stats layout
- Grid view accommodations/rooms
- Side-by-side forms
- Large calendar view

### **Tablet (768-1199px):**
- Collapsible sidebar  
- 2 columns stats
- List view with large cards
- Full-width forms
- Compact calendar

### **Mobile (< 768px):**
- Bottom tab navigation
- 1 column layout
- Vertical card stack
- Mobile-optimized forms
- Swipe calendar

---

## 🔄 **Data Flow & State Management**

### **Authentication Flow:**
```javascript
1. Login với Firebase → Get JWT token
2. Verify role = "ChuCoSo"
3. Store token + user info
4. Load owner's accommodations
5. Redirect to dashboard
```

### **Global State:**
```javascript
{
  auth: {
    user: OwnerData,
    token: string,
    isAuthenticated: boolean
  },
  accommodations: {
    myAccommodations: Accommodation[],
    selectedAccommodation: Accommodation | null,
    loading: boolean
  },
  rooms: {
    myRooms: Room[],
    roomsByAccommodation: { [key: string]: Room[] },
    selectedRoom: Room | null
  },
  bookings: {
    myBookings: Booking[],
    pendingBookings: Booking[],
    filters: BookingFilters
  },
  revenue: {
    monthlyRevenue: RevenueData,
    revenueChart: ChartData,
    loading: boolean
  }
}
```

---

## 🔧 **Key Workflows**

### **1. Tạo cơ sở lưu trú mới:**
```javascript
1. Form nhập thông tin cơ sở
2. Upload ảnh đại diện
3. Nhập địa chỉ → Auto-geocoding  
4. Preview trên map
5. Submit → Chờ duyệt
6. Nhận thông báo kết quả duyệt
```

### **2. Thêm phòng vào cơ sở:**
```javascript
1. Chọn cơ sở đã được duyệt
2. Form thông tin phòng + giá
3. Upload ảnh phòng
4. Preview card phòng
5. Submit → Phòng available ngay
```

### **3. Xử lý đặt phòng:**
```javascript
1. Nhận thông báo đặt phòng mới
2. Xem thông tin khách + yêu cầu
3. Check calendar conflicts
4. Xác nhận/Từ chối
5. Khách nhận thông báo
6. Theo dõi thanh toán
```

### **4. Quản lý doanh thu:**
```javascript
1. Xem dashboard doanh thu
2. Filter theo thời gian/cơ sở
3. So sánh với kỳ trước
4. Export báo cáo
5. Theo dõi pending payments
```

---

## 📋 **Pages & Routes**

```javascript
/owner/dashboard              // Dashboard tổng quan
/owner/accommodations         // Cơ sở của tôi
/owner/accommodations/new     // Tạo cơ sở mới
/owner/accommodations/:id     // Chi tiết/sửa cơ sở
/owner/rooms                  // Tất cả phòng
/owner/rooms/new              // Thêm phòng mới
/owner/rooms/:id              // Chi tiết/sửa phòng
/owner/bookings               // Quản lý đặt phòng
/owner/bookings/:id           // Chi tiết booking
/owner/revenue                // Báo cáo doanh thu
/owner/map                    // Bản đồ cơ sở
/owner/profile                // Hồ sơ cá nhân
/owner/settings               // Cài đặt
```

---

## 🎯 **Critical Features**

### **Smart Notifications:**
```javascript
- Đặt phòng mới (real-time)
- Cơ sở được duyệt/từ chối
- Thanh toán hoàn tất
- Yêu cầu hủy phòng
- Deadline check-in sắp tới
```

### **Quick Actions:**
```javascript
- Xác nhận booking nhanh
- Upload ảnh drag & drop
- Copy link phòng để share
- Export booking list
- One-click contact khách
```

### **Mobile Optimizations:**
```javascript
- Touch-friendly buttons
- Swipe gestures
- Pull-to-refresh
- Offline mode basics
- Camera integration cho upload
```

---

## 🧪 **Testing Scenarios**

### **Happy Path:**
```
1. Đăng nhập → Xem dashboard
2. Tạo cơ sở mới → Chờ duyệt
3. Nhận duyệt → Thêm phòng
4. Nhận đặt phòng → Xác nhận
5. Theo dõi thanh toán
6. Xem báo cáo doanh thu
```

### **Edge Cases:**
```
- Cơ sở bị từ chối → Sửa và gửi lại
- Conflict booking → Từ chối có lý do
- Upload ảnh lỗi → Retry mechanism
- Network mất → Offline handling
- Session hết hạn → Auto re-auth
```

---

## 📊 **Performance Requirements**

```
Page Load: < 1.5s
Image Upload: < 5s
Form Submit: < 2s
Real-time Notifications: < 1s delay
Mobile Performance: > 90
SEO Score: > 85 (public pages)
```

---

## 💡 **Business Logic Notes**

### **Validation Rules:**
```
- Chỉ có thể thêm phòng vào cơ sở đã duyệt
- Không thể xóa cơ sở có phòng đang được đặt
- Giá phòng phải > 0
- Upload ảnh max 5MB, formats: jpg,png,webp
- Địa chỉ bắt buộc có tỉnh/thành phố
```

### **Business Flows:**
```
- Cơ sở mới → ChoDuyet → Cần admin duyệt
- Phòng mới → Available ngay lập tức
- Booking mới → Cần owner xác nhận
- Thanh toán → Auto qua VNPay
- Hoàn tiền → Cần admin approve
```

**🏢 Chủ cơ sở frontend tập trung vào quản lý hiệu quả tài sản và tối ưu hóa doanh thu!**
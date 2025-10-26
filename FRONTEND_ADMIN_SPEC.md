# 👨‍💼 ADMIN FRONTEND SPECIFICATION

## 🎯 **Mục tiêu**
Giao diện quản trị toàn hệ thống cho Admin với quyền cao nhất

---

## 🚀 **Các chức năng chính**

### **1. 📊 DASHBOARD TỔNG QUAN**
```
📈 Thống kê tổng quan:
- Tổng số cơ sở lưu trú (đã duyệt/chờ duyệt/từ chối)
- Tổng số phòng trong hệ thống
- Tổng số người dùng (Admin/ChuCoSo/KhachHang)
- Tổng số đặt phòng (thành công/đang xử lý/đã hủy)
- Doanh thu tổng qua VNPay
- Biểu đồ theo thời gian (ngày/tuần/tháng)
```

**API cần dùng:**
```http
GET /api/admin/dashboard-stats
GET /api/user?page=1&pageSize=1000  # Đếm users
GET /api/cosoluutru?page=1&pageSize=1000  # Đếm accommodations
GET /api/payments  # Thống kê thanh toán
```

### **2. 🏢 QUẢN LÝ CƠ SỞ LƯU TRÚ**
```
📋 Danh sách cơ sở:
- Hiển thị tất cả cơ sở (bao gồm chờ duyệt)
- Filter theo trạng thái: ChoDuyet/DaDuyet/TuChoi
- Filter theo chủ cơ sở
- Search theo tên cơ sở
- Xem chi tiết cơ sở + địa chỉ + tọa độ map

⚡ Thao tác:
- Duyệt cơ sở (approve)
- Từ chối cơ sở (reject với lý do)
- Xem lịch sử duyệt
- Export danh sách Excel/CSV
```

**API cần dùng:**
```http
GET /api/cosoluutru?includeUnapproved=true  # Lấy tất cả
GET /api/cosoluutru/{id}  # Chi tiết
PATCH /api/cosoluutru/{id}/approve  # Duyệt
PATCH /api/cosoluutru/{id}/reject   # Từ chối
GET /api/maps/accommodations  # Xem trên map
```

### **3. 👥 QUẢN LÝ NGƯỜI DÙNG**
```
📋 Danh sách users:
- Hiển thị tất cả users với vai trò
- Filter theo vai trò: Admin/ChuCoSo/KhachHang
- Search theo email, tên, số điện thoại
- Xem chi tiết hoạt động user

⚡ Thao tác:
- Thay đổi vai trò user
- Kích hoạt/vô hiệu hóa tài khoản
- Xem lịch sử đặt phòng của user
- Reset password (nếu cần)
```

**API cần dùng:**
```http
GET /api/user?page=1&pageSize=20&q=search  # Danh sách
GET /api/user/{id}  # Chi tiết user
PATCH /api/user/{id}/role  # Đổi vai trò
GET /api/bookings/user/{id}  # Lịch sử đặt phòng
```

### **4. 💳 QUẢN LÝ THANH TOÁN**
```
📋 Danh sách giao dịch:
- Tất cả giao dịch VNPay
- Filter theo trạng thái: success/pending/failed
- Filter theo loại: deposit/full/topup/refund
- Search theo mã giao dịch, mã đặt phòng
- Thống kê doanh thu theo khoảng thời gian

⚡ Thao tác:
- Xem chi tiết giao dịch
- Xử lý hoàn tiền thủ công
- Export báo cáo tài chính
- Tra cứu giao dịch VNPay
```

**API cần dùng:**
```http
GET /api/payments  # Tất cả giao dịch
GET /api/payments/{id}  # Chi tiết
POST /api/payments/confirm-refund  # Xử lý hoàn tiền
```

### **5. 🗺️ BẢN ĐỒ TỔNG QUAN**
```
🗺️ Map view:
- Hiển thị tất cả cơ sở lưu trú trên map
- Color coding theo trạng thái duyệt
- Click xem chi tiết + thao tác nhanh
- Tìm kiếm địa điểm gần nhất
- Xuất dữ liệu GPS
```

**API cần dùng:**
```http
GET /api/maps/accommodations?status=  # Lấy tất cả
GET /api/maps/nearby  # Tìm địa điểm gần
```

---

## 🎨 **UI/UX Design**

### **Layout Structure:**
```
┌─────────────────────────────────────────┐
│ 🏠 Admin Dashboard     🔔 Notifications │
├─────────────────────────────────────────┤
│ 📊 Stats Cards (4 columns)             │
├─────────────────────────────────────────┤
│ 📈 Revenue Chart  │ 📊 Status Chart    │
├─────────────────────────────────────────┤
│ 📋 Recent Activities Table              │
└─────────────────────────────────────────┘

Sidebar Menu:
📊 Dashboard
🏢 Cơ sở lưu trú  
👥 Người dùng
💳 Thanh toán
🗺️ Bản đồ
⚙️ Cài đặt
🚪 Đăng xuất
```

### **Color Scheme:**
```css
Primary: #2563eb (Blue)
Success: #16a34a (Green) 
Warning: #ea580c (Orange)
Danger: #dc2626 (Red)
Secondary: #64748b (Gray)

Status Colors:
ChoDuyet: #f59e0b (Yellow)
DaDuyet: #10b981 (Green)
TuChoi: #ef4444 (Red)
```

### **Components cần thiết:**
```jsx
// Shared Components
<AdminLayout />
<StatsCard />
<DataTable />
<SearchFilter />
<StatusBadge />
<ActionButton />
<Modal />
<Charts />

// Page Components  
<Dashboard />
<AccommodationList />
<AccommodationDetail />
<UserManagement />
<PaymentManagement />
<MapView />
```

---

## 📱 **Responsive Design**

### **Desktop (1200px+):**
- Full sidebar + main content
- 4 columns stats cards
- Large data tables với pagination
- Split view cho map

### **Tablet (768-1199px):**
- Collapsible sidebar
- 2 columns stats cards  
- Horizontal scroll tables
- Full width map

### **Mobile (< 768px):**
- Burger menu
- 1 column stats cards
- Vertical card layout thay table
- Mobile-optimized map controls

---

## 🔄 **Data Flow & State Management**

### **Authentication Flow:**
```javascript
1. Login với Firebase → Get JWT token
2. Verify role = "Admin" 
3. Store token + user info
4. Redirect to dashboard
5. Auto-refresh token khi hết hạn
```

### **Global State:**
```javascript
{
  auth: {
    user: UserData,
    token: string,
    isAuthenticated: boolean
  },
  dashboard: {
    stats: DashboardStats,
    loading: boolean,
    error: string
  },
  accommodations: {
    list: Accommodation[],
    filters: FilterState,
    pagination: PaginationState
  },
  users: {
    list: User[],
    selectedUser: User | null
  },
  payments: {
    transactions: Transaction[],
    summary: PaymentSummary
  }
}
```

---

## 🔧 **Technical Requirements**

### **Framework Suggestions:**
```
Frontend: React 18+ / Vue 3+ / Angular 15+
UI Library: Material-UI / Ant Design / Tailwind CSS
State: Redux Toolkit / Zustand / Pinia
Charts: Chart.js / Recharts / ApexCharts
Maps: Leaflet / OpenLayers
Tables: TanStack Table / Ant Table
HTTP: Axios / Fetch API
```

### **Key Features:**
```
✅ JWT Authentication
✅ Role-based routing
✅ Real-time notifications
✅ Data export (CSV/Excel)
✅ Responsive design
✅ Dark/Light theme
✅ Multi-language (VN/EN)
✅ Error boundaries
✅ Loading states
✅ Offline handling
```

---

## 📋 **Pages & Routes**

```javascript
/admin/dashboard          // Tổng quan
/admin/accommodations     // Quản lý cơ sở
/admin/accommodations/:id // Chi tiết cơ sở
/admin/users             // Quản lý người dùng
/admin/users/:id         // Chi tiết người dùng
/admin/payments          // Quản lý thanh toán
/admin/payments/:id      // Chi tiết giao dịch
/admin/map              // Bản đồ tổng quan
/admin/settings         // Cài đặt hệ thống
/admin/reports          // Báo cáo thống kê
```

---

## 🧪 **Testing Scenarios**

### **Critical User Flows:**
```
1. Đăng nhập admin → Xem dashboard
2. Duyệt cơ sở lưu trú mới
3. Từ chối cơ sở với lý do
4. Thay đổi vai trò user
5. Xem chi tiết giao dịch thanh toán
6. Xuất báo cáo doanh thu
7. Tìm kiếm cơ sở trên map
8. Xử lý hoàn tiền cho khách
```

### **Edge Cases:**
```
- Network offline handling
- Large dataset pagination
- Concurrent admin actions
- Real-time data updates
- Error state management
- Token expiration handling
```

---

## 📊 **Performance Targets**

```
Page Load: < 2s
API Response: < 500ms
Bundle Size: < 500KB gzipped
Lighthouse Score: > 90
Mobile Performance: > 85
Accessibility: WCAG AA
```

**🎯 Admin frontend cần làm việc hiệu quả với data lớn và cung cấp overview toàn diện cho quản trị viên!**
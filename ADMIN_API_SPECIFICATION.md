# 🔧 ADMIN API SPECIFICATION

## 🎯 **Tổng quan**
Danh sách đầy đủ các API dành riêng cho Admin trong hệ thống Hotel Booking

---

## 🔑 **Authentication**
```
Authorization: Bearer {jwt_token}
Role Required: "Admin"
Content-Type: application/json
```

---

## 📊 **1. DASHBOARD & STATISTICS APIs**

### **1.1 Dashboard Statistics**
```http
GET /api/admin/dashboard-stats
```
**Response:**
```json
{
  "success": true,
  "data": {
    "totalAccommodations": 156,
    "pendingApprovals": 23,
    "approvedAccommodations": 128,
    "rejectedAccommodations": 5,
    "totalRooms": 842,
    "totalUsers": 1250,
    "adminUsers": 3,
    "ownerUsers": 156,
    "customerUsers": 1091,
    "totalBookings": 2341,
    "successfulBookings": 2180,
    "cancelledBookings": 161,
    "totalRevenue": 15650000,
    "monthlyRevenue": 3250000,
    "weeklyRevenue": 850000,
    "dailyRevenue": 125000,
    "revenueChart": [
      { "date": "2025-10-01", "revenue": 125000, "bookings": 8 },
      { "date": "2025-10-02", "revenue": 180000, "bookings": 12 }
    ],
    "statusDistribution": {
      "ChoDuyet": 23,
      "DaDuyet": 128, 
      "TuChoi": 5
    }
  }
}
```

### **1.2 Revenue Analytics**
```http
GET /api/admin/revenue-analytics?period={daily|weekly|monthly|yearly}&startDate={date}&endDate={date}
```

### **1.3 User Growth Analytics**
```http
GET /api/admin/user-analytics?period={daily|weekly|monthly}&months={number}
```

---

## 🏢 **2. ACCOMMODATION MANAGEMENT APIs**

### **2.1 Get All Accommodations (Admin View)**
```http
GET /api/cosoluutru?includeUnapproved=true&status={ChoDuyet|DaDuyet|TuChoi}&ownerId={id}&page={page}&pageSize={size}
```
**Existing API - Admin có thể xem tất cả trạng thái**

### **2.2 Approve Accommodation**
```http
PATCH /api/cosoluutru/{id}/approve
```
**Response:**
```json
{
  "success": true,
  "message": "Đã duyệt cơ sở lưu trú",
  "data": {
    "id": 123,
    "tenCoSo": "Khách sạn ABC",
    "trangThaiDuyet": "DaDuyet",
    "ngayDuyet": "2025-10-20T10:30:00Z"
  }
}
```

### **2.3 Reject Accommodation**
```http
PATCH /api/cosoluutru/{id}/reject
```
**Body:**
```json
{
  "lyDo": "Thiếu giấy phép kinh doanh"
}
```

### **2.4 Accommodation Approval History**
```http
GET /api/admin/accommodations/approval-history?accommodationId={id}
```
**Response:**
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "accommodationId": 123,
      "action": "approve", 
      "reason": null,
      "adminId": 1,
      "adminName": "Admin User",
      "timestamp": "2025-10-20T10:30:00Z"
    }
  ]
}
```

---

## 👥 **3. USER MANAGEMENT APIs**

### **3.1 Get All Users**
```http
GET /api/user?role={Admin|ChuCoSo|KhachHang}&q={search_term}&page={page}&pageSize={size}
```
**Admin có thể xem tất cả users**

### **3.2 Create Owner Account**
```http
POST /api/admin/users
```
**Body:**
```json
{
  "email": "owner@example.com",
  "password": "password123", 
  "name": "Chủ khách sạn",
  "phone": "+84901234567",
  "role": "ChuCoSo"
}
```

### **3.3 Update User Role**
```http
PATCH /api/user/{id}/role
```
**Body:**
```json
{
  "vaiTro": "Admin"
}
```

### **3.4 User Activity Log**
```http
GET /api/admin/users/{id}/activity?days={number}
```
**Response:**
```json
{
  "success": true,
  "data": [
    {
      "timestamp": "2025-10-20T10:30:00Z",
      "action": "login",
      "ip": "192.168.1.1",
      "userAgent": "Mozilla/5.0..."
    },
    {
      "timestamp": "2025-10-20T11:00:00Z", 
      "action": "create_accommodation",
      "details": "Tạo cơ sở: Khách sạn ABC"
    }
  ]
}
```

### **3.5 Disable/Enable User Account**
```http
PATCH /api/admin/users/{id}/status
```
**Body:**
```json
{
  "active": false,
  "reason": "Vi phạm chính sách"
}
```

---

## 💳 **4. PAYMENT MANAGEMENT APIs**

### **4.1 Get All Transactions**
```http
GET /api/payments?status={success|pending|failed}&type={deposit|full|refund}&userId={id}&bookingId={id}&startDate={date}&endDate={date}&page={page}&pageSize={size}
```

### **4.2 Transaction Details**
```http
GET /api/payments/{transactionId}
```

### **4.3 Process Manual Refund**
```http
POST /api/admin/payments/manual-refund
```
**Body:**
```json
{
  "transactionId": "TXN_123456",
  "amount": 500000,
  "reason": "Khách yêu cầu hủy do bất khả kháng",
  "refundMethod": "bank_transfer"
}
```

### **4.4 Payment Statistics**
```http
GET /api/admin/payments/statistics?period={daily|weekly|monthly}&startDate={date}&endDate={date}
```

### **4.5 Revenue Report**
```http
GET /api/admin/payments/revenue-report?format={json|csv|excel}&startDate={date}&endDate={date}
```

---

## 📋 **5. BOOKING MANAGEMENT APIs**

### **5.1 Get All Bookings**
```http
GET /api/admin/bookings?status={DaXacNhan|ChoXacNhan|DaHuy|DaHoanThanh}&userId={id}&accommodationId={id}&startDate={date}&endDate={date}&page={page}&pageSize={size}
```

### **5.2 Booking Details with Full Info**
```http
GET /api/admin/bookings/{id}/details
```
**Response:**
```json
{
  "success": true,
  "data": {
    "booking": {
      "id": 123,
      "maDatPhong": "BOOK_123456",
      "trangThai": "DaXacNhan",
      "ngayDat": "2025-10-20",
      "ngayNhanPhong": "2025-10-25",
      "ngayTraPhong": "2025-10-27",
      "tongTien": 1500000
    },
    "user": {
      "id": 45,
      "hoTen": "Nguyen Van A",
      "email": "user@example.com",
      "soDienThoai": "+84901234567"
    },
    "accommodation": {
      "id": 78,
      "tenCoSo": "Khách sạn ABC",
      "diaChi": "123 Nguyen Trai, Q1, TP.HCM"
    },
    "room": {
      "id": 90,
      "tenPhong": "Deluxe Room",
      "loaiPhong": "Standard"
    },
    "payments": [
      {
        "id": "PAY_123",
        "soTien": 1500000,
        "trangThai": "success",
        "phuongThuc": "VNPay"
      }
    ]
  }
}
```

### **5.3 Cancel Booking (Admin Override)**
```http
POST /api/admin/bookings/{id}/cancel
```
**Body:**
```json
{
  "reason": "Vi phạm chính sách sử dụng",
  "refundAmount": 750000,
  "notifyUser": true
}
```

---

## 🗺️ **6. MAP & LOCATION APIs**

### **6.1 All Accommodations on Map**
```http
GET /api/maps/accommodations?status={ChoDuyet|DaDuyet|TuChoi}&includeAll=true
```
**Admin có thể xem tất cả trạng thái trên map**

### **6.2 Location Analytics**
```http
GET /api/admin/maps/analytics?region={area}&period={monthly}
```
**Response:**
```json
{
  "success": true,
  "data": {
    "regionStats": [
      {
        "province": "TP.HCM",
        "accommodationCount": 45,
        "bookingCount": 340,
        "revenue": 5200000
      }
    ],
    "hotspots": [
      {
        "lat": 10.7769,
        "lng": 106.7009,
        "density": 12,
        "revenue": 2100000
      }
    ]
  }
}
```

---

## 📊 **7. SYSTEM ADMINISTRATION APIs**

### **7.1 System Health Check**
```http
GET /api/admin/system/health
```
**Response:**
```json
{
  "success": true,
  "data": {
    "database": "healthy",
    "firebase": "healthy", 
    "vnpay": "healthy",
    "storage": "healthy",
    "uptime": "5 days, 14 hours",
    "version": "1.0.0"
  }
}
```

### **7.2 Database Statistics**
```http
GET /api/admin/system/db-stats
```

### **7.3 Audit Logs**
```http
GET /api/admin/system/audit-logs?action={login|create|update|delete|approve|reject}&userId={id}&startDate={date}&endDate={date}&page={page}&pageSize={size}
```

### **7.4 Export Data**
```http
POST /api/admin/system/export
```
**Body:**
```json
{
  "type": "accommodations",
  "format": "excel",
  "filters": {
    "status": "DaDuyet",
    "startDate": "2025-01-01",
    "endDate": "2025-10-20"
  }
}
```

---

## 📧 **8. NOTIFICATION MANAGEMENT APIs**

### **8.1 Send System Notification**
```http
POST /api/admin/notifications/send
```
**Body:**
```json
{
  "recipients": "all", // "all" | "owners" | "customers" | [userId1, userId2]
  "title": "Thông báo bảo trì hệ thống",
  "message": "Hệ thống sẽ bảo trì từ 2h-4h sáng ngày 21/10",
  "type": "system", // "system" | "promotion" | "warning"
  "priority": "high" // "low" | "medium" | "high"
}
```

### **8.2 Notification History**
```http
GET /api/admin/notifications/history?page={page}&pageSize={size}
```

---

## 🔧 **9. SETTINGS & CONFIGURATION APIs**

### **9.1 Get System Settings**
```http
GET /api/admin/settings
```

### **9.2 Update System Settings**
```http
PUT /api/admin/settings
```
**Body:**
```json
{
  "maintenanceMode": false,
  "allowNewRegistrations": true,
  "maxAccommodationsPerOwner": 10,
  "commissionRate": 0.15,
  "autoApprovalEnabled": false
}
```

### **9.3 Feature Flags**
```http
GET /api/admin/feature-flags
PUT /api/admin/feature-flags
```

---

## 📈 **10. ANALYTICS & REPORTS APIs**

### **10.1 Custom Report Generator**
```http
POST /api/admin/reports/generate
```
**Body:**
```json
{
  "reportType": "revenue_by_region",
  "period": "monthly",
  "startDate": "2025-01-01",
  "endDate": "2025-10-20",
  "groupBy": "province",
  "format": "excel"
}
```

### **10.2 Popular Destinations**
```http
GET /api/admin/analytics/popular-destinations?period={monthly}&limit={number}
```

### **10.3 User Behavior Analytics**
```http
GET /api/admin/analytics/user-behavior?metric={page_views|booking_conversion|search_patterns}
```

---

## 🚨 **Error Handling**

### **Common Error Responses:**
```json
{
  "success": false,
  "message": "Không có quyền truy cập",
  "errorCode": "ADMIN_ACCESS_DENIED",
  "timestamp": "2025-10-20T10:30:00Z"
}
```

### **HTTP Status Codes:**
- `200` - Success
- `201` - Created  
- `400` - Bad Request
- `401` - Unauthorized (chưa đăng nhập)
- `403` - Forbidden (không phải Admin)
- `404` - Not Found
- `409` - Conflict
- `500` - Internal Server Error

---

## 🔐 **Security Notes**

### **Rate Limiting:**
```
Admin APIs: 1000 requests/hour
Normal APIs: 500 requests/hour
```

### **Audit Trail:**
Tất cả admin actions được log với:
- Admin ID
- Action type
- Target resource
- Timestamp
- IP address
- User agent

### **Required Permissions:**
```javascript
// Minimum permissions for admin operations
{
  "accommodations": ["view_all", "approve", "reject"],
  "users": ["view_all", "create", "update_role", "disable"],
  "payments": ["view_all", "process_refund"],
  "system": ["view_stats", "export_data", "manage_settings"]
}
```

**🎯 Tổng cộng có 45+ API endpoints dành riêng cho Admin với đầy đủ quyền quản trị hệ thống!**
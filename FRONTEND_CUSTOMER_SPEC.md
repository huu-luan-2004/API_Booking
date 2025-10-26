# 🧳 KHÁCH HÀNG FRONTEND SPECIFICATION

## 🎯 **Mục tiêu**
Giao diện thân thiện cho khách hàng tìm kiếm, đặt phòng và quản lý chuyến đi

---

## 🚀 **Các chức năng chính**

### **1. 🏠 TRANG CHỦ & TÌM KIẾM**
```
🔍 Tìm kiếm thông minh:
- Search bar prominent với autocomplete địa chỉ
- Filter: Ngày nhận/trả phòng, số người, giá
- Sắp xếp: Giá, đánh giá, khoảng cách
- Map view toggle để xem vị trí
- Quick filters: Gần trung tâm, Có bãi đậu xe, Wifi...

🏆 Nội dung nổi bật:
- Khách sạn được đánh giá cao
- Ưu đãi đặc biệt
- Địa điểm hot trend
- Last minute deals
```

**API cần dùng:**
```http
GET /api/cosoluutru?page=1&pageSize=20&q=search  # Tìm kiếm
GET /api/maps/accommodations?status=DaDuyet  # Map view
GET /api/maps/suggest?query=  # Address autocomplete
GET /api/rooms?available=true&checkin=&checkout=  # Available rooms
```

### **2. 🏨 CHI TIẾT KHÁCH SẠN & PHÒNG**
```
📋 Thông tin chi tiết:
- Gallery ảnh với lightbox
- Mô tả khách sạn & tiện ích
- Vị trí trên map + địa điểm gần đó
- Đánh giá & review của khách (future feature)
- Chính sách khách sạn

🏠 Danh sách phòng:
- Loại phòng, giá, sức chứa
- Ảnh phòng + mô tả tiện ích
- Giá theo ngày (nếu có promotion)
- Nút "Đặt ngay" với quick booking
- Check availability real-time
```

**API cần dùng:**
```http
GET /api/cosoluutru/{id}  # Chi tiết khách sạn
GET /api/rooms?coSoId={id}&available=true  # Phòng available
GET /api/maps/reverse-geocode  # Địa chỉ chi tiết
GET /api/maps/nearby  # Địa điểm gần đó
GET /api/bookings/check-availability  # Check availability
```

### **3. 📅 QUY TRÌNH ĐẶT PHÒNG**
```
Step 1 - Chọn phòng:
- Select room type
- Confirm dates & guests
- Quick price calculation

Step 2 - Thông tin khách:
- Guest details form
- Special requests
- Contact information

Step 3 - Thanh toán:
- Price breakdown
- Payment method (VNPay)
- Terms & conditions
- Booking confirmation
```

**API cần dùng:**
```http
POST /api/bookings  # Tạo booking
GET /api/bookings/check-availability  # Validate dates
POST /api/payments/create-vnpay-payment  # VNPay payment
GET /api/payments/vnpay-return  # Payment callback
```

### **4. 👤 TÀI KHOẢN CÁ NHÂN**
```
📊 Dashboard cá nhân:
- Upcoming trips
- Past bookings
- Saved/favorite hotels
- Booking statistics

📋 Quản lý đặt phòng:
- Active bookings với countdown
- Booking history với status
- Download booking confirmations
- Request modifications/cancellations

⚙️ Cài đặt:
- Update profile & avatar
- Change password
- Notification preferences
- Delete account
```

**API cần dùng:**
```http
GET /api/user/me  # Profile info
PUT /api/user/profile  # Update profile
PUT /api/user/avatar  # Upload avatar
GET /api/bookings/user  # My bookings
POST /api/payments/create-refund  # Cancel booking
```

### **5. 💳 THANH TOÁN & HÓA ĐƠN**
```
💰 Payment flow:
- Secure VNPay integration
- Multiple payment types: full/deposit
- Real-time payment status
- Automatic email confirmation
- Receipt download/print

📧 Communication:
- Booking confirmation email
- Payment success notification  
- Check-in reminders
- Post-stay follow-up
```

**API cần dùng:**
```http
POST /api/payments/create-vnpay-payment  # Create payment
GET /api/payments/vnpay-return  # Handle return
GET /api/payments?userId={id}  # Payment history
```

### **6. 🗺️ BẢN ĐỒ & VỊ TRÍ**
```
🗺️ Interactive map:
- Hiển thị tất cả khách sạn available
- Filter by price range on map
- Cluster markers for dense areas
- Click marker → Quick preview card
- Direction/navigation integration

📍 Location features:
- Current location detection
- Distance from landmarks
- Public transport info
- Nearby restaurants/attractions
```

**API cần dùng:**
```http
GET /api/maps/accommodations  # Hotels for map
GET /api/maps/nearby  # Nearby places
GET /api/maps/distance  # Calculate distances
GET /api/maps/geocode  # Location search
```

---

## 🎨 **UI/UX Design**

### **Homepage Layout:**
```
┌─────────────────────────────────────────┐
│ 🏠 Hotel Booking    🔍 Search    👤 User│
├─────────────────────────────────────────┤
│           🔍 Hero Search Bar            │
│     [Địa điểm] [Check-in] [Guests] [🔍] │
├─────────────────────────────────────────┤
│ 🏆 Featured Hotels (Horizontal Scroll)  │
├─────────────────────────────────────────┤
│ 🗺️ Map View Toggle | 📋 List View      │
├─────────────────────────────────────────┤
│ 🏨 Hotel Grid/List with Filters        │
└─────────────────────────────────────────┘
```

### **Mobile-First Design:**
```
📱 Mobile Navigation:
- Bottom tab bar: Home, Search, Bookings, Map, Profile
- Sticky search bar
- Swipe gestures for galleries
- Pull-to-refresh
- Floating action button for quick search

💡 Touch Optimizations:
- Large touch targets (44px min)
- Thumb-friendly button placement
- Swipe to delete/favorite
- Haptic feedback
- Voice search integration
```

### **Color Scheme:**
```css
Primary: #0ea5e9 (Sky Blue)
Secondary: #64748b (Slate)
Success: #10b981 (Emerald)
Warning: #f59e0b (Amber)
Danger: #ef4444 (Red)

Hotel Cards:
Available: #22c55e (Green)
Limited: #f97316 (Orange)  
Sold Out: #94a3b8 (Gray)
Featured: #8b5cf6 (Purple)
```

### **Components cần thiết:**
```jsx
// Layout Components
<CustomerLayout />
<BottomNavigation />
<SearchBar />
<FilterPanel />

// Hotel Components  
<HotelCard />
<HotelGallery />
<RoomCard />
<BookingCard />
<PriceDisplay />

// Booking Components
<DatePicker />
<GuestSelector />
<BookingForm />
<PaymentForm />
<BookingConfirmation />

// Map Components
<MapView />
<HotelMarker />
<LocationPicker />

// User Components
<ProfileForm />
<BookingHistory />
<FavoritesList />
```

---

## 📱 **Responsive Breakpoints**

### **Mobile (320-767px):**
```css
- Single column layout
- Bottom navigation
- Full-width cards
- Stacked search filters
- Touch-optimized buttons
- Swipe galleries
```

### **Tablet (768-1023px):**
```css
- Two column hotel grid  
- Side navigation drawer
- Horizontal filter bar
- Modal dialogs
- Split-screen booking flow
```

### **Desktop (1024px+):**
```css
- Three column hotel grid
- Sidebar filters
- Inline booking forms  
- Hover interactions
- Keyboard shortcuts
- Multi-step wizards
```

---

## 🔄 **User Journey & State Management**

### **Guest Flow (No Login):**
```javascript
1. Browse hotels anonymously
2. Select hotel & room
3. Prompted to login before booking
4. Register/Login with Firebase
5. Complete booking process
```

### **Returning Customer Flow:**
```javascript  
1. Auto-login if remembered
2. Personalized recommendations
3. Quick-book from favorites
4. Pre-filled booking forms
5. One-click rebooking
```

### **Global State:**
```javascript
{
  auth: {
    user: CustomerData | null,
    isGuest: boolean,
    token: string | null
  },
  search: {
    query: SearchParams,
    results: Hotel[],
    filters: FilterState,
    loading: boolean,
    mapView: boolean
  },
  booking: {
    selectedHotel: Hotel | null,
    selectedRoom: Room | null,
    bookingDetails: BookingForm,
    currentStep: number,
    paymentStatus: PaymentStatus
  },
  user: {
    profile: UserProfile,
    bookings: Booking[],
    favorites: Hotel[],
    preferences: UserPreferences
  }
}
```

---

## 🔧 **Key User Workflows**

### **1. Hotel Search & Discovery:**
```javascript
1. Enter destination in search bar
2. Select dates & guests
3. View results on map/list
4. Apply filters (price, amenities)
5. Save favorites
6. Compare hotels
```

### **2. Booking Process:**
```javascript
1. Select hotel from search results
2. View hotel details & rooms
3. Choose room & dates
4. Fill guest information
5. Review booking details
6. Complete VNPay payment
7. Receive confirmation
```

### **3. Account Management:**
```javascript
1. Register with email/phone
2. Verify account
3. Complete profile
4. Set preferences
5. Add payment methods
6. Enable notifications
```

### **4. Trip Management:**
```javascript
1. View upcoming bookings
2. Download confirmations
3. Request modifications
4. Track payment status
5. Contact hotel
6. Cancel if needed
```

---

## 📋 **Pages & Routes**

```javascript
// Public Routes
/                     // Homepage
/search               // Search results
/hotels/:id           // Hotel details
/rooms/:id            // Room details

// Auth Routes  
/login                // Login page
/register             // Registration
/forgot-password      // Password reset

// Protected Routes
/booking/confirm      // Booking confirmation
/booking/:id          // Booking details
/profile              // User profile
/bookings             // My bookings
/favorites            // Saved hotels
/settings             // Account settings

// Booking Flow
/book/select-room     // Room selection
/book/guest-info      // Guest details
/book/payment         // Payment page
/book/confirmation    // Success page
```

---

## 🎯 **Advanced Features**

### **Smart Recommendations:**
```javascript
- Based on previous bookings
- Location-based suggestions
- Price range preferences
- Seasonal recommendations
- Similar hotels algorithm
```

### **Real-time Features:**
```javascript
- Live availability updates
- Price change notifications
- Last room alerts
- Flash sales notifications
- Booking status updates
```

### **Accessibility Features:**
```javascript
- Screen reader support
- Keyboard navigation
- High contrast mode
- Font size adjustment
- Voice search
- Multi-language support
```

### **Progressive Web App:**
```javascript
- Offline browsing
- Push notifications
- Add to home screen
- Background sync
- App-like experience
```

---

## 🧪 **Testing Scenarios**

### **Critical User Paths:**
```
1. Guest → Search → Book → Pay → Confirm
2. Register → Complete profile → Book
3. Login → View history → Rebook
4. Search → Filter → Map view → Book
5. Booking → Cancel → Refund
6. Mobile → Install PWA → Offline browse
```

### **Edge Cases:**
```
- Network interruption during payment
- Room becomes unavailable during booking
- Multiple users booking same room
- Payment timeout scenarios
- Browser back button handling
- Session expiration during booking
```

### **Device Testing:**
```
- iOS Safari (iPhone 12+)
- Android Chrome (Samsung, Pixel)
- iPad Safari
- Desktop Chrome/Firefox/Safari
- Slow 3G network simulation
- Touch/mouse/keyboard interactions
```

---

## 📊 **Performance & Analytics**

### **Core Web Vitals:**
```
LCP (Largest Contentful Paint): < 2.5s
FID (First Input Delay): < 100ms
CLS (Cumulative Layout Shift): < 0.1
```

### **Conversion Metrics:**
```
- Search to view rate
- View to booking rate  
- Booking completion rate
- Payment success rate
- User retention rate
- Mobile vs desktop performance
```

### **Technical Metrics:**
```
- Bundle size: < 300KB initial
- Image optimization: WebP + lazy loading
- API response times: < 500ms
- Offline functionality: Basic browse
- Push notification delivery: > 95%
```

---

## 💡 **Business Intelligence**

### **User Behavior Tracking:**
```javascript
// Analytics Events
- hotel_search
- hotel_view  
- room_select
- booking_start
- payment_complete
- booking_cancel
- filter_apply
- map_toggle
```

### **A/B Testing Opportunities:**
```
- Search result layouts
- Booking form designs
- Payment flow steps
- Pricing display formats
- Mobile navigation styles
- Call-to-action buttons
```

**🧳 Khách hàng frontend tập trung vào trải nghiệm mượt mà, tìm kiếm dễ dàng và đặt phòng nhanh chóng!**
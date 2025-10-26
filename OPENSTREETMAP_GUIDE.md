# 🗺️ OPENSTREETMAP INTEGRATION GUIDE

## 📋 **Tổng quan**

Dự án Hotel Booking API đã được tích hợp OpenStreetMap để cung cấp các tính năng:

✅ **Geocoding** - Chuyển địa chỉ thành tọa độ  
✅ **Reverse Geocoding** - Chuyển tọa độ thành địa chỉ  
✅ **Auto-Geocoding** - Tự động lấy tọa độ khi tạo cơ sở lưu trú  
✅ **Places Search** - Tìm địa điểm gần nhất  
✅ **Distance Calculator** - Tính khoảng cách  
✅ **Address Suggestions** - Gợi ý địa chỉ  
✅ **Hotels Map** - Hiển thị khách sạn trên bản đồ  

---

## 🛠️ **Cấu trúc Database**

### **Bảng `DiaChiChiTiet` đã hỗ trợ:**
```sql
CREATE TABLE DiaChiChiTiet (
    Id int IDENTITY(1,1) PRIMARY KEY,
    SoNha nvarchar(50),
    Phuong nvarchar(100),
    Quan nvarchar(100),
    ThanhPho nvarchar(100) NOT NULL,
    KinhDo float,          -- Longitude (Kinh độ)
    ViDo float             -- Latitude (Vĩ độ)
);
```

### **Liên kết với cơ sở lưu trú:**
- `CoSoLuuTru.IdDiaChi` → `DiaChiChiTiet.Id`
- Mỗi cơ sở lưu trú có 1 địa chỉ với tọa độ GPS

---

## 🔧 **Backend Implementation**

### **1. OpenStreetMapService**
```csharp
// Services/OpenStreetMapService.cs
public class OpenStreetMapService
{
    // Geocoding: Address → Coordinates
    public async Task<GeocodeResult?> GeocodeAsync(string address)
    
    // Reverse Geocoding: Coordinates → Address  
    public async Task<ReverseGeocodeResult?> ReverseGeocodeAsync(double lat, double lon)
    
    // Search nearby places
    public async Task<List<PlaceSearchResult>> SearchNearbyAsync(double lat, double lon, string? category, int radius)
}
```

### **2. MapsController - API Endpoints**
```csharp
// Controllers/MapsController.cs
[Route("api/maps")]
public class MapsController
{
    GET /geocode              // Address → Coordinates
    GET /reverse-geocode      // Coordinates → Address
    GET /nearby              // Find nearby places
    GET /accommodations      // Hotels for map display
    GET /distance            // Calculate distance
    GET /suggest             // Address autocomplete
}
```

### **3. Auto-Geocoding Integration**
```csharp
// Controllers/CoSoLuuTruController.cs - Enhanced
[HttpPost]
public async Task<IActionResult> Create()
{
    // Auto-geocoding nếu chưa có tọa độ
    if (!kinhDo.HasValue || !viDo.HasValue)
    {
        var fullAddress = $"{soNha} {phuong}, {quan}, {thanhPho}, Vietnam";
        var geocodeResult = await _mapService.GeocodeAsync(fullAddress);
        
        if (geocodeResult != null)
        {
            kinhDo = geocodeResult.Longitude;
            viDo = geocodeResult.Latitude;
        }
    }
}
```

---

## 🌐 **API Endpoints**

### **1. Geocoding**
```http
GET /api/maps/geocode?address=123%20Phường%20Trần%20Hưng%20Đạo,%20Hà%20Nội

Response:
{
  "success": true,
  "data": {
    "latitude": 21.028511,
    "longitude": 105.804817,
    "displayName": "123, Phường Trần Hưng Đạo, Quận Hoàn Kiếm, Hà Nội"
  }
}
```

### **2. Reverse Geocoding**
```http
GET /api/maps/reverse-geocode?lat=21.028511&lon=105.804817

Response:
{
  "success": true,
  "data": {
    "displayName": "123, Phường Trần Hưng Đạo, Quận Hoàn Kiếm, Hà Nội",
    "address": {
      "houseNumber": "123",
      "road": "Phố Trần Hưng Đạo",
      "quarter": "Phường Trần Hưng Đạo",
      "cityDistrict": "Quận Hoàn Kiếm",
      "city": "Hà Nội"
    }
  }
}
```

### **3. Hotels Map**
```http
GET /api/maps/accommodations?status=DaDuyet

Response:
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
      "anhUrl": "/api/static/uploads/accommodations/hotel1.jpg"
    }
  ]
}
```

### **4. Nearby Places**
```http
GET /api/maps/nearby?lat=21.028511&lon=105.804817&category=restaurant&radius=1000

Response:
{
  "success": true,
  "data": [
    {
      "name": "Nhà hàng ABC",
      "latitude": 21.029,
      "longitude": 105.805,
      "distance": 150.5,
      "type": "restaurant"
    }
  ]
}
```

---

## 🎯 **Use Cases**

### **1. Tạo cơ sở lưu trú với auto-geocoding**
```javascript
// Frontend: Tạo cơ sở lưu trú
const formData = new FormData();
formData.append('tenCoSo', 'Hotel ABC');
formData.append('soNha', '123');
formData.append('phuong', 'Phường Trần Hưng Đạo');
formData.append('quan', 'Quận Hoàn Kiếm');
formData.append('thanhPho', 'Hà Nội');
// Không cần kinhDo, viDo - hệ thống sẽ tự động geocoding

const response = await fetch('/api/cosoluutru', {
    method: 'POST',
    headers: { 'Authorization': `Bearer ${token}` },
    body: formData
});
```

### **2. Hiển thị bản đồ khách sạn**
```html
<!-- Frontend: Leaflet Map -->
<div id="map" style="height: 400px;"></div>

<script>
const map = L.map('map').setView([21.028511, 105.804817], 13);
L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png').addTo(map);

// Load hotels from API
const response = await fetch('/api/maps/accommodations?status=DaDuyet');
const result = await response.json();

result.data.forEach(hotel => {
    L.marker([hotel.latitude, hotel.longitude])
        .bindPopup(`<b>${hotel.tenCoSo}</b><br>${hotel.diaChi}`)
        .addTo(map);
});
</script>
```

### **3. Address Autocomplete**
```javascript
// Frontend: Address suggestions
async function suggestAddress(query) {
    const response = await fetch(`/api/maps/suggest?query=${encodeURIComponent(query)}`);
    const result = await response.json();
    
    return result.data.map(item => ({
        label: item.displayName,
        value: item.displayName,
        lat: item.latitude,
        lon: item.longitude
    }));
}
```

---

## 🧪 **Testing**

### **1. Mở file test**
```bash
# Khởi động server
dotnet run

# Mở browser với file test
start OPENSTREETMAP_TEST.html
```

### **2. Test scenarios**
1. **Geocoding**: Nhập địa chỉ → Xem tọa độ trên map
2. **Reverse Geocoding**: Nhập tọa độ → Xem địa chỉ chi tiết
3. **Hotels Map**: Load danh sách khách sạn trên bản đồ
4. **Nearby Search**: Tìm nhà hàng, ngân hàng gần vị trí
5. **Distance Calculator**: Tính khoảng cách giữa 2 điểm
6. **Address Suggestions**: Gợi ý địa chỉ khi gõ

---

## 📱 **Frontend Integration**

### **1. Leaflet Map Setup**
```html
<!-- Include Leaflet -->
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>

<!-- Map container -->
<div id="map" style="height: 400px;"></div>

<script>
// Initialize map
const map = L.map('map').setView([21.028511, 105.804817], 13);
L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '© OpenStreetMap contributors'
}).addTo(map);
</script>
```

### **2. API Helper Functions**
```javascript
class MapAPI {
    constructor(baseUrl) {
        this.baseUrl = baseUrl;
    }
    
    async geocode(address) {
        const response = await fetch(`${this.baseUrl}/maps/geocode?address=${encodeURIComponent(address)}`);
        return await response.json();
    }
    
    async reverseGeocode(lat, lon) {
        const response = await fetch(`${this.baseUrl}/maps/reverse-geocode?lat=${lat}&lon=${lon}`);
        return await response.json();
    }
    
    async getHotels(status = 'DaDuyet') {
        const response = await fetch(`${this.baseUrl}/maps/accommodations?status=${status}`);
        return await response.json();
    }
    
    async searchNearby(lat, lon, category, radius = 1000) {
        const response = await fetch(`${this.baseUrl}/maps/nearby?lat=${lat}&lon=${lon}&category=${category}&radius=${radius}`);
        return await response.json();
    }
}

// Usage
const mapAPI = new MapAPI('http://localhost:5000/api');
const hotels = await mapAPI.getHotels();
```

---

## 🚀 **Production Deployment**

### **1. Environment Setup**
- ✅ OpenStreetMap service hoạt động qua HTTP calls (không cần API key)
- ✅ Rate limiting: Nominatim có giới hạn 1 request/second
- ✅ User-Agent header đã được set

### **2. Performance Considerations**
```csharp
// Cache geocoding results
services.AddMemoryCache();

public class OpenStreetMapService 
{
    private readonly IMemoryCache _cache;
    
    public async Task<GeocodeResult?> GeocodeAsync(string address)
    {
        var cacheKey = $"geocode_{address}";
        if (_cache.TryGetValue(cacheKey, out GeocodeResult? cached))
            return cached;
            
        var result = await CallNominatimAPI(address);
        _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
        return result;
    }
}
```

### **3. Error Handling**
- ✅ Network timeouts handled
- ✅ Invalid coordinates validation
- ✅ Graceful fallback when geocoding fails
- ✅ Auto-geocoding is optional (không làm gián đoạn tạo cơ sở)

---

## 📊 **Features Summary**

| Tính năng | Status | Description |
|-----------|--------|-------------|
| 🔍 Geocoding | ✅ | Chuyển địa chỉ → tọa độ |
| 📍 Reverse Geocoding | ✅ | Chuyển tọa độ → địa chỉ |
| 🤖 Auto-Geocoding | ✅ | Tự động khi tạo cơ sở lưu trú |
| 🏨 Hotels Map | ✅ | Hiển thị khách sạn trên bản đồ |
| 🎯 Nearby Search | ✅ | Tìm địa điểm gần nhất |
| 📏 Distance Calculator | ✅ | Tính khoảng cách Haversine |
| 💡 Address Suggestions | ✅ | Autocomplete địa chỉ |
| 🧪 Testing Interface | ✅ | OPENSTREETMAP_TEST.html |
| 📚 Documentation | ✅ | API docs + examples |

**🎉 OpenStreetMap integration hoàn tất và sẵn sàng sử dụng!**
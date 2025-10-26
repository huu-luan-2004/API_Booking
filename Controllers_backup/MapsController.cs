using HotelBookingApi.Data;
using HotelBookingApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/maps")]
public class MapsController : ControllerBase
{
    private readonly OpenStreetMapService _mapService;
    private readonly CoSoLuuTruRepository _coSoRepo;
    private readonly ILogger<MapsController> _logger;

    public MapsController(OpenStreetMapService mapService, CoSoLuuTruRepository coSoRepo, ILogger<MapsController> logger)
    {
        _mapService = mapService;
        _coSoRepo = coSoRepo;
        _logger = logger;
    }

    /// <summary>
    /// Chuyển địa chỉ thành tọa độ
    /// </summary>
    [HttpGet("geocode")]
    public async Task<IActionResult> Geocode([FromQuery] string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return BadRequest(new { success = false, message = "Địa chỉ không được để trống" });

        try
        {
            var result = await _mapService.GeocodeAsync(address);
            
            if (result == null)
                return NotFound(new { success = false, message = "Không tìm thấy tọa độ cho địa chỉ này" });

            return Ok(new { 
                success = true, 
                data = result,
                message = "Geocoding thành công"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Geocode API");
            return StatusCode(500, new { success = false, message = "Lỗi hệ thống" });
        }
    }

    /// <summary>
    /// Chuyển tọa độ thành địa chỉ
    /// </summary>
    [HttpGet("reverse-geocode")]
    public async Task<IActionResult> ReverseGeocode([FromQuery] double lat, [FromQuery] double lon)
    {
        if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
            return BadRequest(new { success = false, message = "Tọa độ không hợp lệ" });

        try
        {
            var result = await _mapService.ReverseGeocodeAsync(lat, lon);
            
            if (result == null)
                return NotFound(new { success = false, message = "Không tìm thấy địa chỉ cho tọa độ này" });

            return Ok(new { 
                success = true, 
                data = result,
                message = "Reverse geocoding thành công"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ReverseGeocode API");
            return StatusCode(500, new { success = false, message = "Lỗi hệ thống" });
        }
    }

    /// <summary>
    /// Tìm địa điểm gần nhất
    /// </summary>
    [HttpGet("nearby")]
    public async Task<IActionResult> SearchNearby(
        [FromQuery] double lat, 
        [FromQuery] double lon, 
        [FromQuery] string? category = null,
        [FromQuery] int radius = 1000)
    {
        if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
            return BadRequest(new { success = false, message = "Tọa độ không hợp lệ" });

        if (radius <= 0 || radius > 50000) // max 50km
            return BadRequest(new { success = false, message = "Bán kính phải từ 1 đến 50000 mét" });

        try
        {
            var results = await _mapService.SearchNearbyAsync(lat, lon, category, radius);
            
            return Ok(new { 
                success = true, 
                data = results,
                total = results.Count,
                message = $"Tìm thấy {results.Count} địa điểm trong bán kính {radius}m"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SearchNearby API");
            return StatusCode(500, new { success = false, message = "Lỗi hệ thống" });
        }
    }

    /// <summary>
    /// Lấy tọa độ và địa chỉ của các cơ sở lưu trú (cho hiển thị map)
    /// </summary>
    [HttpGet("accommodations")]
    public async Task<IActionResult> GetAccommodationsForMap([FromQuery] string? status = "DaDuyet")
    {
        try
        {
            // Lấy danh sách cơ sở lưu trú từ database
            var accommodations = await _coSoRepo.ListAsync(1, 1000, null, true, null);
            
            var mapData = accommodations
                .Where(a => string.IsNullOrEmpty(status) || 
                           (a.TrangThaiDuyet?.ToString() == status))
                .Where(a => a.KinhDo != null && a.ViDo != null) // Chỉ lấy những cơ sở có tọa độ
                .Select(a => new {
                    id = a.Id,
                    tenCoSo = a.TenCoSo,
                    latitude = (double)a.ViDo,
                    longitude = (double)a.KinhDo,
                    diaChi = BuildFullAddress(a),
                    trangThaiDuyet = a.TrangThaiDuyet?.ToString(),
                    anhUrl = !string.IsNullOrEmpty(a.Anh?.ToString()) 
                        ? $"/api/static/uploads/accommodations/{a.Anh}" 
                        : null,
                    moTa = a.MoTa?.ToString()
                })
                .ToArray();

            return Ok(new { 
                success = true, 
                data = mapData,
                total = mapData.Length,
                message = "Lấy danh sách cơ sở lưu trú thành công"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetAccommodationsForMap API");
            return StatusCode(500, new { success = false, message = "Lỗi hệ thống" });
        }
    }

    // Helper method to build full address
    private static string BuildFullAddress(dynamic accommodation)
    {
        var parts = new List<string>();
        
        if (!string.IsNullOrEmpty(accommodation.SoNha?.ToString()))
            parts.Add(accommodation.SoNha.ToString());
        if (!string.IsNullOrEmpty(accommodation.Phuong?.ToString()))
            parts.Add(accommodation.Phuong.ToString());
        if (!string.IsNullOrEmpty(accommodation.Quan?.ToString()))
            parts.Add(accommodation.Quan.ToString());
        if (!string.IsNullOrEmpty(accommodation.ThanhPho?.ToString()))
            parts.Add(accommodation.ThanhPho.ToString());

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Tính khoảng cách giữa 2 điểm
    /// </summary>
    [HttpGet("distance")]
    public IActionResult CalculateDistance(
        [FromQuery] double lat1, 
        [FromQuery] double lon1, 
        [FromQuery] double lat2, 
        [FromQuery] double lon2)
    {
        if (lat1 < -90 || lat1 > 90 || lon1 < -180 || lon1 > 180 ||
            lat2 < -90 || lat2 > 90 || lon2 < -180 || lon2 > 180)
            return BadRequest(new { success = false, message = "Tọa độ không hợp lệ" });

        try
        {
            var distance = CalculateHaversineDistance(lat1, lon1, lat2, lon2);
            
            return Ok(new { 
                success = true, 
                data = new {
                    distance = Math.Round(distance, 2),
                    unit = "meters",
                    distanceKm = Math.Round(distance / 1000, 2),
                    from = new { latitude = lat1, longitude = lon1 },
                    to = new { latitude = lat2, longitude = lon2 }
                },
                message = "Tính khoảng cách thành công"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CalculateDistance API");
            return StatusCode(500, new { success = false, message = "Lỗi hệ thống" });
        }
    }

    /// <summary>
    /// Suggest địa chỉ dựa trên input (autocomplete)
    /// </summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> SuggestAddress([FromQuery] string query, [FromQuery] int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return BadRequest(new { success = false, message = "Query phải có ít nhất 2 ký tự" });

        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://nominatim.openstreetmap.org/search?q={encodedQuery}&format=json&limit={limit}&countrycodes=vn&addressdetails=1";
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "HotelBookingAPI/1.0");
            
            var response = await httpClient.GetStringAsync(url);
            var results = System.Text.Json.JsonSerializer.Deserialize<NominatimSearchResult[]>(response);

            var suggestions = results?.Select(r => new {
                displayName = r.display_name,
                latitude = double.Parse(r.lat),
                longitude = double.Parse(r.lon),
                type = r.type,
                @class = r.@class
            }).ToArray() ?? Array.Empty<object>();

            return Ok(new { 
                success = true, 
                data = suggestions,
                total = suggestions.Length,
                message = $"Tìm thấy {suggestions.Length} gợi ý"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SuggestAddress API");
            return StatusCode(500, new { success = false, message = "Lỗi hệ thống" });
        }
    }

    // Helper method
    private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6371000; // meters
        
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        
        return earthRadius * c;
    }

    // Internal DTO for Nominatim (duplicate from service for controller use)
    private class NominatimSearchResult
    {
        public string lat { get; set; } = "";
        public string lon { get; set; } = "";
        public string display_name { get; set; } = "";
        public string? type { get; set; }
        public string? @class { get; set; }
    }
}
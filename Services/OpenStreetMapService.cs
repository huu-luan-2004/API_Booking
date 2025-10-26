using System.Text.Json;

namespace HotelBookingApi.Services;

public class OpenStreetMapService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenStreetMapService> _logger;

    public OpenStreetMapService(HttpClient httpClient, ILogger<OpenStreetMapService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // Set user agent cho Nominatim API
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "HotelBookingAPI/1.0 (contact@hotel.com)");
    }

    /// <summary>
    /// Geocoding: Chuyển địa chỉ thành tọa độ (lat, lon)
    /// </summary>
    public async Task<GeocodeResult?> GeocodeAsync(string address)
    {
        try
        {
            var encodedAddress = Uri.EscapeDataString(address);
            var url = $"https://nominatim.openstreetmap.org/search?q={encodedAddress}&format=json&limit=1&countrycodes=vn";
            
            var response = await _httpClient.GetStringAsync(url);
            var results = JsonSerializer.Deserialize<NominatimSearchResult[]>(response);

            if (results?.Length > 0)
            {
                var result = results[0];
                return new GeocodeResult
                {
                    Latitude = double.Parse(result.lat, System.Globalization.CultureInfo.InvariantCulture),
                    Longitude = double.Parse(result.lon, System.Globalization.CultureInfo.InvariantCulture),
                    DisplayName = result.display_name,
                    BoundingBox = result.boundingbox
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error geocoding address: {Address}", address);
            return null;
        }
    }

    /// <summary>
    /// Reverse Geocoding: Chuyển tọa độ thành địa chỉ
    /// </summary>
    public async Task<ReverseGeocodeResult?> ReverseGeocodeAsync(double latitude, double longitude)
    {
        var latStr = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lonStr = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);

        async Task<ReverseGeocodeResult?> TryFetchAsync(string url)
        {
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<NominatimReverseResult>(response);
            if (result == null) return null;
            return new ReverseGeocodeResult
            {
                Latitude = latitude,
                Longitude = longitude,
                DisplayName = result.display_name,
                Address = new AddressComponents
                {
                    HouseNumber = result.address?.house_number,
                    Road = result.address?.road,
                    Quarter = result.address?.quarter,
                    Suburb = result.address?.suburb,
                    Neighbourhood = result.address?.neighbourhood,
                    Town = result.address?.town,
                    Village = result.address?.village,
                    Hamlet = result.address?.hamlet,
                    Municipality = result.address?.municipality,
                    CityDistrict = result.address?.city_district,
                    County = result.address?.county,
                    StateDistrict = result.address?.state_district,
                    City = result.address?.city,
                    Province = result.address?.province ?? result.address?.state,
                    Country = result.address?.country,
                    PostCode = result.address?.postcode
                }
            };
        }

        // 1) Thử với country filter và addressdetails + zoom cao
        var url1 = $"https://nominatim.openstreetmap.org/reverse?lat={latStr}&lon={lonStr}&format=json&addressdetails=1&zoom=18&accept-language=vi,en&countrycodes=vn";
        try
        {
            var r1 = await TryFetchAsync(url1);
            if (r1 != null) return r1;
        }
        catch (Exception ex1)
        {
            _logger.LogWarning(ex1, "Reverse geocode url1 failed, trying fallback url2");
        }

        // 2) Fallback: bỏ country filter
        var url2f = $"https://nominatim.openstreetmap.org/reverse?lat={latStr}&lon={lonStr}&format=json&addressdetails=1&zoom=18&accept-language=vi,en";
        try
        {
            var r2 = await TryFetchAsync(url2f);
            if (r2 != null) return r2;
        }
        catch (Exception ex2)
        {
            _logger.LogWarning(ex2, "Reverse geocode url2 failed, trying final fallback url3");
        }

        // 3) Cuối cùng: tối giản tham số
        var url3 = $"https://nominatim.openstreetmap.org/reverse?lat={latStr}&lon={lonStr}&format=json&accept-language=vi,en";
        try
        {
            var r3 = await TryFetchAsync(url3);
            if (r3 != null) return r3;
        }
        catch (Exception ex3)
        {
            _logger.LogError(ex3, "Reverse geocode all attempts failed for {Lat}, {Lon}", latitude, longitude);
        }

        return null;
    }

    /// <summary>
    /// Tìm kiếm địa điểm gần nhất
    /// </summary>
    public async Task<List<PlaceSearchResult>> SearchNearbyAsync(double latitude, double longitude, string? category = null, int radius = 1000)
    {
        try
        {
            var results = new List<PlaceSearchResult>();
            
            // Tìm kiếm trong bán kính (sử dụng bounding box)
            var lat1 = latitude - (radius / 111320.0); // ~111320 meters per degree lat
            var lat2 = latitude + (radius / 111320.0);
            var lon1 = longitude - (radius / (111320.0 * Math.Cos(latitude * Math.PI / 180)));
            var lon2 = longitude + (radius / (111320.0 * Math.Cos(latitude * Math.PI / 180)));

            var bbox = $"{lon1},{lat1},{lon2},{lat2}";
            var query = category ?? "amenity";
            
            var url = $"https://nominatim.openstreetmap.org/search?q={query}&format=json&limit=20&countrycodes=vn&bounded=1&viewbox={bbox}";
            
            var response = await _httpClient.GetStringAsync(url);
            var searchResults = JsonSerializer.Deserialize<NominatimSearchResult[]>(response);

            if (searchResults != null)
            {
                foreach (var item in searchResults)
                {
                    var distance = CalculateDistance(latitude, longitude, double.Parse(item.lat, System.Globalization.CultureInfo.InvariantCulture), double.Parse(item.lon, System.Globalization.CultureInfo.InvariantCulture));
                    if (distance <= radius)
                    {
                        results.Add(new PlaceSearchResult
                        {
                            Name = item.display_name,
                            Latitude = double.Parse(item.lat, System.Globalization.CultureInfo.InvariantCulture),
                            Longitude = double.Parse(item.lon, System.Globalization.CultureInfo.InvariantCulture),
                            Distance = distance,
                            Type = item.type,
                            Class = item.@class
                        });
                    }
                }
            }

            return results.OrderBy(r => r.Distance).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching nearby places");
            return new List<PlaceSearchResult>();
        }
    }

    /// <summary>
    /// Tính khoảng cách giữa 2 điểm (Haversine formula)
    /// </summary>
    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
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
}

// DTOs cho OpenStreetMap
public class GeocodeResult
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DisplayName { get; set; } = "";
    public string[]? BoundingBox { get; set; }
}

public class ReverseGeocodeResult
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DisplayName { get; set; } = "";
    public AddressComponents? Address { get; set; }
}

public class AddressComponents
{
    public string? HouseNumber { get; set; }
    public string? Road { get; set; }
    public string? Quarter { get; set; }
    public string? Suburb { get; set; }
    public string? Neighbourhood { get; set; }
    public string? Town { get; set; }
    public string? Village { get; set; }
    public string? Hamlet { get; set; }
    public string? Municipality { get; set; }
    public string? CityDistrict { get; set; }
    public string? County { get; set; }
    public string? StateDistrict { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? Country { get; set; }
    public string? PostCode { get; set; }
}

public class PlaceSearchResult
{
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Distance { get; set; } // meters
    public string? Type { get; set; }
    public string? Class { get; set; }
}

// DTOs cho Nominatim API responses
internal class NominatimSearchResult
{
    public string lat { get; set; } = "";
    public string lon { get; set; } = "";
    public string display_name { get; set; } = "";
    public string[]? boundingbox { get; set; }
    public string? type { get; set; }
    public string? @class { get; set; }
}

internal class NominatimReverseResult
{
    public string display_name { get; set; } = "";
    public NominatimAddress? address { get; set; }
}

internal class NominatimAddress
{
    public string? house_number { get; set; }
    public string? road { get; set; }
    public string? quarter { get; set; }
    public string? suburb { get; set; }
    public string? neighbourhood { get; set; }
    public string? town { get; set; }
    public string? village { get; set; }
    public string? hamlet { get; set; }
    public string? municipality { get; set; }
    public string? city_district { get; set; }
    public string? county { get; set; }
    public string? state_district { get; set; }
    public string? city { get; set; }
    public string? province { get; set; }
    public string? state { get; set; }
    public string? country { get; set; }
    public string? postcode { get; set; }
}
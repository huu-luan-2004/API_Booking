using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using HotelBookingApi.Data;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;

namespace HotelBookingApi.Controllers
{
    [ApiController]
    [Route("api/ratings")]
    public class RatingsController : ControllerBase
    {
        private readonly DanhGiaRepository _danhGiaRepo;
        private readonly PhongRepository _phongRepo;
        private readonly DatPhongRepository _datPhongRepo;

        public RatingsController(
            DanhGiaRepository danhGiaRepo,
            PhongRepository phongRepo,
            DatPhongRepository datPhongRepo)
        {
            _danhGiaRepo = danhGiaRepo;
            _phongRepo = phongRepo;
            _datPhongRepo = datPhongRepo;
        }

        // Tạo đánh giá mới
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateRating([FromBody] CreateRatingRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst("id")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                // Kiểm tra phòng tồn tại
                var room = await _phongRepo.GetByIdAsync(request.RoomId);
                if (room == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy phòng" });
                }

                // Nếu có bookingId, kiểm tra booking và quyền đánh giá
                if (request.BookingId.HasValue)
                {
                    var booking = await _datPhongRepo.GetByIdAsync(request.BookingId.Value);
                    if (booking == null)
                    {
                        return NotFound(new { success = false, message = "Không tìm thấy booking" });
                    }

                    // Kiểm tra booking thuộc về user này
                    var bookingUserId = booking.GetType().GetProperty("IdNguoiDung")?.GetValue(booking);
                    if (bookingUserId?.ToString() != userId.ToString())
                    {
                        return Forbid(new { success = false, message = "Bạn không có quyền đánh giá booking này" }.ToString());
                    }

                    // Kiểm tra đã đánh giá chưa
                    var hasRated = await _danhGiaRepo.HasUserRatedRoomAsync(userId, request.BookingId.Value);
                    if (hasRated)
                    {
                        return BadRequest(new { success = false, message = "Bạn đã đánh giá booking này rồi" });
                    }
                }

                // Tạo đánh giá
                var ratingId = await _danhGiaRepo.CreateAsync(
                    userId,
                    request.RoomId,
                    request.Rating,
                    request.Comment ?? "",
                    request.BookingId,
                    request.Media,
                    request.IsAnonymous
                );

                // Lấy đánh giá vừa tạo
                var newRating = await _danhGiaRepo.GetByIdAsync(ratingId);

                return Ok(new
                {
                    success = true,
                    message = "Tạo đánh giá thành công",
                    data = newRating
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Lấy đánh giá theo phòng
        [HttpGet("room/{roomId}")]
        public async Task<IActionResult> GetRatingsByRoom(
            int roomId,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10)
        {
            try
            {
                // Kiểm tra phòng tồn tại
                var room = await _phongRepo.GetByIdAsync(roomId);
                if (room == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy phòng" });
                }

                // Lấy danh sách đánh giá
                var ratings = await _danhGiaRepo.GetByRoomIdAsync(roomId, page, limit);
                var totalRatings = await _danhGiaRepo.CountByRoomIdAsync(roomId);
                var averageRating = await _danhGiaRepo.GetAverageRatingAsync(roomId);
                var ratingStats = await _danhGiaRepo.GetRatingStatsAsync(roomId);

                // Xử lý media URLs
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var processedRatings = ratings.Select(r =>
                {
                    var rating = r as IDictionary<string, object> ?? new Dictionary<string, object>();
                    
                    // Xử lý media URLs
                    if (rating.TryGetValue("Media", out var media) && !string.IsNullOrEmpty(media?.ToString()))
                    {
                        var mediaFiles = media.ToString()?.Split(',') ?? new string[0];
                        var mediaUrls = mediaFiles
                            .Where(f => !string.IsNullOrEmpty(f))
                            .Select(f => f.StartsWith("http") ? f : $"{baseUrl}/uploads/ratings/{f.Trim()}")
                            .ToArray();
                        rating["MediaUrls"] = mediaUrls;
                    }

                    return rating;
                });

                return Ok(new
                {
                    success = true,
                    message = "Lấy đánh giá thành công",
                    data = new
                    {
                        ratings = processedRatings,
                        pagination = new
                        {
                            page,
                            limit,
                            total = totalRatings,
                            totalPages = (int)Math.Ceiling((double)totalRatings / limit)
                        },
                        summary = new
                        {
                            averageRating = Math.Round(averageRating, 1),
                            totalRatings,
                            ratingStats
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Lấy đánh giá của người dùng
        [HttpGet("my-ratings")]
        [Authorize]
        public async Task<IActionResult> GetMyRatings(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10)
        {
            try
            {
                var userIdClaim = User.FindFirst("id")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                var ratings = await _danhGiaRepo.GetByUserIdAsync(userId, page, limit);

                // Xử lý URLs cho ảnh phòng và media
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var processedRatings = ratings.Select(r =>
                {
                    var rating = r as IDictionary<string, object> ?? new Dictionary<string, object>();
                    
                    // Xử lý ảnh phòng
                    if (rating.TryGetValue("AnhPhong", out var roomImage) && !string.IsNullOrEmpty(roomImage?.ToString()))
                    {
                        var imagePath = roomImage.ToString();
                        if (!imagePath.StartsWith("http"))
                        {
                            rating["RoomImageUrl"] = $"{baseUrl}/uploads/rooms/{imagePath}";
                        }
                    }

                    // Xử lý media đánh giá
                    if (rating.TryGetValue("Media", out var media) && !string.IsNullOrEmpty(media?.ToString()))
                    {
                        var mediaFiles = media.ToString()?.Split(',') ?? new string[0];
                        var mediaUrls = mediaFiles
                            .Where(f => !string.IsNullOrEmpty(f))
                            .Select(f => f.StartsWith("http") ? f : $"{baseUrl}/uploads/ratings/{f.Trim()}")
                            .ToArray();
                        rating["MediaUrls"] = mediaUrls;
                    }

                    return rating;
                });

                return Ok(new
                {
                    success = true,
                    message = "Lấy đánh giá của bạn thành công",
                    data = new
                    {
                        ratings = processedRatings,
                        pagination = new
                        {
                            page,
                            limit
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Cập nhật đánh giá
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateRating(int id, [FromBody] UpdateRatingRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst("id")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                // Kiểm tra đánh giá tồn tại và quyền sở hữu
                var isOwner = await _danhGiaRepo.IsOwnerAsync(id, userId);
                if (!isOwner)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đánh giá hoặc bạn không có quyền chỉnh sửa" });
                }

                // Cập nhật đánh giá
                var updated = await _danhGiaRepo.UpdateAsync(id, request.Rating, request.Comment, request.Media);
                if (!updated)
                {
                    return BadRequest(new { success = false, message = "Cập nhật đánh giá thất bại" });
                }

                // Lấy đánh giá đã cập nhật
                var updatedRating = await _danhGiaRepo.GetByIdAsync(id);

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật đánh giá thành công",
                    data = updatedRating
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Xóa đánh giá
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteRating(int id)
        {
            try
            {
                var userIdClaim = User.FindFirst("id")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Unauthorized(new { success = false, message = "Vui lòng đăng nhập" });
                }

                // Kiểm tra quyền sở hữu
                var isOwner = await _danhGiaRepo.IsOwnerAsync(id, userId);
                if (!isOwner)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy đánh giá hoặc bạn không có quyền xóa" });
                }

                // Xóa đánh giá
                var deleted = await _danhGiaRepo.DeleteAsync(id);
                if (!deleted)
                {
                    return BadRequest(new { success = false, message = "Xóa đánh giá thất bại" });
                }

                return Ok(new
                {
                    success = true,
                    message = "Xóa đánh giá thành công"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Lấy thống kê đánh giá phòng
        [HttpGet("room/{roomId}/stats")]
        public async Task<IActionResult> GetRoomRatingStats(int roomId)
        {
            try
            {
                // Kiểm tra phòng tồn tại
                var room = await _phongRepo.GetByIdAsync(roomId);
                if (room == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy phòng" });
                }

                var totalRatings = await _danhGiaRepo.CountByRoomIdAsync(roomId);
                var averageRating = await _danhGiaRepo.GetAverageRatingAsync(roomId);
                var ratingStats = await _danhGiaRepo.GetRatingStatsAsync(roomId);

                // Tạo stats đầy đủ từ 1-5 sao
                var fullStats = new List<object>();
                for (int i = 5; i >= 1; i--)
                {
                    var stat = ratingStats.FirstOrDefault(s => 
                        s.GetType().GetProperty("Diem")?.GetValue(s)?.ToString() == i.ToString());
                    
                    var count = 0;
                    if (stat != null)
                    {
                        var countValue = stat.GetType().GetProperty("SoLuong")?.GetValue(stat);
                        int.TryParse(countValue?.ToString(), out count);
                    }
                    
                    var percentage = totalRatings > 0 ? Math.Round((double)count / totalRatings * 100, 1) : 0;
                    
                    fullStats.Add(new
                    {
                        rating = i,
                        count,
                        percentage
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Lấy thống kê đánh giá thành công",
                    data = new
                    {
                        averageRating = Math.Round(averageRating, 1),
                        totalRatings,
                        distribution = fullStats
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    // Request models
    public class CreateRatingRequest
    {
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "ID phòng không hợp lệ")]
        public int RoomId { get; set; }

        [Required]
        [Range(1, 5, ErrorMessage = "Điểm đánh giá phải từ 1-5")]
        public int Rating { get; set; }

        [MaxLength(1000, ErrorMessage = "Bình luận không được vượt quá 1000 ký tự")]
        public string? Comment { get; set; }

        public int? BookingId { get; set; }

        [MaxLength(500, ErrorMessage = "Media không được vượt quá 500 ký tự")]
        public string? Media { get; set; }

        public bool IsAnonymous { get; set; } = false;
    }

    public class UpdateRatingRequest
    {
        [Required]
        [Range(1, 5, ErrorMessage = "Điểm đánh giá phải từ 1-5")]
        public int Rating { get; set; }

        [MaxLength(1000, ErrorMessage = "Bình luận không được vượt quá 1000 ký tự")]
        public string Comment { get; set; } = "";

        [MaxLength(500, ErrorMessage = "Media không được vượt quá 500 ký tự")]
        public string? Media { get; set; }
    }
}
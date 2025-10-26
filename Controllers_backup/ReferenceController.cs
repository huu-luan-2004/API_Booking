using Microsoft.AspNetCore.Mvc;
using HotelBookingApi.Data;
using Dapper;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/reference")]
public class ReferenceController : ControllerBase
{
    private readonly SqlConnectionFactory _factory;
    public ReferenceController(SqlConnectionFactory factory) => _factory = factory;

    [HttpGet("room-types")]
    public async Task<IActionResult> GetRoomTypes()
    {
        try
        {
            using var db = _factory.Create();
            
            // Kiểm tra xem có bảng LoaiPhong không
            var hasLoaiPhongTable = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='LoaiPhong'"
            );
            
            if (hasLoaiPhongTable > 0)
            {
                // Nếu có bảng LoaiPhong, lấy từ đó
                var roomTypes = await db.QueryAsync(
                    "SELECT Id, TenLoaiPhong as Name, MoTa as Description FROM LoaiPhong ORDER BY TenLoaiPhong"
                );
                
                return Ok(new { 
                    success = true, 
                    message = "Danh sách loại phòng", 
                    data = roomTypes 
                });
            }
            else
            {
                // Nếu không có bảng LoaiPhong, lấy từ cột LoaiPhong trong bảng Phong
                var hasLoaiPhongColumn = await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Phong' AND COLUMN_NAME='LoaiPhong'"
                );
                
                if (hasLoaiPhongColumn > 0)
                {
                    var roomTypes = await db.QueryAsync<string>(
                        "SELECT DISTINCT LoaiPhong FROM Phong WHERE LoaiPhong IS NOT NULL AND LoaiPhong != '' ORDER BY LoaiPhong"
                    );
                    
                    var result = roomTypes.Select((type, index) => new {
                        Id = index + 1,
                        Name = type,
                        Description = type
                    });
                    
                    return Ok(new { 
                        success = true, 
                        message = "Danh sách loại phòng", 
                        data = result 
                    });
                }
                else
                {
                    // Trả về danh sách mặc định
                    var defaultRoomTypes = new[]
                    {
                        new { Id = 1, Name = "Standard", Description = "Phòng tiêu chuẩn" },
                        new { Id = 2, Name = "Superior", Description = "Phòng cao cấp" },
                        new { Id = 3, Name = "Deluxe", Description = "Phòng sang trọng" },
                        new { Id = 4, Name = "Suite", Description = "Phòng tổng thống" },
                        new { Id = 5, Name = "Family", Description = "Phòng gia đình" },
                        new { Id = 6, Name = "Single", Description = "Phòng đơn" },
                        new { Id = 7, Name = "Double", Description = "Phòng đôi" },
                        new { Id = 8, Name = "Twin", Description = "Phòng hai giường đơn" }
                    };
                    
                    return Ok(new { 
                        success = true, 
                        message = "Danh sách loại phòng mặc định", 
                        data = defaultRoomTypes 
                    });
                }
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi lấy danh sách loại phòng", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> GetAddresses()
    {
        try
        {
            using var db = _factory.Create();
            
            // Kiểm tra xem bảng DiaChiChiTiet có tồn tại không
            var tableExists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DiaChiChiTiet'"
            );
            
            if (tableExists == 0)
            {
                return Ok(new { 
                    success = true, 
                    message = "Danh sách địa chỉ chi tiết", 
                    data = new List<object>() 
                });
            }

            var addresses = await db.QueryAsync(@"
                SELECT 
                    Id,
                    SoNha,
                    Phuong,
                    Quan,
                    ThanhPho,
                    KenhDo,
                    ViDo,
                    CONCAT_WS(', ',
                        CASE WHEN SoNha IS NOT NULL AND SoNha != '' THEN SoNha ELSE NULL END,
                        CASE WHEN Phuong IS NOT NULL AND Phuong != '' THEN Phuong ELSE NULL END,
                        CASE WHEN Quan IS NOT NULL AND Quan != '' THEN Quan ELSE NULL END,
                        CASE WHEN ThanhPho IS NOT NULL AND ThanhPho != '' THEN ThanhPho ELSE NULL END
                    ) AS DiaChiDayDu
                FROM DiaChiChiTiet 
                ORDER BY ThanhPho, Quan, Phuong
            ");

            return Ok(new { 
                success = true, 
                message = "Danh sách địa chỉ chi tiết", 
                data = addresses 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi lấy danh sách địa chỉ", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("provinces")]
    public async Task<IActionResult> GetProvinces()
    {
        try
        {
            using var db = _factory.Create();
            
            // Kiểm tra các bảng có thể chứa thông tin tỉnh thành
            var provinces = new[]
            {
                new { Id = 1, Name = "Hà Nội", Code = "HN" },
                new { Id = 2, Name = "TP. Hồ Chí Minh", Code = "HCM" },
                new { Id = 3, Name = "Đà Nẵng", Code = "DN" },
                new { Id = 4, Name = "Hải Phòng", Code = "HP" },
                new { Id = 5, Name = "Cần Thơ", Code = "CT" },
                new { Id = 6, Name = "An Giang", Code = "AG" },
                new { Id = 7, Name = "Bà Rịa - Vũng Tàu", Code = "BRVT" },
                new { Id = 8, Name = "Bắc Giang", Code = "BG" },
                new { Id = 9, Name = "Bắc Kạn", Code = "BK" },
                new { Id = 10, Name = "Bạc Liêu", Code = "BL" },
                new { Id = 11, Name = "Bắc Ninh", Code = "BN" },
                new { Id = 12, Name = "Bến Tre", Code = "BT" },
                new { Id = 13, Name = "Bình Định", Code = "BD" },
                new { Id = 14, Name = "Bình Dương", Code = "BDG" },
                new { Id = 15, Name = "Bình Phước", Code = "BP" },
                new { Id = 16, Name = "Bình Thuận", Code = "BTH" },
                new { Id = 17, Name = "Cà Mau", Code = "CM" },
                new { Id = 18, Name = "Cao Bằng", Code = "CB" },
                new { Id = 19, Name = "Đắk Lắk", Code = "DL" },
                new { Id = 20, Name = "Đắk Nông", Code = "DN2" },
                new { Id = 21, Name = "Điện Biên", Code = "DB" },
                new { Id = 22, Name = "Đồng Nai", Code = "DNA" },
                new { Id = 23, Name = "Đồng Tháp", Code = "DT" },
                new { Id = 24, Name = "Gia Lai", Code = "GL" },
                new { Id = 25, Name = "Hà Giang", Code = "HG" },
                new { Id = 26, Name = "Hà Nam", Code = "HNA" },
                new { Id = 27, Name = "Hà Tĩnh", Code = "HT" },
                new { Id = 28, Name = "Hải Dương", Code = "HD" },
                new { Id = 29, Name = "Hậu Giang", Code = "HGA" },
                new { Id = 30, Name = "Hòa Bình", Code = "HB" },
                new { Id = 31, Name = "Hưng Yên", Code = "HY" },
                new { Id = 32, Name = "Khánh Hòa", Code = "KH" },
                new { Id = 33, Name = "Kiên Giang", Code = "KG" },
                new { Id = 34, Name = "Kon Tum", Code = "KT" },
                new { Id = 35, Name = "Lai Châu", Code = "LC" },
                new { Id = 36, Name = "Lâm Đồng", Code = "LD" },
                new { Id = 37, Name = "Lạng Sơn", Code = "LS" },
                new { Id = 38, Name = "Lào Cai", Code = "LCA" },
                new { Id = 39, Name = "Long An", Code = "LA" },
                new { Id = 40, Name = "Nam Định", Code = "ND" },
                new { Id = 41, Name = "Nghệ An", Code = "NA" },
                new { Id = 42, Name = "Ninh Bình", Code = "NB" },
                new { Id = 43, Name = "Ninh Thuận", Code = "NT" },
                new { Id = 44, Name = "Phú Thọ", Code = "PT" },
                new { Id = 45, Name = "Phú Yên", Code = "PY" },
                new { Id = 46, Name = "Quảng Bình", Code = "QB" },
                new { Id = 47, Name = "Quảng Nam", Code = "QN" },
                new { Id = 48, Name = "Quảng Ngãi", Code = "QNG" },
                new { Id = 49, Name = "Quảng Ninh", Code = "QNI" },
                new { Id = 50, Name = "Quảng Trị", Code = "QT" },
                new { Id = 51, Name = "Sóc Trăng", Code = "ST" },
                new { Id = 52, Name = "Sơn La", Code = "SL" },
                new { Id = 53, Name = "Tây Ninh", Code = "TN" },
                new { Id = 54, Name = "Thái Bình", Code = "TB" },
                new { Id = 55, Name = "Thái Nguyên", Code = "TNG" },
                new { Id = 56, Name = "Thanh Hóa", Code = "TH" },
                new { Id = 57, Name = "Thừa Thiên Huế", Code = "TTH" },
                new { Id = 58, Name = "Tiền Giang", Code = "TG" },
                new { Id = 59, Name = "Trà Vinh", Code = "TV" },
                new { Id = 60, Name = "Tuyên Quang", Code = "TQ" },
                new { Id = 61, Name = "Vĩnh Long", Code = "VL" },
                new { Id = 62, Name = "Vĩnh Phúc", Code = "VP" },
                new { Id = 63, Name = "Yên Bái", Code = "YB" }
            };
            
            return Ok(new { 
                success = true, 
                message = "Danh sách tỉnh thành Việt Nam", 
                data = provinces 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi lấy danh sách tỉnh thành", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("accommodation-statuses")]
    public IActionResult GetAccommodationStatuses()
    {
        var statuses = new[]
        {
            new { Id = "ChoDuyet", Name = "Chờ duyệt", Description = "Đang chờ admin duyệt" },
            new { Id = "DaDuyet", Name = "Đã duyệt", Description = "Đã được duyệt và hoạt động" },
            new { Id = "TuChoi", Name = "Từ chối", Description = "Bị từ chối bởi admin" },
            new { Id = "TamNgung", Name = "Tạm ngưng", Description = "Tạm ngưng hoạt động" }
        };
        
        return Ok(new { 
            success = true, 
            message = "Danh sách trạng thái cơ sở lưu trú", 
            data = statuses 
        });
    }

    [HttpPost("addresses")]
    public async Task<IActionResult> CreateAddress([FromBody] dynamic body)
    {
        try
        {
            string? soNha = body?.soNha;
            string? phuong = body?.phuong;
            string? quan = body?.quan;
            string? thanhPho = body?.thanhPho;
            double? kenhDo = body?.kenhDo;
            double? viDo = body?.viDo;

            if (string.IsNullOrWhiteSpace(thanhPho))
                return BadRequest(new { success = false, message = "Thành phố là bắt buộc" });

            using var db = _factory.Create();
            
            // Kiểm tra bảng có tồn tại không
            var tableExists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DiaChiChiTiet'"
            );
            
            if (tableExists == 0)
            {
                return BadRequest(new { 
                    success = false, 
                    message = "Bảng DiaChiChiTiet không tồn tại trong database" 
                });
            }
            
            // Kiểm tra xem địa chỉ đã tồn tại chưa
            var existingId = await db.ExecuteScalarAsync<int?>(
                @"SELECT TOP 1 Id FROM DiaChiChiTiet 
                  WHERE ISNULL(SoNha,'') = ISNULL(@soNha,'') 
                    AND ISNULL(Phuong,'') = ISNULL(@phuong,'') 
                    AND ISNULL(Quan,'') = ISNULL(@quan,'') 
                    AND ThanhPho = @thanhPho",
                new { soNha, phuong, quan, thanhPho }
            );

            if (existingId.HasValue)
            {
                var existing = await db.QueryFirstOrDefaultAsync(
                    "SELECT * FROM DiaChiChiTiet WHERE Id = @id", 
                    new { id = existingId.Value }
                );
                
                return Ok(new { 
                    success = true, 
                    message = "Địa chỉ đã tồn tại", 
                    data = existing 
                });
            }

            // Tạo địa chỉ mới
            var sql = @"
                INSERT INTO DiaChiChiTiet (SoNha, Phuong, Quan, ThanhPho, KenhDo, ViDo)
                VALUES (@soNha, @phuong, @quan, @thanhPho, @kenhDo, @viDo);
                SELECT SCOPE_IDENTITY();
            ";

            var newId = await db.ExecuteScalarAsync<int>(sql, new { 
                soNha, phuong, quan, thanhPho, kenhDo, viDo 
            });

            var newAddress = await db.QueryFirstOrDefaultAsync(
                "SELECT * FROM DiaChiChiTiet WHERE Id = @id", 
                new { id = newId }
            );

            return StatusCode(201, new { 
                success = true, 
                message = "Tạo địa chỉ thành công", 
                data = newAddress 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi tạo địa chỉ", 
                error = ex.Message 
            });
        }
    }

    [HttpPut("addresses/{id}")]
    public async Task<IActionResult> UpdateAddress(int id, [FromBody] dynamic body)
    {
        try
        {
            string? soNha = body?.soNha;
            string? phuong = body?.phuong;
            string? quan = body?.quan;
            string? thanhPho = body?.thanhPho;
            double? kenhDo = body?.kenhDo;
            double? viDo = body?.viDo;

            if (string.IsNullOrWhiteSpace(thanhPho))
                return BadRequest(new { success = false, message = "Thành phố là bắt buộc" });

            using var db = _factory.Create();

            // Kiểm tra địa chỉ có tồn tại không
            var exists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM DiaChiChiTiet WHERE Id = @id", 
                new { id }
            );

            if (exists == 0)
                return NotFound(new { success = false, message = "Không tìm thấy địa chỉ" });

            // Cập nhật địa chỉ
            var sql = @"
                UPDATE DiaChiChiTiet 
                SET SoNha = @soNha, Phuong = @phuong, Quan = @quan, 
                    ThanhPho = @thanhPho, KenhDo = @kenhDo, ViDo = @viDo
                WHERE Id = @id
            ";

            await db.ExecuteAsync(sql, new { id, soNha, phuong, quan, thanhPho, kenhDo, viDo });

            var updated = await db.QueryFirstOrDefaultAsync(
                "SELECT * FROM DiaChiChiTiet WHERE Id = @id", 
                new { id }
            );

            return Ok(new { 
                success = true, 
                message = "Cập nhật địa chỉ thành công", 
                data = updated 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi cập nhật địa chỉ", 
                error = ex.Message 
            });
        }
    }

    [HttpDelete("addresses/{id}")]
    public async Task<IActionResult> DeleteAddress(int id)
    {
        try
        {
            using var db = _factory.Create();

            // Kiểm tra địa chỉ có tồn tại không
            var exists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM DiaChiChiTiet WHERE Id = @id", 
                new { id }
            );

            if (exists == 0)
                return NotFound(new { success = false, message = "Không tìm thấy địa chỉ" });

            // Kiểm tra xem có cơ sở lưu trú nào đang sử dụng địa chỉ này không
            var inUse = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM CoSoLuuTru WHERE IdDiaChi = @id", 
                new { id }
            );

            if (inUse > 0)
                return BadRequest(new { 
                    success = false, 
                    message = "Không thể xóa địa chỉ vì đang được sử dụng bởi cơ sở lưu trú" 
                });

            // Xóa địa chỉ
            await db.ExecuteAsync("DELETE FROM DiaChiChiTiet WHERE Id = @id", new { id });

            return Ok(new { 
                success = true, 
                message = "Xóa địa chỉ thành công" 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi xóa địa chỉ", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("cities")]
    public async Task<IActionResult> GetCities()
    {
        try
        {
            using var db = _factory.Create();
            
            // Kiểm tra bảng có tồn tại không
            var tableExists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DiaChiChiTiet'"
            );
            
            if (tableExists == 0)
            {
                return Ok(new { 
                    success = true, 
                    message = "Danh sách tỉnh/thành phố", 
                    data = new List<object>() 
                });
            }

            var cities = await db.QueryAsync(@"
                SELECT DISTINCT ThanhPho
                FROM DiaChiChiTiet 
                WHERE ThanhPho IS NOT NULL AND ThanhPho != ''
                ORDER BY ThanhPho
            ");

            return Ok(new { 
                success = true, 
                message = "Danh sách tỉnh/thành phố", 
                data = cities 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi lấy danh sách tỉnh/thành phố", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("districts")]
    public async Task<IActionResult> GetDistricts([FromQuery] string? thanhPho)
    {
        try
        {
            using var db = _factory.Create();
            
            var sql = @"
                SELECT DISTINCT Quan
                FROM DiaChiChiTiet 
                WHERE Quan IS NOT NULL AND Quan != ''
            ";
            
            object parameters = new { };
            if (!string.IsNullOrWhiteSpace(thanhPho))
            {
                sql += " AND ThanhPho = @thanhPho";
                parameters = new { thanhPho };
            }
            
            sql += " ORDER BY Quan";

            var districts = await db.QueryAsync(sql, parameters);

            return Ok(new { 
                success = true, 
                message = "Danh sách quận/huyện", 
                data = districts 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi lấy danh sách quận/huyện", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("wards")]
    public async Task<IActionResult> GetWards([FromQuery] string? thanhPho, [FromQuery] string? quan)
    {
        try
        {
            using var db = _factory.Create();
            
            var sql = @"
                SELECT DISTINCT Phuong
                FROM DiaChiChiTiet 
                WHERE Phuong IS NOT NULL AND Phuong != ''
            ";
            
            var conditions = new List<string>();
            var parameters = new Dictionary<string, object>();

            if (!string.IsNullOrWhiteSpace(thanhPho))
            {
                conditions.Add("ThanhPho = @thanhPho");
                parameters["thanhPho"] = thanhPho;
            }

            if (!string.IsNullOrWhiteSpace(quan))
            {
                conditions.Add("Quan = @quan");
                parameters["quan"] = quan;
            }

            if (conditions.Any())
            {
                sql += " AND " + string.Join(" AND ", conditions);
            }

            sql += " ORDER BY Phuong";

            var wards = await db.QueryAsync(sql, parameters);

            return Ok(new { 
                success = true, 
                message = "Danh sách phường/xã", 
                data = wards 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false, 
                message = "Lỗi khi lấy danh sách phường/xã", 
                error = ex.Message 
            });
        }
    }

    [HttpGet("banks")]
    public IActionResult GetBanks()
    {
        var banks = new[]
        {
            new { Ma = "VCB", Ten = "Vietcombank - Ngân hàng Ngoại thương Việt Nam" },
            new { Ma = "VTB", Ten = "Vietinbank - Ngân hàng Công thương Việt Nam" },
            new { Ma = "BIDV", Ten = "BIDV - Ngân hàng Đầu tư và Phát triển Việt Nam" },
            new { Ma = "ACB", Ten = "ACB - Ngân hàng Á Châu" },
            new { Ma = "TCB", Ten = "Techcombank - Ngân hàng Kỹ thương Việt Nam" },
            new { Ma = "MB", Ten = "MB Bank - Ngân hàng Quân đội" },
            new { Ma = "VPB", Ten = "VPBank - Ngân hàng Việt Nam Thịnh vượng" },
            new { Ma = "TPB", Ten = "TPBank - Ngân hàng Tiên Phong" },
            new { Ma = "STB", Ten = "Sacombank - Ngân hàng Sài Gòn Thương tín" },
            new { Ma = "HDB", Ten = "HDBank - Ngân hàng Phát triển TP.HCM" },
            new { Ma = "SHB", Ten = "SHB - Ngân hàng Sài Gòn - Hà Nội" },
            new { Ma = "EIB", Ten = "Eximbank - Ngân hàng Xuất nhập khẩu Việt Nam" },
            new { Ma = "MSB", Ten = "MSB - Ngân hàng Hàng hải" },
            new { Ma = "OCB", Ten = "OCB - Ngân hàng Phương Đông" },
            new { Ma = "SCB", Ten = "SCB - Ngân hàng Sài Gòn" }
        };

        return Ok(new { 
            success = true, 
            message = "Danh sách ngân hàng Việt Nam", 
            data = banks 
        });
    }
}
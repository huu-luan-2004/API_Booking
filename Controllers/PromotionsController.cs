using HotelBookingApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace HotelBookingApi.Controllers;

[ApiController]
[Route("api/promotions")]
public class PromotionsController : ControllerBase
{
	private readonly KhuyenMaiRepository _repo;
	private readonly PhongRepository _phongRepo;
	public PromotionsController(KhuyenMaiRepository repo, PhongRepository phongRepo)
	{
		_repo = repo; _phongRepo = phongRepo;
	}

	// Create promotion and optionally assign to rooms
	[HttpPost]
	[Authorize(Roles = "Admin,ChuCoSo")] // adjust roles as needed
	public async Task<IActionResult> Create([FromBody] JsonElement body)
	{
		var (cleanBody, roomIds) = SplitBody(body);
		if (cleanBody.ValueKind == JsonValueKind.Undefined || cleanBody.ValueKind == JsonValueKind.Null)
			return BadRequest(new { success=false, message="Payload không hợp lệ" });

		var promo = await _repo.CreateAsync(cleanBody, roomIds);
		return StatusCode(201, new { success=true, message="Tạo khuyến mãi thành công", data = promo });
	}

	// Update promotion and room assignments
	[HttpPut("{id:int}")]
	[Authorize(Roles = "Admin,ChuCoSo")]
	public async Task<IActionResult> Update([FromRoute] int id, [FromBody] JsonElement body)
	{
		var exists = await _repo.GetByIdAsync(id);
		if (exists is null) return NotFound(new { success=false, message="Không tìm thấy khuyến mãi" });
		var (cleanBody, roomIds) = SplitBody(body);
		var updated = await _repo.UpdateAsync(id, cleanBody, roomIds);
		return Ok(new { success=true, message="Cập nhật khuyến mãi thành công", data = updated });
	}

	// Delete promotion (and links)
	[HttpDelete("{id:int}")]
	[Authorize(Roles = "Admin")]
	public async Task<IActionResult> Delete([FromRoute] int id)
	{
		var exists = await _repo.GetByIdAsync(id);
		if (exists is null) return NotFound(new { success=false, message="Không tìm thấy khuyến mãi" });
		await _repo.DeleteAsync(id);
		return Ok(new { success=true, message="Đã xóa khuyến mãi" });
	}

	// Get promotion detail with room ids
	[HttpGet("{id:int}")]
	public async Task<IActionResult> Get([FromRoute] int id)
	{
		var promo = await _repo.GetByIdAsync(id);
		if (promo is null) return NotFound(new { success=false, message="Không tìm thấy khuyến mãi" });
		var rooms = await _repo.GetRoomIdsAsync(id);
		return Ok(new { success=true, data = new { KhuyenMai = promo, RoomIds = rooms } });
	}

	private static (JsonElement cleanBody, IEnumerable<int> roomIds) SplitBody(JsonElement body)
	{
		// Accept two shapes:
		// { khuyenMai: {...}, roomIds: [1,2,3] }
		// or a flat body with a property roomIds inside
		JsonElement promo = body;
		IEnumerable<int> ids = Array.Empty<int>();
		try
		{
			if (body.ValueKind == JsonValueKind.Object)
			{
				if (body.TryGetProperty("khuyenMai", out var kmElem))
				{
					promo = kmElem;
				}
				if (body.TryGetProperty("roomIds", out var idsElem) && idsElem.ValueKind == JsonValueKind.Array)
				{
					var list = new List<int>();
					foreach (var i in idsElem.EnumerateArray()) { if (i.TryGetInt32(out var n)) list.Add(n); }
					ids = list;
				}
			}
		}
		catch { }
		return (promo, ids);
	}
}

// Vai trò file: Chỉ Admin active được truy cập; policy đối chiếu DB thay vì chỉ tin role claim cũ.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Admin), Route("api/v1/admin")]
public sealed class AdminController(AdminService admin, ICurrentUser current) : ControllerBase
{
    // HeroPrice: PATCH giá hero và ghi audit qua AdminService.
    [HttpPatch("heroes/{id:guid}/price")] public async Task<ActionResult<AdminPriceDto>> HeroPrice(Guid id, UpdatePriceRequest request, CancellationToken ct) => Ok(await admin.UpdateHeroPriceAsync(current.UserId, id, request, ct));
    // CosmeticPrice: PATCH giá cosmetic và ghi audit.
    [HttpPatch("cosmetics/{id:guid}/price")] public async Task<ActionResult<AdminPriceDto>> CosmeticPrice(Guid id, UpdatePriceRequest request, CancellationToken ct) => Ok(await admin.UpdateCosmeticPriceAsync(current.UserId, id, request, ct));
    // Audit: GET lịch sử thao tác admin theo cursor.
    [HttpGet("audit")] public async Task<ActionResult<AdminAuditPageDto>> Audit([FromQuery] string? cursor, [FromQuery] int limit = 20, CancellationToken ct = default) => Ok(await admin.AuditAsync(cursor, limit, ct));
}

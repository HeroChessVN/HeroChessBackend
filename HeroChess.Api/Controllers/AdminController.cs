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
    [HttpPatch("heroes/{id:guid}/price")]
    [Authorize(Policy = AccountPolicies.ShopWrite)]
    public async Task<ActionResult<AdminPriceDto>> HeroPrice(Guid id, UpdatePriceRequest request, CancellationToken ct) => Ok(await admin.UpdateHeroPriceAsync(current.UserId, id, request, ct));
    // CosmeticPrice: PATCH giá cosmetic và ghi audit.
    [HttpPatch("cosmetics/{id:guid}/price")]
    [Authorize(Policy = AccountPolicies.ShopWrite)]
    public async Task<ActionResult<AdminPriceDto>> CosmeticPrice(Guid id, UpdatePriceRequest request, CancellationToken ct) => Ok(await admin.UpdateCosmeticPriceAsync(current.UserId, id, request, ct));
    // BoardPrice: PATCH giá board cosmetic (nội dung mới từ code upstream).
    [HttpPatch("board-cosmetics/{id:guid}/price")]
    [Authorize(Policy = AccountPolicies.ShopWrite)]
    public Task<AdminPriceDto> BoardPrice(Guid id, UpdatePriceRequest request, CancellationToken ct) =>
        admin.UpdateBoardPriceAsync(current.UserId, id, request, ct);
    // Audit: GET lịch sử thao tác admin theo cursor.
    [HttpGet("audit")]
    [Authorize(Policy = AccountPolicies.AuditRead)]
    public async Task<ActionResult<AdminAuditPageDto>> Audit([FromQuery] string? cursor, [FromQuery] int limit = 20,
        [FromQuery] string? action = null, [FromQuery] string? entityType = null, [FromQuery] string? entityId = null,
        [FromQuery] Guid? actorUserId = null, [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default) =>
        Ok(await admin.AuditAsync(cursor, limit, action, entityType, entityId, actorUserId, from, to, ct));
}

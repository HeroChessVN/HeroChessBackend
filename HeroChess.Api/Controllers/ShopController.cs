// Vai trò file: HTTP mua hero và xem cửa hàng; quyền sở hữu và giá được kiểm tra trong service.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1/shop")]
public sealed class ShopController(ShopService shop, ShopCatalogService catalog, ICurrentUser current) : ControllerBase
{
    [HttpPost("board-cosmetics/{id:guid}/purchase")]
    public Task<PurchaseBoardCosmeticDto> PurchaseBoard(Guid id, CancellationToken ct) =>
        shop.PurchaseBoardAsync(current.UserId, id, Request.Headers["Idempotency-Key"].FirstOrDefault(), ct);

    [HttpPost("heroes/{heroId:guid}/purchase")]
    // PurchaseHero: Đọc header Idempotency-Key và chuyển actor/hero sang ShopService.
    public async Task<ActionResult<PurchaseHeroDto>> PurchaseHero(Guid heroId, CancellationToken ct) =>
        Ok(await shop.PurchaseHeroAsync(current.UserId, heroId, Request.Headers["Idempotency-Key"].FirstOrDefault(), ct));

    // Heroes: Danh sách hero đang bán trong cửa hàng.
    [HttpGet("heroes")]
    public async Task<ActionResult<IReadOnlyList<HeroDto>>> Heroes(CancellationToken ct) =>
        Ok(await catalog.ListAsync(current.UserId, ct));

    // Owned: Hero người chơi đang sở hữu kèm nguồn nhận.
    [HttpGet("owned")]
    public async Task<ActionResult<IReadOnlyList<AdminOwnedHeroDto>>> Owned(CancellationToken ct) =>
        Ok(await catalog.OwnedAsync(current.UserId, ct));

    // Purchases: Lịch sử mua hero của người chơi.
    [HttpGet("purchases")]
    public async Task<ActionResult<IReadOnlyList<AdminPurchaseDto>>> Purchases(CancellationToken ct) =>
        Ok(await catalog.PurchasesAsync(current.UserId, ct));
}

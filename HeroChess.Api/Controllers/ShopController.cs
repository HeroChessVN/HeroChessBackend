// Vai trò file: HTTP mua hero; quyền sở hữu và giá được kiểm tra trong service.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1/shop")]
public sealed class ShopController(ShopService shop, ICurrentUser current) : ControllerBase
{
    [HttpPost("heroes/{heroId:guid}/purchase")]
    // PurchaseHero: Đọc header Idempotency-Key và chuyển actor/hero sang ShopService.
    public async Task<ActionResult<PurchaseHeroDto>> PurchaseHero(Guid heroId, CancellationToken ct) =>
        Ok(await shop.PurchaseHeroAsync(current.UserId, heroId, Request.Headers["Idempotency-Key"].FirstOrDefault(), ct));
}

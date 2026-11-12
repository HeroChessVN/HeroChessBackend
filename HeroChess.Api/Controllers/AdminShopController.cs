// Vai trò file: HTTP quản trị cửa hàng: danh sách, bật/tắt bán, đổi giá và lịch sử mua.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Admin)]
[Route("api/v1/admin/shop")]
public sealed class AdminShopController(AdminShopService shop, ICurrentUser current) : ControllerBase
{
    // Heroes: Danh sách hero trong cửa hàng kèm cờ mua được và số lượt đã mua.
    [HttpGet("heroes")]
    [Authorize(Policy = AccountPolicies.ShopRead)]
    public async Task<ActionResult<AdminShopHeroPageDto>> Heroes([FromQuery] string? query, [FromQuery] bool? inShop,
        [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await shop.ListAsync(query, inShop, cursor, limit, ct));

    // UpdateHero: Đổi giá và/hoặc bật/tắt bán một hero.
    [HttpPatch("heroes/{id:guid}")]
    [Authorize(Policy = AccountPolicies.ShopWrite)]
    public async Task<ActionResult<AdminHeroDto>> UpdateHero(Guid id, AdminShopUpdateRequest request, CancellationToken ct = default) =>
        Ok(await shop.UpdateAsync(current.UserId, id, request, ct));

    // BatchPrices: Đổi giá nhiều hero; lỗi từng dòng được báo riêng.
    [HttpPatch("prices")]
    [Authorize(Policy = AccountPolicies.ShopWrite)]
    public async Task<ActionResult<AdminBatchPriceResultDto>> BatchPrices(AdminBatchPriceRequest request, CancellationToken ct = default) =>
        Ok(await shop.BatchUpdatePricesAsync(current.UserId, request, ct));

    // Purchases: Lịch sử mua hero trong cửa hàng.
    [HttpGet("purchases")]
    [Authorize(Policy = AccountPolicies.ShopRead)]
    public async Task<ActionResult<AdminPurchasePageDto>> Purchases([FromQuery] Guid? heroId, [FromQuery] string? cursor,
        [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await shop.PurchasesAsync(heroId, cursor, limit, ct));
}
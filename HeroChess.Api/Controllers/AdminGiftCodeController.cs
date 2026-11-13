// Vai trò file: HTTP quản trị gift code (admin) và quy đổi mã (player).
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Admin)]
[Route("api/v1/admin/gift-codes")]
public sealed class AdminGiftCodeController(AdminGiftCodeService giftCodes, ICurrentUser current) : ControllerBase
{
    // GiftCodes: Danh sách gift code kèm số lượt đã quy đổi.
    [HttpGet]
    [Authorize(Policy = AccountPolicies.GiftCodeRead)]
    public async Task<ActionResult<AdminGiftCodePageDto>> GiftCodes([FromQuery] string? query, [FromQuery] bool? active,
        [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await giftCodes.ListAsync(query, active, cursor, limit, ct));

    // GiftCodeDetail: Chi tiết một gift code.
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AccountPolicies.GiftCodeRead)]
    public async Task<ActionResult<AdminGiftCodeDto>> GiftCodeDetail(Guid id, CancellationToken ct = default) =>
        Ok(await giftCodes.DetailAsync(id, ct));

    // CreateGiftCode: Tạo gift code; để trống Code để server sinh mã.
    [HttpPost]
    [Authorize(Policy = AccountPolicies.GiftCodeWrite)]
    public async Task<ActionResult<AdminGiftCodeDto>> CreateGiftCode(AdminGiftCodeUpsertRequest request, CancellationToken ct = default) =>
        Ok(await giftCodes.CreateAsync(current.UserId, request, ct));

    // UpdateGiftCode: Sửa cấu hình gift code.
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = AccountPolicies.GiftCodeWrite)]
    public async Task<ActionResult<AdminGiftCodeDto>> UpdateGiftCode(Guid id, AdminGiftCodeUpdateRequest request, CancellationToken ct = default) =>
        Ok(await giftCodes.UpdateAsync(current.UserId, id, request, ct));

    // Redemptions: Lịch sử quy đổi của một gift code.
    [HttpGet("{id:guid}/redemptions")]
    [Authorize(Policy = AccountPolicies.GiftCodeRead)]
    public async Task<ActionResult<AdminGiftCodeRedemptionPageDto>> Redemptions(Guid id, [FromQuery] string? cursor,
        [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await giftCodes.RedemptionsAsync(id, cursor, limit, ct));
}

[ApiController]
[Authorize(Policy = AccountPolicies.Player)]
[Route("api/v1/gift-codes")]
public sealed class GiftCodeController(AdminGiftCodeService giftCodes, ICurrentUser current) : ControllerBase
{
    // Redeem: Người chơi quy đổi mã gift code; transaction nguyên tử chống quy đổi trùng.
    [HttpPost("redeem")]
    public async Task<ActionResult<RedeemGiftCodeDto>> Redeem(RedeemGiftCodeRequest request, CancellationToken ct = default) =>
        Ok(await giftCodes.RedeemAsync(current.UserId, request, ct));
}
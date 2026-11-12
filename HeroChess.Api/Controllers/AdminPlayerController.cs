// Vai trò file: HTTP quản trị người chơi: tìm kiếm, hồ sơ, ban/mở khoá, điều chỉnh ví và bộ sưu tập.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Admin)]
[Route("api/v1/admin/players")]
public sealed class AdminPlayerController(AdminPlayerService players, ICurrentUser current) : ControllerBase
{
    // Players: Danh sách người chơi kèm Elo, số dư và số hero.
    [HttpGet]
    [Authorize(Policy = AccountPolicies.PlayerRead)]
    public async Task<ActionResult<AdminPlayerPageDto>> Players([FromQuery] string? query, [FromQuery] string? status,
        [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await players.ListAsync(query, status, cursor, limit, ct));

    // PlayerDetail: Hồ sơ đầy đủ của một người chơi.
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AccountPolicies.PlayerRead)]
    public async Task<ActionResult<AdminPlayerDetailDto>> PlayerDetail(Guid id, CancellationToken ct = default) =>
        Ok(await players.DetailAsync(id, ct));

    // SetStatus: Ban hoặc mở khoá tài khoản; lý do bắt buộc.
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AccountPolicies.PlayerWrite)]
    public async Task<ActionResult<AdminPlayerDto>> SetStatus(Guid id, AdminPlayerStatusRequest request, CancellationToken ct = default) =>
        Ok(await players.SetStatusAsync(current.UserId, id, request, ct));

    // AdjustCoins: Cộng/trừ coin thủ công.
    [HttpPost("{id:guid}/coins")]
    [Authorize(Policy = AccountPolicies.PlayerWrite)]
    public async Task<ActionResult<AdminCoinAdjustmentDto>> AdjustCoins(Guid id, AdminCoinAdjustmentRequest request, CancellationToken ct = default) =>
        Ok(await players.AdjustCoinsAsync(current.UserId, id, request, ct));

    // GrantHero: Cấp hero cho người chơi.
    [HttpPost("{id:guid}/heroes")]
    [Authorize(Policy = AccountPolicies.PlayerWrite)]
    public async Task<ActionResult<AdminOwnedHeroDto>> GrantHero(Guid id, AdminGrantHeroRequest request, CancellationToken ct = default) =>
        Ok(await players.GrantHeroAsync(current.UserId, id, request, ct));

    // RevokeHero: Thu hero do admin cấp.
    [HttpDelete("{id:guid}/heroes/{heroId:guid}")]
    [Authorize(Policy = AccountPolicies.PlayerWrite)]
    public async Task<ActionResult<AdminOwnedHeroDto>> RevokeHero(Guid id, Guid heroId, [FromQuery] string? reason, CancellationToken ct = default) =>
        Ok(await players.RevokeHeroAsync(current.UserId, id, heroId, reason, ct));

    // Transactions: Lịch sử biến động ví của một người chơi.
    [HttpGet("{id:guid}/transactions")]
    [Authorize(Policy = AccountPolicies.PlayerRead)]
    public async Task<ActionResult<IReadOnlyList<AdminWalletTransactionDto>>> Transactions(Guid id, CancellationToken ct = default) =>
        Ok(await players.TransactionsAsync(id, ct));
}
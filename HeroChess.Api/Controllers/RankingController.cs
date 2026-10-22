// Vai trò file: Bảng xếp hạng công khai và hạng của người chơi hiện tại; không nhận playerId tự khai trong request.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1/ranking")]
public sealed class RankingController(RankingService ranking, ICurrentUser current) : ControllerBase
{
    // Leaderboard: GET bảng xếp hạng theo Elo, phân trang bằng cursor.
    [HttpGet]
    public async Task<ActionResult<RankingPageDto>> Leaderboard([FromQuery] string? cursor = null, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await ranking.LeaderboardAsync(limit, cursor, ct));

    // Me: GET hạng và rating của người chơi đang đăng nhập; lấy identity từ token.
    [HttpGet("me")]
    public async Task<ActionResult<MyRankingDto>> Me(CancellationToken ct = default) =>
        Ok(await ranking.MeAsync(current.UserId, ct));
}

// Vai trò file: HTTP số liệu admin: dashboard, cân bằng và bot. Chỉ đọc, không sinh số liệu giả.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Admin)]
[Route("api/v1/admin")]
public sealed class AdminAnalyticsController(AdminAnalyticsService analytics) : ControllerBase
{
    // Stats: Số liệu tổng quan cho dashboard.
    [HttpGet("stats")]
    [Authorize(Policy = AccountPolicies.ReadAll)]
    public async Task<ActionResult<AdminStatsDto>> Stats(CancellationToken ct = default) =>
        Ok(await analytics.StatsAsync(ct));

    // Balance: Báo cáo cân bằng theo mode (ranked hoặc bot).
    [HttpGet("analytics/balance")]
    [Authorize(Policy = AccountPolicies.AnalyticsRead)]
    public async Task<ActionResult<AdminBalanceAnalyticsDto>> Balance([FromQuery] string mode = "ranked",
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, CancellationToken ct = default) =>
        Ok(await analytics.BalanceAsync(mode, from, to, ct));

    // Bot: Báo cáo hiệu suất bot, tách khỏi cân bằng PvP.
    [HttpGet("analytics/bot")]
    [Authorize(Policy = AccountPolicies.AnalyticsRead)]
    public async Task<ActionResult<AdminBotAnalyticsDto>> Bot([FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null, CancellationToken ct = default) =>
        Ok(await analytics.BotAnalyticsAsync(from, to, ct));
}
// Vai trò file: HTTP tra cứu trận cho admin. Chỉ đọc; không nới quyền xem trận của người chơi.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Admin)]
[Route("api/v1/admin/matches")]
public sealed class AdminMatchController(AdminMatchService matches) : ControllerBase
{
    // Matches: Danh sách trận với bộ lọc mode/status/thời gian/người chơi.
    [HttpGet]
    [Authorize(Policy = AccountPolicies.AnalyticsRead)]
    public async Task<ActionResult<AdminMatchPageDto>> Matches([FromQuery] string? mode, [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] Guid? playerId,
        [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await matches.ListAsync(mode, status, from, to, playerId, cursor, limit, ct));

    // MatchDetail: Chi tiết trận gồm snapshot luật và lineup đã đóng băng.
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AccountPolicies.AnalyticsRead)]
    public async Task<ActionResult<AdminMatchDetailDto>> MatchDetail(Guid id, CancellationToken ct = default) =>
        Ok(await matches.DetailAsync(id, ct));

    // Actions: Nhật ký hành động theo sequence.
    [HttpGet("{id:guid}/actions")]
    [Authorize(Policy = AccountPolicies.AnalyticsRead)]
    public async Task<ActionResult<IReadOnlyList<AdminMatchActionDto>>> Actions(Guid id,
        [FromQuery] int afterSequence = -1, [FromQuery] int limit = 100, CancellationToken ct = default) =>
        Ok(await matches.ActionsAsync(id, afterSequence, limit, ct));
}
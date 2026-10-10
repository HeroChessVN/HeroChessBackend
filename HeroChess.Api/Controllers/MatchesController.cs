// Vai trò file: HTTP đọc trận/replay và command fallback; REST và WS dùng chung MatchCommandService.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1/matches")]
public sealed class MatchesController(ICurrentUser current, MatchReadService read, MatchCommandService commands, MatchHistoryService history) : ControllerBase
{
    // State: GET snapshot hiện tại có kiểm tra participant.
    [HttpGet("{id:guid}/state")] public async Task<ActionResult<MatchStateDto>> State(Guid id, CancellationToken ct) => Ok(await read.StateAsync(current.UserId, id, ct));
    // Legal: GET legal moves từ Rules server.
    [HttpGet("{id:guid}/legal-actions")] public async Task<ActionResult<IReadOnlyList<LegalMoveDto>>> Legal(Guid id, CancellationToken ct) => Ok(await read.LegalAsync(current.UserId, id, ct));
    [HttpGet("{id:guid}/hero-actions")] public async Task<ActionResult<IReadOnlyList<LegalMoveDto>>> HeroActions(Guid id, [FromQuery] Guid pieceId, CancellationToken ct) => Ok(await read.HeroActionsAsync(current.UserId, id, pieceId, ct));
    // Command: POST action qua pipeline có dedup/version/transaction.
    [HttpPost("{id:guid}/commands")] public async Task<ActionResult<MatchCommandResultDto>> Command(Guid id, MatchCommandRequest request, CancellationToken ct) => Ok(await commands.ExecuteAsync(current.UserId, id, request, null, ct));
    // List: GET lịch sử trận của actor.
    [HttpGet] public async Task<ActionResult<MatchPageDto>> List([FromQuery] string? cursor, [FromQuery] int limit = 20, CancellationToken ct = default) => Ok(await history.ListAsync(current.UserId, cursor, limit, ct));
    // Replay: GET trang action đã lưu của trận đã bắt đầu, chỉ cho participant.
    [HttpGet("{id:guid}/replay")] public async Task<ActionResult<ReplayPageDto>> Replay(Guid id, [FromQuery] int afterSequence = -1, [FromQuery] int limit = 50, CancellationToken ct = default) => Ok(await history.ReplayAsync(current.UserId, id, afterSequence, limit, ct));
}

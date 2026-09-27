// Vai trò file: HTTP chọn/confirm/hủy selection trước khi trận bắt đầu.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1/matches/{matchId:guid}")]
public sealed class MatchSelectionController(MatchSelectionService service, MatchSelectionLifetime lifetime, ICurrentUser current) : ControllerBase
{
    // Get: GET selection đã lọc dữ liệu bí mật đối thủ.
    [HttpGet("selection")] public async Task<ActionResult<MatchSelectionDto>> Get(Guid matchId, CancellationToken ct) => Ok(await service.GetAsync(current.UserId, matchId, ct));
    // Select: PUT lineup ID + revision vào selection.
    [HttpPut("selection")] public async Task<ActionResult<MatchSelectionDto>> Select(Guid matchId, SelectLineupRequest request, CancellationToken ct) => Ok(await service.SelectAsync(current.UserId, matchId, request, ct));
    // Confirm: POST khóa lựa chọn; đủ hai bên thì tạo state start.
    [HttpPost("confirm")] public async Task<ActionResult<MatchSelectionDto>> Confirm(Guid matchId, CancellationToken ct) => Ok(await service.ConfirmAsync(current.UserId, matchId, ct));
    // Cancel: DELETE selection của participant; active match phải resign qua command.
    [HttpDelete("selection")] public async Task<IActionResult> Cancel(Guid matchId, CancellationToken ct)
    { await lifetime.CancelAsync(matchId, current.UserId, null, ct); return NoContent(); }
}

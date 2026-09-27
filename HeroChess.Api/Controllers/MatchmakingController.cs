// Vai trò file: Tạo/poll/hủy ticket ghép trận; chưa phải controller sửa state bàn cờ.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1/matchmaking/tickets")]
public sealed class MatchmakingController(MatchmakingService service, ICurrentUser current) : ControllerBase
{
    [HttpPost]
    // Create: POST ranked/bot ticket, trả 201.
    public async Task<ActionResult<MatchmakingTicketDto>> Create(CreateMatchmakingTicketRequest request, CancellationToken ct)
    {
        var ticket = await service.CreateAsync(current.UserId, request.Mode, ct);
        return Created($"/api/v1/matchmaking/tickets/{ticket.TicketId}", ticket);
    }
    // Get: GET trạng thái ticket thuộc actor.
    [HttpGet("{id:guid}")] public ActionResult<MatchmakingTicketDto> Get(Guid id) => Ok(service.Get(current.UserId, id));
    // Cancel: DELETE ticket còn waiting; không hủy trận đã active.
    [HttpDelete("{id:guid}")] public IActionResult Cancel(Guid id) { service.Cancel(current.UserId, id); return NoContent(); }
}

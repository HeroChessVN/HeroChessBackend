// Vai trò file: HTTP có bearer auth dùng để xin vé mở WebSocket.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1/ws-ticket")]
public sealed class WsTicketController(WsTicketService tickets, ICurrentUser current) : ControllerBase
{
    // Issue: POST cấp vé một lần và thời gian hết hạn cho actor hiện tại.
    [HttpPost] public ActionResult<WsTicketDto> Issue() { var issued = tickets.Issue(current.UserId); return Ok(new WsTicketDto(issued.Ticket, issued.ExpiresAt)); }
}

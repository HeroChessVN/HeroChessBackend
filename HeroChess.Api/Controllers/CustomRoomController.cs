using HeroChess.Api.Auth;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1/matchmaking/rooms")]
public sealed class CustomRoomController(MatchmakingService service, ICurrentUser current) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CustomRoomDto>> Create(CancellationToken ct)
    {
        var room = await service.CreateRoomAsync(current.UserId, ct);
        return CreatedAtAction(nameof(Get), new { roomId = room.RoomId }, room);
    }

    [HttpGet("current")]
    public async Task<ActionResult<CustomRoomDto>> Current(CancellationToken ct)
    {
        var room = await service.ReadRoomAsync(current.UserId, null, ct);
        return room is null ? NoContent() : Ok(room);
    }

    [HttpGet("{roomId:guid}")]
    public async Task<ActionResult<CustomRoomDto>> Get(Guid roomId, CancellationToken ct) =>
        Ok(await service.ReadRoomAsync(current.UserId, roomId, ct) ??
            throw new ApiException(404, "ROOM_NOT_FOUND", "The room was not found."));

    [HttpPost("join")]
    public async Task<ActionResult<CustomRoomDto>> Join(JoinCustomRoomRequest request, CancellationToken ct) =>
        Ok(await service.JoinRoomAsync(current.UserId, request.Code, ct));

    [HttpPost("{roomId:guid}/leave")]
    public async Task<IActionResult> Leave(Guid roomId, LeaveCustomRoomRequest request, CancellationToken ct)
    {
        await service.LeaveRoomAsync(current.UserId, roomId, request.Reason, ct);
        return NoContent();
    }

    [HttpPost("{roomId:guid}/presence")]
    public async Task<IActionResult> Presence(Guid roomId, CancellationToken ct)
    {
        await service.RoomPresenceAsync(current.UserId, roomId, ct);
        return NoContent();
    }

    [HttpPost("{roomId:guid}/rematch")]
    public async Task<ActionResult<CustomRoomDto>> Rematch(Guid roomId, RematchCustomRoomRequest request, CancellationToken ct) =>
        Ok(await service.RematchRoomAsync(current.UserId, roomId, request.PreviousMatchId, request.RequestId, ct));
}
